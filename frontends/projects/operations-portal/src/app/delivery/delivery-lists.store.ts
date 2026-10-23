import { computed, inject } from '@angular/core';
import {
  patchState,
  signalStore,
  withComputed,
  withHooks,
  withMethods,
  withState,
} from '@ngrx/signals';
import { DeliveryApi, DeliveryRoute, Driver, IsoDate, Notifier } from '@parcelflow/shared';
import { catchError, firstValueFrom, of } from 'rxjs';
import { fromPage, withPagedList } from '../core/paged-list.feature';

export const ROUTE_PAGE_SIZE = 20;
export const DRIVER_PAGE_SIZE = 20;
/** Drivers are few; one large page is enough to resolve names and fill pickers. */
export const DRIVER_LOOKUP_SIZE = 100;

export const DriverListStore = signalStore(
  withPagedList<Driver>(() => {
    const api = inject(DeliveryApi);
    return (page, pageSize) => fromPage(api.drivers(page, pageSize));
  }, DRIVER_PAGE_SIZE),
  withHooks({
    onInit(store) {
      store.loadPage(1);
    },
  }),
);

interface RouteListExtraState {
  drivers: Driver[];
  creating: boolean;
}

/** Paged delivery routes, driver names and route creation. */
export const RouteListStore = signalStore(
  withPagedList<DeliveryRoute>(() => {
    const api = inject(DeliveryApi);
    return (page, pageSize) => fromPage(api.routes(page, pageSize));
  }, ROUTE_PAGE_SIZE),
  withState<RouteListExtraState>({ drivers: [], creating: false }),
  withComputed(({ drivers }) => ({
    driverNames: computed(
      () => new Map(drivers().map((driver) => [driver.subject, driver.displayName])),
    ),
  })),
  withMethods((store, api = inject(DeliveryApi), notifier = inject(Notifier)) => ({
    async loadDrivers(): Promise<void> {
      // Names are a convenience: subjects are shown when the lookup fails.
      const page = await firstValueFrom(
        api.drivers(1, DRIVER_LOOKUP_SIZE).pipe(catchError(() => of(null))),
      );
      if (page) {
        patchState(store, { drivers: page.items });
      }
    },
    /** Creates a route; errors are rethrown so the form can display field errors. */
    async create(name: string, serviceDate: IsoDate): Promise<DeliveryRoute> {
      patchState(store, { creating: true });
      try {
        const route = await firstValueFrom(api.createRoute(name.trim(), serviceDate));
        notifier.success('routes.created', { name: route.name });
        store.loadPage(1);
        return route;
      } finally {
        patchState(store, { creating: false });
      }
    },
  })),
  withHooks({
    onInit(store) {
      store.loadPage(1);
      void store.loadDrivers();
    },
  }),
);
