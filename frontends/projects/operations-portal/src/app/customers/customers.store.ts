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
import { Company, CustomerApi, Guid, Profile } from '@parcelflow/shared';
import { EMPTY, catchError, forkJoin, of, pipe, switchMap, tap } from 'rxjs';
import { sliceFetcher, withPagedList } from '../core/paged-list.feature';

export const CUSTOMER_PAGE_SIZE = 20;

export const CustomerListStore = signalStore(
  withPagedList<Profile>(() => {
    const api = inject(CustomerApi);
    return sliceFetcher((offset, limit) => api.listProfiles(offset, limit));
  }, CUSTOMER_PAGE_SIZE),
  withHooks({
    onInit(store) {
      store.loadPage(1);
    },
  }),
);

interface CompaniesState {
  companies: Company[];
  loading: boolean;
  loaded: boolean;
  error: unknown;
}

/** Read-only company directory (not paginated by the API). */
export const CompanyListStore = signalStore(
  withState<CompaniesState>({ companies: [], loading: false, loaded: false, error: null }),
  withComputed(({ companies, loaded, error }) => ({
    isEmpty: computed(() => loaded() && !error() && companies().length === 0),
  })),
  withMethods((store, api = inject(CustomerApi)) => ({
    load: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true, error: null })),
        switchMap(() =>
          api.listCompanies().pipe(
            tap((companies) =>
              patchState(store, {
                companies: [...companies].sort((a, b) => a.name.localeCompare(b.name)),
                loading: false,
                loaded: true,
              }),
            ),
            catchError((error: unknown) => {
              patchState(store, { loading: false, error });
              return EMPTY;
            }),
          ),
        ),
      ),
    ),
  })),
  withHooks({
    onInit(store) {
      store.load();
    },
  }),
);

interface CustomerDetailState {
  profile: Profile | null;
  company: Company | null;
  loading: boolean;
  error: unknown;
}

/** Customer profile with its company name resolved from the company directory. */
export const CustomerDetailStore = signalStore(
  withState<CustomerDetailState>({ profile: null, company: null, loading: false, error: null }),
  withMethods((store, api = inject(CustomerApi)) => ({
    load: rxMethod<Guid>(
      pipe(
        tap(() => patchState(store, { loading: true, error: null })),
        switchMap((id) =>
          forkJoin({
            profile: api.getProfile(id),
            // A failing directory must not hide the profile itself.
            companies: api.listCompanies().pipe(catchError(() => of([] as Company[]))),
          }).pipe(
            tap(({ profile, companies }) =>
              patchState(store, {
                profile,
                company: companies.find((item) => item.id === profile.companyId) ?? null,
                loading: false,
              }),
            ),
            catchError((error: unknown) => {
              patchState(store, { loading: false, error, profile: null, company: null });
              return EMPTY;
            }),
          ),
        ),
      ),
    ),
  })),
);
