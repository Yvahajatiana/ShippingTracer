import { computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { DELIVERY_ZONES, DeliveryZone, ShipmentApi, Tariff, TariffBody } from '@parcelflow/shared';
import { EMPTY, catchError, firstValueFrom, pipe, switchMap, tap } from 'rxjs';

interface TariffsState {
  tariffs: Tariff[];
  loading: boolean;
  error: unknown;
  /** Zone currently being saved, if any. */
  savingZone: DeliveryZone | null;
}

const initialState: TariffsState = { tariffs: [], loading: false, error: null, savingZone: null };

/** Per-zone tariffs (base price and price per kg). */
export const TariffsStore = signalStore(
  withState(initialState),
  withComputed(({ tariffs }) => ({
    /** Tariffs in the canonical zone order. */
    ordered: computed(() =>
      [...tariffs()].sort(
        (a, b) => DELIVERY_ZONES.indexOf(a.zone) - DELIVERY_ZONES.indexOf(b.zone),
      ),
    ),
  })),
  withMethods((store, api = inject(ShipmentApi)) => ({
    load: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true, error: null })),
        switchMap(() =>
          api.tariffs().pipe(
            tap((tariffs) => patchState(store, { tariffs, loading: false })),
            catchError((error: unknown) => {
              patchState(store, { loading: false, error });
              return EMPTY;
            }),
          ),
        ),
      ),
    ),
    async update(zone: DeliveryZone, body: TariffBody): Promise<Tariff> {
      patchState(store, { savingZone: zone });
      try {
        const tariff = await firstValueFrom(api.updateTariff(zone, body));
        patchState(store, {
          tariffs: store.tariffs().map((item) => (item.zone === zone ? tariff : item)),
        });
        return tariff;
      } finally {
        patchState(store, { savingZone: null });
      }
    },
  })),
);
