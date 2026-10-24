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
  IsoDate,
  KPI_MAX_RANGE_DAYS,
  KpiReport,
  ReportingApi,
  addDays,
  daysBetween,
  toIsoDate,
} from '@parcelflow/shared';
import { EMPTY, catchError, pipe, switchMap, tap } from 'rxjs';

export const DEFAULT_RANGE_DAYS = 30;

/** Range picked in the UI: both bounds are INCLUSIVE calendar dates. */
export interface KpiRange {
  from: IsoDate;
  to: IsoDate;
}

interface KpiState extends KpiRange {
  report: KpiReport | null;
  loading: boolean;
  error: unknown;
}

/** Last `days` days, today included, as local calendar dates (inclusive bounds). */
export function defaultRange(now = new Date(), days = DEFAULT_RANGE_DAYS): KpiRange {
  const from = new Date(now);
  from.setDate(from.getDate() - (days - 1));
  return { from: toIsoDate(from), to: toIsoDate(now) };
}

/** Number of days covered by an inclusive range (1 when from equals to). */
export function rangeLength({ from, to }: KpiRange): number {
  return daysBetween(from, to) + 1;
}

/** An inclusive range is valid when ordered and within the Reporting maximum. */
export function isValidRange(range: KpiRange): boolean {
  return (
    !!range.from && !!range.to && range.from <= range.to && rangeLength(range) <= KPI_MAX_RANGE_DAYS
  );
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
        // Reporting treats `to` as exclusive: send the day after the inclusive end.
        switchMap(({ from, to }) =>
          api.kpis(from, addDays(to, 1)).pipe(
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
      /**
       * Applies a new inclusive range; returns false when it is invalid (missing, from after
       * to, or longer than KPI_MAX_RANGE_DAYS).
       */
      setRange(from: IsoDate, to: IsoDate): boolean {
        if (!isValidRange({ from, to })) {
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
