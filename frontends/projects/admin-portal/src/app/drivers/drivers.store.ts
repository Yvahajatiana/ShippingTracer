import { inject } from '@angular/core';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { DeliveryApi, Driver } from '@parcelflow/shared';
import { firstValueFrom } from 'rxjs';
import { withPagedList } from '../core/paged-list.feature';

/** Paged delivery drivers (Keycloak subjects) with create and update. */
export const DriversStore = signalStore(
  withPagedList<Driver, null>(
    () => {
      const api = inject(DeliveryApi);
      return (_query, page, pageSize) => api.drivers(page, pageSize);
    },
    { query: null, pageSize: 20 },
  ),
  withState({ saving: false }),
  withMethods((store, api = inject(DeliveryApi)) => ({
    async create(subject: string, displayName: string): Promise<Driver> {
      patchState(store, { saving: true });
      try {
        const driver = await firstValueFrom(api.createDriver(subject, displayName));
        store.reload();
        return driver;
      } finally {
        patchState(store, { saving: false });
      }
    },
    async update(subject: string, displayName: string, isActive: boolean): Promise<Driver> {
      patchState(store, { saving: true });
      try {
        const driver = await firstValueFrom(api.updateDriver(subject, displayName, isActive));
        store.replaceItem((item) => item.subject === subject, driver);
        return driver;
      } finally {
        patchState(store, { saving: false });
      }
    },
  })),
);
