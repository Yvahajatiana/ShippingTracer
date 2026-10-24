import { computed, inject } from '@angular/core';
import {
  ApiError,
  BillingApi,
  Guid,
  Invoice,
  NotificationApi,
  NotificationRecord,
  PublicTracking,
  Shipment,
  ShipmentApi,
  TrackingApi,
  saveFile,
  toApiError,
} from '@parcelflow/shared';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, catchError, merge, pipe, switchMap, tap } from 'rxjs';
import { Result, attempt } from '../core/result';

interface ShipmentDetailState {
  shipment: Shipment | null;
  loading: boolean;
  error: ApiError | null;
  tracking: PublicTracking | null;
  trackingLoading: boolean;
  trackingError: ApiError | null;
  invoice: Invoice | null;
  invoiceLoading: boolean;
  notifications: NotificationRecord[];
  notificationsLoading: boolean;
  notificationsError: ApiError | null;
  downloading: boolean;
}

const initialState: ShipmentDetailState = {
  shipment: null,
  loading: false,
  error: null,
  tracking: null,
  trackingLoading: false,
  trackingError: null,
  invoice: null,
  invoiceLoading: false,
  notifications: [],
  notificationsLoading: false,
  notificationsError: null,
  downloading: false,
};

/**
 * One shipment with its tracking timeline, invoice and notifications.
 * Secondary sections fail independently so the shipment details stay usable.
 */
export const ShipmentDetailStore = signalStore(
  withState(initialState),
  withComputed(({ tracking }) => ({
    timeline: computed(() => [...(tracking()?.history ?? [])].reverse()),
  })),
  withMethods(
    (
      store,
      shipments = inject(ShipmentApi),
      trackingApi = inject(TrackingApi),
      billing = inject(BillingApi),
      notificationsApi = inject(NotificationApi),
    ) => {
      const loadTracking = (shipment: Shipment) =>
        trackingApi.publicTracking(shipment.trackingNumber).pipe(
          tap((tracking) => patchState(store, { tracking, trackingLoading: false })),
          catchError((error: unknown) => {
            patchState(store, { trackingLoading: false, trackingError: toApiError(error) });
            return EMPTY;
          }),
        );
      const loadInvoice = (shipment: Shipment) =>
        // Billing keys each invoice by its shipment: the invoice id is the shipment id.
        billing.invoice(shipment.id).pipe(
          tap((invoice) => patchState(store, { invoice, invoiceLoading: false })),
          catchError(() => {
            // The invoice link is optional: hide it when the shipment has no invoice yet (404)
            // or when Billing is unavailable.
            patchState(store, { invoice: null, invoiceLoading: false });
            return EMPTY;
          }),
        );
      const loadNotifications = (shipment: Shipment) =>
        notificationsApi.list(shipment.id).pipe(
          tap((page) =>
            patchState(store, { notifications: page.items, notificationsLoading: false }),
          ),
          catchError((error: unknown) => {
            patchState(store, {
              notificationsLoading: false,
              notificationsError: toApiError(error),
            });
            return EMPTY;
          }),
        );

      return {
        load: rxMethod<Guid>(
          pipe(
            tap(() => patchState(store, { ...initialState, loading: true })),
            switchMap((id) =>
              shipments.get(id).pipe(
                tap((shipment) =>
                  patchState(store, {
                    shipment,
                    loading: false,
                    trackingLoading: true,
                    invoiceLoading: true,
                    notificationsLoading: true,
                  }),
                ),
                switchMap((shipment) =>
                  merge(loadTracking(shipment), loadInvoice(shipment), loadNotifications(shipment)),
                ),
                catchError((error: unknown) => {
                  patchState(store, { loading: false, error: toApiError(error) });
                  return EMPTY;
                }),
              ),
            ),
          ),
        ),

        /** Downloads the PDF label through the browser. */
        async downloadLabel(): Promise<Result<void>> {
          const shipment = store.shipment();
          if (!shipment) {
            return { ok: true, value: undefined };
          }
          patchState(store, { downloading: true });
          const result = await attempt(shipments.label(shipment.id));
          patchState(store, { downloading: false });
          if (!result.ok) {
            return result;
          }
          saveFile(result.value);
          return { ok: true, value: undefined };
        },
      };
    },
  ),
);
