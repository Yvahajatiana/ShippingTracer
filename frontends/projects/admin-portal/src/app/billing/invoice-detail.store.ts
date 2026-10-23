import { computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import {
  ApiError,
  BillingApi,
  CreditNote,
  CreditReason,
  Guid,
  Invoice,
  Payment,
} from '@parcelflow/shared';
import {
  EMPTY,
  Observable,
  catchError,
  firstValueFrom,
  forkJoin,
  of,
  pipe,
  switchMap,
  tap,
  throwError,
} from 'rxjs';

interface InvoiceDetailState {
  invoice: Invoice | null;
  /** Latest payment attempt; null when none was recorded (404). */
  payment: Payment | null;
  creditNotes: CreditNote[];
  loading: boolean;
  error: unknown;
  issuing: boolean;
}

const initialState: InvoiceDetailState = {
  invoice: null,
  payment: null,
  creditNotes: [],
  loading: false,
  error: null,
  issuing: false,
};

/** A missing payment is a normal state, not an error. */
function paymentOrNull(source: Observable<Payment>): Observable<Payment | null> {
  return source.pipe(
    catchError((error: unknown) =>
      error instanceof ApiError && error.status === 404 ? of(null) : throwError(() => error),
    ),
  );
}

/** Credit notes can only be issued on paid invoices with remaining credit. */
export function canIssueCreditNote(invoice: Invoice | null): boolean {
  return (
    !!invoice &&
    (invoice.status === 'Paid' || invoice.status === 'PartiallyCredited') &&
    invoice.remainingCredit > 0
  );
}

export const InvoiceDetailStore = signalStore(
  withState(initialState),
  withComputed(({ invoice }) => ({
    canIssueCreditNote: computed(() => canIssueCreditNote(invoice())),
  })),
  withMethods((store, api = inject(BillingApi)) => {
    function fetchAll(id: Guid) {
      return forkJoin({
        invoice: api.invoice(id),
        payment: paymentOrNull(api.payment(id)),
        // An invoice holds a handful of credit notes; the first 100 cover it.
        creditNotes: api.creditNotes(id, 1, 100),
      });
    }

    return {
      load: rxMethod<Guid>(
        pipe(
          tap(() => patchState(store, { loading: true, error: null })),
          switchMap((id) =>
            fetchAll(id).pipe(
              tap(({ invoice, payment, creditNotes }) =>
                patchState(store, {
                  invoice,
                  payment,
                  creditNotes: creditNotes.items,
                  loading: false,
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
      /**
       * Issues a credit note. The caller keeps the same operationId across retries so the
       * server can deduplicate. On success the invoice totals are refreshed.
       */
      async issueCreditNote(
        operationId: Guid,
        amount: number,
        reason: CreditReason,
      ): Promise<CreditNote> {
        const invoice = store.invoice();
        if (!invoice) {
          throw new Error('No invoice loaded.');
        }
        patchState(store, { issuing: true });
        try {
          const note = await firstValueFrom(
            api.issueCreditNote(invoice.id, operationId, amount, reason),
          );
          patchState(store, {
            creditNotes: [...store.creditNotes().filter((item) => item.id !== note.id), note],
          });
          try {
            patchState(store, { invoice: await firstValueFrom(api.invoice(invoice.id)) });
          } catch {
            // The note exists; fall back to a local estimate of the new totals.
            const creditTotal = Math.round((invoice.creditTotal + note.amount) * 100) / 100;
            const remainingCredit = Math.round((invoice.amount - creditTotal) * 100) / 100;
            patchState(store, {
              invoice: {
                ...invoice,
                creditTotal,
                remainingCredit,
                status: remainingCredit === 0 ? 'Credited' : 'PartiallyCredited',
              },
            });
          }
          return note;
        } finally {
          patchState(store, { issuing: false });
        }
      },
    };
  }),
);
