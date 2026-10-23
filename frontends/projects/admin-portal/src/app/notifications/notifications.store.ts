import { inject } from '@angular/core';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { Guid, NotificationApi, NotificationRecord } from '@parcelflow/shared';
import { withPagedList } from '../core/paged-list.feature';

/** Notification audit trail (admins see every customer), optionally filtered by shipment. */
export const NotificationsStore = signalStore(
  withPagedList<NotificationRecord, Guid | null>(
    () => {
      const api = inject(NotificationApi);
      return (shipmentId, page, pageSize) => api.list(shipmentId, page, pageSize);
    },
    { query: null, pageSize: 20 },
  ),
  withState({ selected: null as NotificationRecord | null }),
  withMethods((store) => ({
    select(selected: NotificationRecord | null): void {
      patchState(store, { selected });
    },
  })),
);
