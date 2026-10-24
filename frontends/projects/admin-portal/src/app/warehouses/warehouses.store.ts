import { inject } from '@angular/core';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import {
  CreateWarehouseBody,
  Guid,
  UpdateWarehouseBody,
  Warehouse,
  WarehouseApi,
} from '@parcelflow/shared';
import { firstValueFrom } from 'rxjs';
import { withPagedList } from '../core/paged-list.feature';

/** Paged warehouses and sorting centres with create and update. */
export const WarehousesStore = signalStore(
  withPagedList<Warehouse, null>(
    () => {
      const api = inject(WarehouseApi);
      return (_query, page, pageSize) => api.list(page, pageSize);
    },
    { query: null, pageSize: 20 },
  ),
  withState({ saving: false }),
  withMethods((store, api = inject(WarehouseApi)) => ({
    async create(body: CreateWarehouseBody): Promise<Warehouse> {
      patchState(store, { saving: true });
      try {
        const warehouse = await firstValueFrom(api.create(body));
        // The server decides the ordering: refresh the current page and total.
        store.reload();
        return warehouse;
      } finally {
        patchState(store, { saving: false });
      }
    },
    async update(id: Guid, body: UpdateWarehouseBody): Promise<Warehouse> {
      patchState(store, { saving: true });
      try {
        const warehouse = await firstValueFrom(api.update(id, body));
        store.replaceItem((item) => item.id === id, warehouse);
        return warehouse;
      } finally {
        patchState(store, { saving: false });
      }
    },
  })),
);
