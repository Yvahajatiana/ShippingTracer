import { computed, inject } from '@angular/core';
import {
  Address,
  AddressBody,
  ApiError,
  CustomerApi,
  Guid,
  Profile,
  ProfileBody,
  toApiError,
} from '@parcelflow/shared';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, Observable, catchError, pipe, switchMap, tap } from 'rxjs';
import { Result, attempt } from '../core/result';

interface ProfileState {
  profile: Profile | null;
  loading: boolean;
  loaded: boolean;
  /** Load failure other than "profile not created yet". */
  error: ApiError | null;
  /** GET /customers/me answered 404: the customer must onboard first. */
  missing: boolean;
  saving: boolean;
}

const initialState: ProfileState = {
  profile: null,
  loading: false,
  loaded: false,
  error: null,
  missing: false,
  saving: false,
};

/** Customer profile of the signed-in user and its saved addresses. */
export const ProfileStore = signalStore(
  withState(initialState),
  withComputed(({ profile }) => ({
    addresses: computed<Address[]>(() => profile()?.addresses ?? []),
    fullName: computed(() => {
      const current = profile();
      return current ? `${current.firstName} ${current.lastName}`.trim() : '';
    }),
  })),
  withMethods((store, api = inject(CustomerApi)) => {
    /** Runs a mutation with the shared `saving` flag. */
    async function mutate<T>(
      request: Observable<T>,
      apply: (value: T) => void,
    ): Promise<Result<T>> {
      patchState(store, { saving: true });
      const result = await attempt(request);
      patchState(store, { saving: false });
      if (result.ok) {
        apply(result.value);
      }
      return result;
    }

    function requireProfileId(): Guid {
      const profile = store.profile();
      if (!profile) {
        throw new Error('The profile must be loaded before it can be changed.');
      }
      return profile.id;
    }

    function replaceAddresses(update: (addresses: Address[]) => Address[]): void {
      const profile = store.profile();
      if (profile) {
        patchState(store, { profile: { ...profile, addresses: update(profile.addresses) } });
      }
    }

    return {
      load: rxMethod<void>(
        pipe(
          tap(() => patchState(store, { loading: true, error: null, missing: false })),
          switchMap(() =>
            api.me().pipe(
              tap((profile) => patchState(store, { profile, loading: false, loaded: true })),
              catchError((error: unknown) => {
                const apiError = toApiError(error);
                patchState(store, {
                  profile: null,
                  loading: false,
                  loaded: true,
                  missing: apiError.status === 404,
                  error: apiError.status === 404 ? null : apiError,
                });
                return EMPTY;
              }),
            ),
          ),
        ),
      ),

      create(body: ProfileBody): Promise<Result<Profile>> {
        return mutate(api.createProfile(body), (profile) =>
          patchState(store, { profile, missing: false }),
        );
      },

      update(body: ProfileBody): Promise<Result<Profile>> {
        return mutate(api.updateProfile(requireProfileId(), body), (profile) =>
          patchState(store, { profile }),
        );
      },

      addAddress(body: AddressBody): Promise<Result<Address>> {
        return mutate(api.addAddress(requireProfileId(), body), (address) =>
          replaceAddresses((addresses) => [...addresses, address]),
        );
      },

      updateAddress(addressId: Guid, body: AddressBody): Promise<Result<Address>> {
        return mutate(api.updateAddress(requireProfileId(), addressId, body), (address) =>
          replaceAddresses((addresses) =>
            addresses.map((item) => (item.id === addressId ? address : item)),
          ),
        );
      },

      deleteAddress(addressId: Guid): Promise<Result<void>> {
        return mutate(api.deleteAddress(requireProfileId(), addressId), () =>
          replaceAddresses((addresses) => addresses.filter((item) => item.id !== addressId)),
        );
      },
    };
  }),
);
