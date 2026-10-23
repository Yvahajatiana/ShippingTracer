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
import { IsoDate, KpiReport, ReportingApi, toIsoDate } from '@parcelflow/shared';
import { EMPTY, catchError, pipe, switchMap, tap } from 'rxjs';

export const DEFAULT_RANGE_DAYS = 30;

export interface KpiRange {
  from: IsoDate;
  to: IsoDate;
}

interface KpiState extends KpiRange {
  report: KpiReport | null;
  loading: boolean;
  error: unknown;
}

/** Last `days` days, today included, as local calendar dates. */
export function defaultRange(now = new Date(), days = DEFAULT_RANGE_DAYS): KpiRange {
  const from = new Date(now);
  from.setDate(from.getDate() - days);
  return { from: toIsoDate(from), to: toIsoDate(now) };
}

export const KpiStore = signalStore(
  withState<KpiState>(() => ({ ...defaultRange(), report: null, loading: false, error: null })),
  withComputed(({ report, loading, error }) => ({
    isEmpty: computed(() => !loading() && !error() && report()?.totalShipments === 0),
    dailyLabels: computed(() => report()?.dailyVolumes.map((item) => item.date) ?? []),
    dailyCounts: computed(() => report()?.dailyVolumes.map((item) => item.shipments) ?? []),
    statusVolumes: computed(() => report()?.statusVolumes ?? []),
  })),
  withMethods((store, api = inject(ReportingApi)) => {
    const load = rxMethod<KpiRange>(
      pipe(
        tap(() => patchState(store, { loading: true, error: null })),
        switchMap(({ from, to }) =>
          api.kpis(from, to).pipe(
            tap((report) => patchState(store, { report, loading: false })),
            catchError((error: unknown) => {
              patchState(store, { loading: false, error, report: null });
              return EMPTY;
            }),
          ),
        ),
      ),
    );
    return {
      /** Applies a new range; returns false when it is invalid (from after to or missing). */
      setRange(from: IsoDate, to: IsoDate): boolean {
        if (!from || !to || from > to) {
          return false;
        }
        patchState(store, { from, to });
        load({ from, to });
        return true;
      },
      reload(): void {
        load({ from: store.from(), to: store.to() });
      },
      resetRange(): void {
        const range = defaultRange();
        patchState(store, range);
        load(range);
      },
    };
  }),
  withHooks({
    onInit(store) {
      store.reload();
    },
  }),
);
