import { computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { Company, CustomerApi, Guid, Profile } from '@parcelflow/shared';
import { EMPTY, catchError, firstValueFrom, forkJoin, pipe, switchMap, tap } from 'rxjs';

interface CustomerDetailState {
  profile: Profile | null;
  companies: Company[];
  loading: boolean;
  error: unknown;
  assigning: boolean;
}

const initialState: CustomerDetailState = {
  profile: null,
  companies: [],
  loading: false,
  error: null,
  assigning: false,
};

/** One customer profile plus the company catalogue used for assignment. */
export const CustomerDetailStore = signalStore(
  withState(initialState),
  withComputed(({ profile, companies }) => ({
    company: computed(() => {
      const companyId = profile()?.companyId;
      return companyId ? (companies().find((item) => item.id === companyId) ?? null) : null;
    }),
    sortedCompanies: computed(() =>
      [...companies()].sort((a, b) =>
        a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }),
      ),
    ),
  })),
  withMethods((store, api = inject(CustomerApi)) => ({
    load: rxMethod<Guid>(
      pipe(
        tap(() => patchState(store, { loading: true, error: null })),
        switchMap((id) =>
          forkJoin({ profile: api.getProfile(id), companies: api.listCompanies() }).pipe(
            tap(({ profile, companies }) =>
              patchState(store, { profile, companies, loading: false }),
            ),
            catchError((error: unknown) => {
              patchState(store, { loading: false, error });
              return EMPTY;
            }),
          ),
        ),
      ),
    ),
    /** Assigns a company (or none); failures are rethrown for the caller to report. */
    async assignCompany(companyId: Guid | null): Promise<Profile> {
      const current = store.profile();
      if (!current) {
        throw new Error('No profile loaded.');
      }
      patchState(store, { assigning: true });
      try {
        const profile = await firstValueFrom(api.assignCompany(current.id, companyId));
        patchState(store, { profile });
        return profile;
      } finally {
        patchState(store, { assigning: false });
      }
    },
  })),
);
