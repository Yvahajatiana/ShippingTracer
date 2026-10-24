import { inject } from '@angular/core';
import { signalStore } from '@ngrx/signals';
import { CustomerApi, Profile } from '@parcelflow/shared';
import { offsetPage, withPagedList } from '../core/paged-list.feature';

export const CUSTOMERS_PAGE_SIZE = 20;

/** Customer profiles, paged by offset/limit (the endpoint returns no total). */
export const CustomersStore = signalStore(
  withPagedList<Profile, null>(
    () => {
      const api = inject(CustomerApi);
      return (_query, page, pageSize) =>
        offsetPage((offset, limit) => api.listProfiles(offset, limit), page, pageSize);
    },
    { query: null, pageSize: CUSTOMERS_PAGE_SIZE, totalKnown: false },
  ),
);
