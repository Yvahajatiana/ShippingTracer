import { inject } from '@angular/core';
import { ApiError, BillingApi, Shipment, ShipmentApi, toApiError } from '@parcelflow/shared';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, catchError, forkJoin, pipe, switchMap, tap } from 'rxjs';

export const RECENT_SHIPMENTS = 5;
/** Billing caps pages at 100 items; pending invoices beyond that are not counted. */
export const INVOICE_SCAN_SIZE = 100;

interface DashboardState {
  recentShipments: Shipment[];
  pendingInvoices: number;
  loading: boolean;
  loaded: boolean;
  error: ApiError | null;
}

const initialState: DashboardState = {
  recentShipments: [],
  pendingInvoices: 0,
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
                invoices: billing.invoices(1, INVOICE_SCAN_SIZE),
              }).pipe(
                tap(({ recent, invoices }) =>
                  patchState(store, {
                    recentShipments: recent,
                    pendingInvoices: invoices.items.filter(
                      (invoice) => invoice.status === 'PendingPayment',
                    ).length,
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
