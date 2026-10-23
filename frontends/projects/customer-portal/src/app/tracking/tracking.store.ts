import { computed, inject } from '@angular/core';
import { ApiError, PublicTracking, TrackingApi, toApiError } from '@parcelflow/shared';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, catchError, pipe, switchMap, tap } from 'rxjs';

interface TrackingState {
  trackingNumber: string | null;
  tracking: PublicTracking | null;
  loading: boolean;
  error: ApiError | null;
}

/** Public (anonymous) tracking of one parcel by its tracking number. */
export const TrackingStore = signalStore(
  withState<TrackingState>({ trackingNumber: null, tracking: null, loading: false, error: null }),
  withComputed(({ error, tracking }) => ({
    notFound: computed(() => error()?.status === 404),
    invalidNumber: computed(() => error()?.status === 400),
    /** Most recent event first, as customers expect on a tracking page. */
    timeline: computed(() => [...(tracking()?.history ?? [])].reverse()),
  })),
  withMethods((store, api = inject(TrackingApi)) => ({
    load: rxMethod<string>(
      pipe(
        tap((trackingNumber) =>
          patchState(store, { trackingNumber, tracking: null, loading: true, error: null }),
        ),
        switchMap((trackingNumber) =>
          api.publicTracking(trackingNumber).pipe(
            tap((tracking) => patchState(store, { tracking, loading: false })),
            catchError((error: unknown) => {
              patchState(store, { loading: false, error: toApiError(error) });
              return EMPTY;
            }),
          ),
        ),
      ),
    ),
  })),
);
