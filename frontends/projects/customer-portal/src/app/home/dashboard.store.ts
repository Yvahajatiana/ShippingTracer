import { inject } from '@angular/core';
import {
  ApiError,
  BillingApi,
  MAX_PAGE_SIZE,
  Shipment,
  ShipmentApi,
  fetchAllPages,
  toApiError,
} from '@parcelflow/shared';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, catchError, forkJoin, pipe, switchMap, tap } from 'rxjs';

export const RECENT_SHIPMENTS = 5;
/**
 * Billing caps pages at 100 items and GET /invoices has no status filter: the counter pages
 * through the customer's invoices (bounded by fetchAllPages) and counts the pending ones.
 */
export const INVOICE_SCAN_SIZE = MAX_PAGE_SIZE;

interface DashboardState {
  recentShipments: Shipment[];
  pendingInvoices: number;
  /** False when the scan stopped at the page bound: the count is then a lower bound. */
  pendingInvoicesComplete: boolean;
  loading: boolean;
  loaded: boolean;
  error: ApiError | null;
}

const initialState: DashboardState = {
  recentShipments: [],
  pendingInvoices: 0,
  pendingInvoicesComplete: true,
  loading: false,
  loaded: false,
  error: null,
};

/** Signed-in customer summary shown on the landing page. */
export const DashboardStore = signalStore(
  withState(initialState),
  withMethods((store, shipments = inject(ShipmentApi), billing = inject(BillingApi)) => ({
    /** Loads the dashboard while `active` is true (signed-in customer), resets it otherwise. */
    load: rxMethod<boolean>(
      pipe(
        tap((active) => patchState(store, { ...initialState, loading: active })),
        switchMap((active) =>
          !active
            ? EMPTY
            : forkJoin({
                recent: shipments.list(0, RECENT_SHIPMENTS),
                invoices: fetchAllPages((page, pageSize) => billing.invoices(page, pageSize), {
                  pageSize: INVOICE_SCAN_SIZE,
                }),
              }).pipe(
                tap(({ recent, invoices }) =>
                  patchState(store, {
                    recentShipments: recent,
                    pendingInvoices: invoices.items.filter(
                      (invoice) => invoice.status === 'PendingPayment',
                    ).length,
                    pendingInvoicesComplete: invoices.complete,
                    loading: false,
                    loaded: true,
                  }),
                ),
                catchError((error: unknown) => {
                  patchState(store, { loading: false, loaded: true, error: toApiError(error) });
                  return EMPTY;
                }),
              ),
        ),
      ),
    ),
  })),
);
