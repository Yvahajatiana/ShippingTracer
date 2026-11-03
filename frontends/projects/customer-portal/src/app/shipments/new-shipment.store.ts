import { InjectionToken, inject } from '@angular/core';
import {
  ApiError,
  Quote,
  Shipment,
  ShipmentApi,
  ShipmentBody,
  toApiError,
} from '@parcelflow/shared';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, catchError, debounceTime, distinctUntilChanged, pipe, switchMap, tap } from 'rxjs';
import { Result, attempt } from '../core/result';

/** Quiet period before a quote is requested while the customer is typing. */
export const QUOTE_DEBOUNCE_MS = new InjectionToken<number>('QUOTE_DEBOUNCE_MS', {
  factory: () => 400,
});

interface NewShipmentState {
  quote: Quote | null;
  quoting: boolean;
  quoteError: ApiError | null;
  creating: boolean;
}

const sameBody = (left: ShipmentBody | null, right: ShipmentBody | null) =>
  JSON.stringify(left) === JSON.stringify(right);

/** Live quote and creation of a shipment. */
export const NewShipmentStore = signalStore(
  withState<NewShipmentState>({ quote: null, quoting: false, quoteError: null, creating: false }),
  withMethods((store, api = inject(ShipmentApi), debounceMs = inject(QUOTE_DEBOUNCE_MS)) => ({
    /**
     * Feed with the current body (null while the form is invalid). Requests are debounced,
     * deduplicated and superseded requests are cancelled.
     */
    requestQuote: rxMethod<ShipmentBody | null>(
      pipe(
        debounceTime(debounceMs),
        distinctUntilChanged(sameBody),
        switchMap((body) => {
          if (!body) {
            patchState(store, { quote: null, quoting: false, quoteError: null });
            return EMPTY;
          }
          patchState(store, { quoting: true, quoteError: null });
          return api.quote(body).pipe(
            tap((quote) => patchState(store, { quote, quoting: false })),
            catchError((error: unknown) => {
              patchState(store, { quote: null, quoting: false, quoteError: toApiError(error) });
              return EMPTY;
            }),
          );
        }),
      ),
    ),

    async create(body: ShipmentBody): Promise<Result<Shipment>> {
      patchState(store, { creating: true });
      const result = await attempt(api.create(body));
      patchState(store, { creating: false });
      return result;
    },
  })),
);
