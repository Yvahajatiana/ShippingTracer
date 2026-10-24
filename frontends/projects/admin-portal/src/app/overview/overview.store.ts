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
  addDays,
  fetchAllPages,
  toIsoDate,
} from '@parcelflow/shared';
import {
  EMPTY,
  Observable,
  catchError,
  forkJoin,
  map,
  merge,
  of,
  pipe,
  switchMap,
  tap,
} from 'rxjs';

/** Window of the KPI summary, in days, ending today. */
export const KPI_WINDOW_DAYS = 30;

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
  /** The pending-invoice count pages through all invoices, so it loads on its own. */
  pendingInvoicesLoading: boolean;
  /** False when the scan stopped at the page bound: the count is then a lower bound. */
  pendingInvoicesComplete: boolean;
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
  pendingInvoicesLoading: false,
  pendingInvoicesComplete: true,
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

/** Last day covered by a report: Reporting echoes the EXCLUSIVE `to` it received. */
export function reportLastDay(report: KpiReport): string {
  return addDays(report.to, -1);
}

/** Fills missing days of `[from, to)` with zero so the chart shows a continuous timeline. */
export function dailySeries(report: KpiReport): { dates: string[]; values: number[] } {
  const byDate = new Map(report.dailyVolumes.map((item) => [item.date, item.shipments]));
  const dates: string[] = [];
  const cursor = new Date(`${report.from}T00:00:00Z`);
  const end = new Date(`${report.to}T00:00:00Z`);
  while (cursor < end && dates.length < 366) {
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
    /** Inclusive last day of the displayed window. */
    lastDay: computed(() => {
      const current = report();
      return current ? reportLastDay(current) : null;
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
            // Reporting treats `to` as exclusive: send the day after the inclusive end.
            return reporting.kpis(from, addDays(to, 1)).pipe(
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
          tap(() =>
            patchState(store, {
              countersLoading: true,
              pendingInvoicesLoading: true,
              pendingInvoicesComplete: true,
            }),
          ),
          switchMap(() =>
            merge(
              forkJoin({
                companies: orNull(customers.listCompanies().pipe(map((items) => items.length))),
                warehouses: orNull(warehouses.list(1, 1).pipe(map((page) => page.total))),
                drivers: orNull(delivery.drivers(1, 1).pipe(map((page) => page.total))),
              }).pipe(
                tap((counts) =>
                  patchState(store, (state) => ({
                    counters: { ...state.counters, ...counts },
                    countersLoading: false,
                  })),
                ),
              ),
              // Billing has no status filter on GET /invoices: page through every invoice
              // (bounded by fetchAllPages) and count the pending ones.
              fetchAllPages((page, pageSize) => billing.invoices(page, pageSize)).pipe(
                map((all) => ({
                  count: all.items.filter((invoice) => invoice.status === 'PendingPayment')
                    .length as number | null,
                  complete: all.complete,
                })),
                catchError(() => of({ count: null, complete: true })),
                tap(({ count, complete }) =>
                  patchState(store, (state) => ({
                    counters: { ...state.counters, pendingInvoices: count },
                    pendingInvoicesLoading: false,
                    pendingInvoicesComplete: complete,
                  })),
                ),
              ),
            ),
          ),
        ),
      ),
    }),
  ),
);
