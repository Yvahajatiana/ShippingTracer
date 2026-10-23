import { computed, inject } from '@angular/core';
import {
  ApiError,
  BillingApi,
  CreditNote,
  Guid,
  Invoice,
  MockPaymentOutcome,
  Payment,
  toApiError,
} from '@parcelflow/shared';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, catchError, forkJoin, map, of, pipe, switchMap, tap } from 'rxjs';
import { Result, attempt } from '../core/result';

interface InvoiceDetailState {
  invoice: Invoice | null;
  /** Last payment attempt; null when none was made yet (Billing answers 404). */
  payment: Payment | null;
  creditNotes: CreditNote[];
  loading: boolean;
  error: ApiError | null;
  paying: boolean;
}

const initialState: InvoiceDetailState = {
  invoice: null,
  payment: null,
  creditNotes: [],
  loading: false,
  error: null,
  paying: false,
};

/** Invoice with its payment, credit notes and the mock payment action. */
export const InvoiceDetailStore = signalStore(
  withState(initialState),
  withComputed(({ invoice }) => ({
    payable: computed(() => invoice()?.status === 'PendingPayment'),
  })),
  withMethods((store, api = inject(BillingApi)) => {
    return {
      load: rxMethod<Guid>(
        pipe(
          tap(() => patchState(store, { ...initialState, loading: true })),
          switchMap((id) =>
            forkJoin({
              invoice: api.invoice(id),
              payment: api.payment(id).pipe(
                catchError((error: unknown) => {
                  const apiError = toApiError(error);
                  // 404 only means "not paid yet"; anything else is a real failure.
                  if (apiError.status === 404) {
                    return of(null);
                  }
                  throw apiError;
                }),
              ),
              creditNotes: api.creditNotes(id).pipe(map((page) => page.items)),
            }).pipe(
              tap(({ invoice, payment, creditNotes }) =>
                patchState(store, { invoice, payment, creditNotes, loading: false }),
              ),
              catchError((error: unknown) => {
                patchState(store, { loading: false, error: toApiError(error) });
                return EMPTY;
              }),
            ),
          ),
        ),
      ),

      /**
       * Submits a mock payment. The caller owns the operation id so a retry of the
       * same attempt is idempotent on the server.
       */
      async pay(operationId: Guid, outcome: MockPaymentOutcome): Promise<Result<Payment>> {
        const invoice = store.invoice();
        if (!invoice) {
          throw new Error('The invoice must be loaded before it can be paid.');
        }
        patchState(store, { paying: true });
        const result = await attempt(api.pay(invoice.id, operationId, outcome));
        if (result.ok) {
          // The invoice status is derived by Billing: reload it rather than guessing.
          const refreshed = await attempt(api.invoice(invoice.id));
          patchState(store, {
            payment: result.value,
            invoice: refreshed.ok
              ? refreshed.value
              : { ...invoice, status: outcome === 'Approved' ? 'Paid' : 'PaymentFailed' },
          });
        }
        patchState(store, { paying: false });
        return result;
      },
    };
  }),
);
