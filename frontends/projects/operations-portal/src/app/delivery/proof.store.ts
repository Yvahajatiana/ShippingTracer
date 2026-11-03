import { inject } from '@angular/core';
import { patchState, signalStore, withHooks, withMethods, withState } from '@ngrx/signals';
import { DeliveryApi, DeliveryAttempt, Guid, ProofImageKind } from '@parcelflow/shared';
import { catchError, firstValueFrom, of } from 'rxjs';

interface ProofState {
  attempt: DeliveryAttempt | null;
  signatureUrl: string | null;
  photoUrl: string | null;
  loading: boolean;
  error: unknown;
  imageError: boolean;
}

/**
 * Delivery proof of one stop. Images are exposed as object URLs that are revoked
 * when the store is destroyed (provided by the proof dialog component).
 */
export const ProofStore = signalStore(
  withState<ProofState>({
    attempt: null,
    signatureUrl: null,
    photoUrl: null,
    loading: false,
    error: null,
    imageError: false,
  }),
  withMethods((store, api = inject(DeliveryApi)) => {
    let disposed = false;
    const revokeUrls = (...urls: (string | null)[]) =>
      urls.forEach((url) => url && URL.revokeObjectURL(url));
    const revoke = () => {
      revokeUrls(store.signatureUrl(), store.photoUrl());
      patchState(store, { signatureUrl: null, photoUrl: null });
    };

    const image = async (routeId: Guid, shipmentId: Guid, kind: ProofImageKind) => {
      const blob = await firstValueFrom(
        api.proofImage(routeId, shipmentId, kind).pipe(catchError(() => of(null))),
      );
      if (!blob) {
        patchState(store, { imageError: true });
        return null;
      }
      return URL.createObjectURL(blob);
    };

    return {
      async load(routeId: Guid, shipmentId: Guid): Promise<void> {
        revoke();
        patchState(store, { loading: true, error: null, imageError: false, attempt: null });
        try {
          const attempt = await firstValueFrom(api.proof(routeId, shipmentId));
          const [signatureUrl, photoUrl] = await Promise.all([
            attempt.hasSignature ? image(routeId, shipmentId, 'Signature') : null,
            attempt.hasPhoto ? image(routeId, shipmentId, 'Photo') : null,
          ]);
          if (disposed) {
            // The dialog closed while images were downloading.
            revokeUrls(signatureUrl, photoUrl);
            return;
          }
          patchState(store, { attempt, signatureUrl, photoUrl, loading: false });
        } catch (error) {
          patchState(store, { loading: false, error });
        }
      },
      dispose(): void {
        disposed = true;
        revoke();
      },
    };
  }),
  withHooks({
    onDestroy(store) {
      store.dispose();
    },
  }),
);
