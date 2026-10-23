import { computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';
import {
  AvailableParcel,
  DeliveryApi,
  DeliveryFailureReason,
  DeliveryRoute,
  DeliveryStop,
  Driver,
  Guid,
  Notifier,
  Shipment,
  ShipmentApi,
  blobToBase64,
  toApiError,
} from '@parcelflow/shared';
import { Observable, catchError, firstValueFrom, forkJoin, map, of } from 'rxjs';
import { DRIVER_LOOKUP_SIZE } from './delivery-lists.store';

/** Page size used to list parcels that can be added to a route. */
export const AVAILABLE_PARCELS_SIZE = 100;

export type RouteAction = 'driver' | 'addStop' | 'removeStop' | 'start' | 'confirm' | 'fail';

/** Proofs captured on the delivery dialog. */
export interface DeliveryProofInput {
  operationId: Guid;
  recipientName: string;
  /** PNG signature, already base64 encoded (canvas export). */
  signaturePng: string | null;
  /** Photo file, converted to base64 before sending. */
  photo: Blob | null;
}

export type ActionResult = { ok: true } | { ok: false; error: unknown };

/** A stop enriched with shipment information for display. */
export interface StopView extends DeliveryStop {
  trackingNumber: string | null;
  recipientName: string | null;
  city: string | null;
}

interface RouteDetailState {
  routeId: Guid | null;
  route: DeliveryRoute | null;
  loading: boolean;
  error: unknown;
  drivers: Driver[];
  parcels: AvailableParcel[];
  parcelsLoading: boolean;
  /** Shipment summaries keyed by id, used to label stops. */
  shipments: Record<Guid, Shipment | null>;
  busy: RouteAction | null;
}

export const RouteDetailStore = signalStore(
  withState<RouteDetailState>({
    routeId: null,
    route: null,
    loading: false,
    error: null,
    drivers: [],
    parcels: [],
    parcelsLoading: false,
    shipments: {},
    busy: null,
  }),
  withComputed(({ route, drivers, shipments, parcels }) => ({
    isPlanned: computed(() => route()?.status === 'Planned'),
    isInProgress: computed(() => route()?.status === 'InProgress'),
    activeDrivers: computed(() => drivers().filter((driver) => driver.isActive)),
    driverName: computed(() => {
      const subject = route()?.driverSubject;
      if (!subject) {
        return null;
      }
      return drivers().find((driver) => driver.subject === subject)?.displayName ?? subject;
    }),
    canStart: computed(() => {
      const current = route();
      return current?.status === 'Planned' && !!current.driverSubject && current.stops.length > 0;
    }),
    stops: computed<StopView[]>(() => {
      const known = shipments();
      const available = new Map(parcels().map((item) => [item.shipmentId, item.trackingNumber]));
      return [...(route()?.stops ?? [])]
        .sort((a, b) => a.sequence - b.sequence)
        .map((stop) => {
          const shipment = known[stop.shipmentId];
          return {
            ...stop,
            trackingNumber: shipment?.trackingNumber ?? available.get(stop.shipmentId) ?? null,
            recipientName: shipment?.recipient.name ?? null,
            city: shipment?.recipient.city ?? null,
          };
        });
    }),
  })),
  withComputed(({ stops }) => ({
    progress: computed(() => {
      const all = stops();
      const done = all.filter((stop) => stop.status !== 'Pending').length;
      return {
        total: all.length,
        percent: all.length ? Math.round((done / all.length) * 100) : 0,
        delivered: all.filter((stop) => stop.status === 'Delivered').length,
        failed: all.filter((stop) => stop.status === 'Failed').length,
        pending: all.filter((stop) => stop.status === 'Pending').length,
      };
    }),
  })),
  withMethods(
    (
      store,
      api = inject(DeliveryApi),
      shipmentApi = inject(ShipmentApi),
      notifier = inject(Notifier),
    ) => {
      /** Fetches summaries for stops that are not known yet; failures fall back to ids. */
      const loadShipments = async (route: DeliveryRoute) => {
        const missing = route.stops
          .map((stop) => stop.shipmentId)
          .filter((id) => !(id in store.shipments()));
        if (missing.length === 0) {
          return;
        }
        const requests: Record<Guid, Observable<Shipment | null>> = {};
        for (const id of missing) {
          requests[id] = shipmentApi.get(id).pipe(catchError(() => of(null)));
        }
        const loaded = await firstValueFrom(forkJoin(requests));
        patchState(store, { shipments: { ...store.shipments(), ...loaded } });
      };

      const loadParcels = async () => {
        patchState(store, { parcelsLoading: true });
        const page = await firstValueFrom(
          api.availableParcels(1, AVAILABLE_PARCELS_SIZE).pipe(catchError(() => of(null))),
        );
        patchState(store, { parcels: page?.items ?? [], parcelsLoading: false });
      };

      const applyRoute = (route: DeliveryRoute) => {
        patchState(store, { route });
        void loadShipments(route);
      };

      const refreshRoute = async () => {
        const id = store.routeId();
        if (!id) {
          return;
        }
        const route = await firstValueFrom(api.route(id).pipe(catchError(() => of(null))));
        if (route) {
          applyRoute(route);
        }
      };

      /** Runs a mutation with a busy flag; conflicts reload the route since its state moved on. */
      const run = async (
        action: RouteAction,
        mutation: () => Promise<unknown>,
        successKey: string,
      ): Promise<ActionResult> => {
        patchState(store, { busy: action });
        try {
          await mutation();
          notifier.success(successKey);
          return { ok: true };
        } catch (error) {
          if (toApiError(error).status === 409) {
            await refreshRoute();
          }
          return { ok: false, error };
        } finally {
          patchState(store, { busy: null });
        }
      };

      /** Same as run, but reports the error as a toast (actions without a form). */
      const runAndNotify = async (
        action: RouteAction,
        mutation: () => Promise<unknown>,
        successKey: string,
      ): Promise<boolean> => {
        const result = await run(action, mutation, successKey);
        if (!result.ok) {
          notifier.error(result.error);
        }
        return result.ok;
      };

      return {
        async load(id: Guid): Promise<void> {
          patchState(store, { routeId: id, loading: true, error: null });
          try {
            const [route, drivers] = await Promise.all([
              firstValueFrom(api.route(id)),
              firstValueFrom(
                api.drivers(1, DRIVER_LOOKUP_SIZE).pipe(
                  map((page) => page.items),
                  catchError(() => of([] as Driver[])),
                ),
              ),
            ]);
            patchState(store, { drivers, loading: false });
            applyRoute(route);
            if (route.status === 'Planned') {
              await loadParcels();
            }
          } catch (error) {
            patchState(store, { loading: false, error, route: null });
          }
        },

        reloadParcels: loadParcels,

        assignDriver(driverSubject: string): Promise<boolean> {
          const id = store.routeId()!;
          return runAndNotify(
            'driver',
            async () => applyRoute(await firstValueFrom(api.assignDriver(id, driverSubject))),
            'routes.detail.driverAssigned',
          );
        },

        async addStop(shipmentId: Guid): Promise<boolean> {
          const id = store.routeId()!;
          const ok = await runAndNotify(
            'addStop',
            async () => applyRoute(await firstValueFrom(api.addStop(id, shipmentId))),
            'routes.detail.stopAdded',
          );
          await loadParcels();
          return ok;
        },

        async removeStop(shipmentId: Guid): Promise<boolean> {
          const id = store.routeId()!;
          const ok = await runAndNotify(
            'removeStop',
            async () => {
              await firstValueFrom(api.removeStop(id, shipmentId));
              // The API answers 204: reload to get the new sequences.
              await refreshRoute();
            },
            'routes.detail.stopRemoved',
          );
          await loadParcels();
          return ok;
        },

        start(): Promise<boolean> {
          const id = store.routeId()!;
          return runAndNotify(
            'start',
            async () => {
              applyRoute(await firstValueFrom(api.startRoute(id)));
              patchState(store, { parcels: [] });
            },
            'routes.detail.started',
          );
        },

        /**
         * Confirms a delivery. The caller keeps the same operationId across retries of one
         * dialog so the server can deduplicate a request that was received but not answered.
         */
        async confirmDelivery(shipmentId: Guid, proof: DeliveryProofInput): Promise<ActionResult> {
          const id = store.routeId()!;
          return run(
            'confirm',
            async () => {
              const photo = proof.photo ? await blobToBase64(proof.photo) : null;
              await firstValueFrom(
                api.confirmDelivery(id, shipmentId, {
                  operationId: proof.operationId,
                  recipientName: proof.recipientName.trim(),
                  signaturePng: proof.signaturePng,
                  photo,
                }),
              );
              await refreshRoute();
            },
            'routes.delivery.confirmed',
          );
        },

        async failDelivery(
          shipmentId: Guid,
          operationId: Guid,
          reason: DeliveryFailureReason,
        ): Promise<ActionResult> {
          const id = store.routeId()!;
          return run(
            'fail',
            async () => {
              await firstValueFrom(api.failDelivery(id, shipmentId, { operationId, reason }));
              await refreshRoute();
            },
            'routes.delivery.failed',
          );
        },
      };
    },
  ),
);
