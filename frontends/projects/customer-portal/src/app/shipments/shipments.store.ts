import { inject } from '@angular/core';
import { Shipment, ShipmentApi } from '@parcelflow/shared';
import { signalStore } from '@ngrx/signals';
import { fromOffsetLimit, withPagedList } from '../core/paged-list.feature';

export const SHIPMENTS_PAGE_SIZE = 20;

/** The signed-in customer's shipments (offset/limit paging, server order). */
export const ShipmentsStore = signalStore(
  withPagedList<Shipment>(SHIPMENTS_PAGE_SIZE, () => {
    const api = inject(ShipmentApi);
    return fromOffsetLimit((offset, limit) => api.list(offset, limit));
  }),
);
