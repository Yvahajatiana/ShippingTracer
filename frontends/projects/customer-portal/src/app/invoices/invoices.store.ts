import { inject } from '@angular/core';
import { BillingApi, Invoice } from '@parcelflow/shared';
import { signalStore } from '@ngrx/signals';
import { map } from 'rxjs';
import { fromPage, withPagedList } from '../core/paged-list.feature';

export const INVOICES_PAGE_SIZE = 20;

/** The signed-in customer's invoices (page/pageSize paging). */
export const InvoicesStore = signalStore(
  withPagedList<Invoice>(INVOICES_PAGE_SIZE, () => {
    const api = inject(BillingApi);
    return (page, pageSize) => api.invoices(page, pageSize).pipe(map(fromPage));
  }),
);
