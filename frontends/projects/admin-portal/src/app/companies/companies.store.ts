import { computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { Company, CompanyBody, CustomerApi, Guid } from '@parcelflow/shared';
import { EMPTY, catchError, firstValueFrom, pipe, switchMap, tap } from 'rxjs';

interface CompaniesState {
  companies: Company[];
  loading: boolean;
  error: unknown;
  saving: boolean;
}

const initialState: CompaniesState = { companies: [], loading: false, error: null, saving: false };

/** Companies list (the endpoint is not paged) with create and update. Provided by the page. */
export const CompaniesStore = signalStore(
  withState(initialState),
  withComputed(({ companies }) => ({
    sorted: computed(() =>
      [...companies()].sort((a, b) =>
        a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }),
      ),
    ),
  })),
  withMethods((store, api = inject(CustomerApi)) => {
    /** Runs a mutation with the saving flag; failures are rethrown for the form to handle. */
    async function mutate(request: Promise<Company>, apply: (company: Company) => void) {
      patchState(store, { saving: true });
      try {
        const company = await request;
        apply(company);
        return company;
      } finally {
        patchState(store, { saving: false });
      }
    }

    return {
      load: rxMethod<void>(
        pipe(
          tap(() => patchState(store, { loading: true, error: null })),
          switchMap(() =>
            api.listCompanies().pipe(
              tap((companies) => patchState(store, { companies, loading: false })),
              catchError((error: unknown) => {
                patchState(store, { loading: false, error });
                return EMPTY;
              }),
            ),
          ),
        ),
      ),
      create(body: CompanyBody): Promise<Company> {
        return mutate(firstValueFrom(api.createCompany(body)), (company) =>
          patchState(store, { companies: [...store.companies(), company] }),
        );
      },
      update(id: Guid, body: CompanyBody): Promise<Company> {
        return mutate(firstValueFrom(api.updateCompany(id, body)), (company) =>
          patchState(store, {
            companies: store.companies().map((item) => (item.id === id ? company : item)),
          }),
        );
      },
    };
  }),
);
