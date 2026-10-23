import { computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import {
  BillingApi,
  CustomerApi,
  DeliveryApi,
  KpiReport,
  ReportingApi,
  WarehouseApi,
  toIsoDate,
} from '@parcelflow/shared';
import { EMPTY, Observable, catchError, forkJoin, map, of, pipe, switchMap, tap } from 'rxjs';

/** Window of the KPI summary, in days, ending today. */
export const KPI_WINDOW_DAYS = 30;
/** Invoices scanned to count those awaiting payment (API maximum page size). */
export const INVOICE_SCAN_SIZE = 100;

/** Section counters; null when the count could not be loaded. */
export interface AdminCounters {
  companies: number | null;
  warehouses: number | null;
  drivers: number | null;
  pendingInvoices: number | null;
}

interface OverviewState {
  report: KpiReport | null;
  loading: boolean;
  error: unknown;
  counters: AdminCounters;
  countersLoading: boolean;
}

const EMPTY_COUNTERS: AdminCounters = {
  companies: null,
  warehouses: null,
  drivers: null,
  pendingInvoices: null,
};

const initialState: OverviewState = {
  report: null,
  loading: false,
  error: null,
  counters: EMPTY_COUNTERS,
  countersLoading: false,
};

/** A failing counter must not break the whole dashboard. */
function orNull(source: Observable<number>): Observable<number | null> {
  return source.pipe(catchError(() => of(null)));
}

/** Inclusive date range of the last `days` days, as DateOnly strings. */
export function lastDays(days: number, today = new Date()): { from: string; to: string } {
  const from = new Date(today);
  from.setDate(from.getDate() - (days - 1));
  return { from: toIsoDate(from), to: toIsoDate(today) };
}

/** Fills missing days with zero so the chart shows a continuous timeline. */
export function dailySeries(report: KpiReport): { dates: string[]; values: number[] } {
  const byDate = new Map(report.dailyVolumes.map((item) => [item.date, item.shipments]));
  const dates: string[] = [];
  const cursor = new Date(`${report.from}T00:00:00Z`);
  const end = new Date(`${report.to}T00:00:00Z`);
  while (cursor <= end && dates.length <= 366) {
    dates.push(cursor.toISOString().slice(0, 10));
    cursor.setUTCDate(cursor.getUTCDate() + 1);
  }
  return { dates, values: dates.map((date) => byDate.get(date) ?? 0) };
}

export const OverviewStore = signalStore(
  withState(initialState),
  withComputed(({ report }) => ({
    daily: computed(() => {
      const current = report();
      return current ? dailySeries(current) : { dates: [], values: [] };
    }),
  })),
  withMethods(
    (
      store,
      reporting = inject(ReportingApi),
      customers = inject(CustomerApi),
      warehouses = inject(WarehouseApi),
      delivery = inject(DeliveryApi),
      billing = inject(BillingApi),
    ) => ({
      loadReport: rxMethod<void>(
        pipe(
          tap(() => patchState(store, { loading: true, error: null })),
          switchMap(() => {
            const { from, to } = lastDays(KPI_WINDOW_DAYS);
            return reporting.kpis(from, to).pipe(
              tap((report) => patchState(store, { report, loading: false })),
              catchError((error: unknown) => {
                patchState(store, { loading: false, error });
                return EMPTY;
              }),
            );
          }),
        ),
      ),
      loadCounters: rxMethod<void>(
        pipe(
          tap(() => patchState(store, { countersLoading: true })),
          switchMap(() =>
            forkJoin({
              companies: orNull(customers.listCompanies().pipe(map((items) => items.length))),
              warehouses: orNull(warehouses.list(1, 1).pipe(map((page) => page.total))),
              drivers: orNull(delivery.drivers(1, 1).pipe(map((page) => page.total))),
              pendingInvoices: orNull(
                billing
                  .invoices(1, INVOICE_SCAN_SIZE)
                  .pipe(
                    map(
                      (page) =>
                        page.items.filter((invoice) => invoice.status === 'PendingPayment').length,
                    ),
                  ),
              ),
            }).pipe(tap((counters) => patchState(store, { counters, countersLoading: false }))),
          ),
        ),
      ),
    }),
  ),
);
