import { computed, inject } from '@angular/core';
import {
  patchState,
  signalStore,
  withComputed,
  withHooks,
  withMethods,
  withState,
} from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import {
  Guid,
  InternalTracking,
  Notifier,
  Shipment,
  ShipmentApi,
  TrackingApi,
  saveFile,
} from '@parcelflow/shared';
import { EMPTY, catchError, firstValueFrom, pipe, switchMap, tap } from 'rxjs';
import { sliceFetcher, withPagedList } from '../core/paged-list.feature';

export const SHIPMENT_PAGE_SIZE = 20;

/** Paged shipment list with a tracking-number filter applied to the loaded page. */
export const ShipmentListStore = signalStore(
  withPagedList<Shipment>(() => {
    const api = inject(ShipmentApi);
    return sliceFetcher((offset, limit) => api.list(offset, limit));
  }, SHIPMENT_PAGE_SIZE),
  withState({ query: '' }),
  withComputed(({ items, query }) => ({
    filtered: computed(() => {
      const term = query().trim().toUpperCase();
      return term ? items().filter((item) => item.trackingNumber.includes(term)) : items();
    }),
  })),
  withMethods((store) => ({
    setQuery(query: string): void {
      patchState(store, { query });
    },
  })),
  withHooks({
    onInit(store) {
      store.loadPage(1);
    },
  }),
);

interface ShipmentDetailState {
  shipment: Shipment | null;
  loading: boolean;
  error: unknown;
  tracking: InternalTracking | null;
  trackingLoading: boolean;
  trackingError: unknown;
  downloading: boolean;
}

/** Shipment details and the internal tracking history. */
export const ShipmentDetailStore = signalStore(
  withState<ShipmentDetailState>({
    shipment: null,
    loading: false,
    error: null,
    tracking: null,
    trackingLoading: false,
    trackingError: null,
    downloading: false,
  }),
  withComputed(({ tracking }) => ({
    /** Most recent event first. */
    timeline: computed(() =>
      [...(tracking()?.history ?? [])].sort((a, b) =>
        b.occurredAtUtc.localeCompare(a.occurredAtUtc),
      ),
    ),
  })),
  withMethods(
    (
      store,
      shipments = inject(ShipmentApi),
      trackingApi = inject(TrackingApi),
      notifier = inject(Notifier),
    ) => {
      const loadShipment = rxMethod<Guid>(
        pipe(
          tap(() => patchState(store, { loading: true, error: null })),
          switchMap((id) =>
            shipments.get(id).pipe(
              tap((shipment) => patchState(store, { shipment, loading: false })),
              catchError((error: unknown) => {
                patchState(store, { loading: false, error, shipment: null });
                return EMPTY;
              }),
            ),
          ),
        ),
      );
      const loadTracking = rxMethod<Guid>(
        pipe(
          tap(() => patchState(store, { trackingLoading: true, trackingError: null })),
          switchMap((id) =>
            trackingApi.shipmentTracking(id).pipe(
              tap((tracking) => patchState(store, { tracking, trackingLoading: false })),
              catchError((trackingError: unknown) => {
                patchState(store, { trackingLoading: false, trackingError, tracking: null });
                return EMPTY;
              }),
            ),
          ),
        ),
      );
      return {
        load(id: Guid): void {
          loadShipment(id);
          loadTracking(id);
        },
        reloadTracking(): void {
          const id = store.shipment()?.id;
          if (id) {
            loadTracking(id);
          }
        },
        async downloadLabel(): Promise<void> {
          const shipment = store.shipment();
          if (!shipment || store.downloading()) {
            return;
          }
          patchState(store, { downloading: true });
          try {
            saveFile(await firstValueFrom(shipments.label(shipment.id)));
          } catch (error) {
            notifier.error(error);
          } finally {
            patchState(store, { downloading: false });
          }
        },
      };
    },
  ),
);
