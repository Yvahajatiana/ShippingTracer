import { inject } from '@angular/core';
import { NotificationApi, NotificationRecord } from '@parcelflow/shared';
import { signalStore } from '@ngrx/signals';
import { map } from 'rxjs';
import { fromPage, withPagedList } from '../core/paged-list.feature';

export const NOTIFICATIONS_PAGE_SIZE = 20;

/** E-mail and SMS notifications sent to the signed-in customer. */
export const NotificationsStore = signalStore(
  withPagedList<NotificationRecord>(NOTIFICATIONS_PAGE_SIZE, () => {
    const api = inject(NotificationApi);
    return (page, pageSize) => api.list(null, page, pageSize).pipe(map(fromPage));
  }),
);
