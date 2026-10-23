import { computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';
import { BillingApi, Invoice, InvoiceStatus } from '@parcelflow/shared';
import { withPagedList } from '../core/paged-list.feature';

/** Status filter value; '' shows every invoice of the loaded page. */
export type InvoiceStatusFilter = InvoiceStatus | '';

/**
 * All invoices (admin view), server paged. The billing API has no status filter,
 * so the filter applies to the loaded page only.
 */
export const InvoicesStore = signalStore(
  withPagedList<Invoice, null>(
    () => {
      const api = inject(BillingApi);
      return (_query, page, pageSize) => api.invoices(page, pageSize);
    },
    { query: null, pageSize: 20 },
  ),
  withState({ statusFilter: '' as InvoiceStatusFilter }),
  withComputed(({ items, statusFilter }) => ({
    visible: computed(() => {
      const status = statusFilter();
      return status ? items().filter((invoice) => invoice.status === status) : items();
    }),
  })),
  withMethods((store) => ({
    setStatusFilter(statusFilter: InvoiceStatusFilter): void {
      patchState(store, { statusFilter });
    },
  })),
);
