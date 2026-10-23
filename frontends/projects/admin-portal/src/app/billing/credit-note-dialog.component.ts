import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  model,
  untracked,
} from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  CREDIT_REASONS,
  CreditReason,
  FieldErrorComponent,
  Notifier,
  PfMoneyPipe,
  newOperationId,
} from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { reportFormError } from '../core/form-errors';
import { InvoiceDetailStore } from './invoice-detail.store';

/** Amounts carry at most two decimals (BillingRules.IsAmount). */
const AMOUNT_PATTERN = /^\d+(\.\d{1,2})?$/;

/**
 * Issues a credit note on the invoice of the surrounding InvoiceDetailStore.
 * One operationId is generated per opening and reused on every retry (idempotency key).
 */
@Component({
  selector: 'pf-credit-note-dialog',
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    PfMoneyPipe,
    FieldErrorComponent,
    HlmButtonImports,
    HlmDialogImports,
    HlmFieldImports,
    HlmInputImports,
    HlmNativeSelectImports,
    HlmSpinnerImports,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <hlm-dialog [state]="open() ? 'open' : 'closed'" (closed)="open.set(false)">
      <hlm-dialog-content
        *hlmDialogPortal
        class="sm:max-w-md"
        [closeLabel]="'common.close' | transloco"
      >
        @if (store.invoice(); as invoice) {
          <hlm-dialog-header>
            <h2 hlmDialogTitle>{{ 'invoices.creditNote.title' | transloco }}</h2>
            <p hlmDialogDescription>
              {{
                'invoices.creditNote.description'
                  | transloco
                    : {
                        number: invoice.number,
                        remaining: (invoice.remainingCredit | pfMoney: invoice.currency),
                      }
              }}
            </p>
          </hlm-dialog-header>
          <form class="grid gap-4" [formGroup]="form" (ngSubmit)="submit()" novalidate>
            <div hlmField>
              <label hlmFieldLabel for="credit-amount">
                {{ 'invoices.creditNote.amount' | transloco }} ({{ invoice.currency }})
              </label>
              <input
                hlmInput
                id="credit-amount"
                type="number"
                inputmode="decimal"
                min="0.01"
                step="0.01"
                [max]="invoice.remainingCredit"
                formControlName="amount"
              />
              <pf-field-error [control]="form.controls.amount" />
            </div>
            <div hlmField>
              <label hlmFieldLabel for="credit-reason">
                {{ 'invoices.creditNote.reason' | transloco }}
              </label>
              <hlm-native-select class="w-full" selectId="credit-reason" formControlName="reason">
                @for (reason of reasons; track reason) {
                  <option [value]="reason">{{ 'enum.CreditReason.' + reason | transloco }}</option>
                }
              </hlm-native-select>
            </div>
            <hlm-dialog-footer>
              <button hlmBtn type="button" variant="outline" hlmDialogClose>
                {{ 'common.cancel' | transloco }}
              </button>
              <button hlmBtn type="submit" [disabled]="store.issuing()">
                @if (store.issuing()) {
                  <hlm-spinner class="size-4" [aria-label]="'common.loading' | transloco" />
                }
                {{ 'invoices.creditNote.submit' | transloco }}
              </button>
            </hlm-dialog-footer>
          </form>
        }
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class CreditNoteDialogComponent {
  protected readonly store = inject(InvoiceDetailStore);
  private readonly notifier = inject(Notifier);

  readonly open = model(false);
  protected readonly reasons = CREDIT_REASONS;

  /** Idempotency key of the current dialog session. */
  private operationId = newOperationId();

  protected readonly form = inject(NonNullableFormBuilder).group({
    amount: [null as number | null, [Validators.required, Validators.min(0.01)]],
    reason: ['BillingCorrection' as CreditReason, Validators.required],
  });

  constructor() {
    effect(() => {
      if (!this.open()) {
        return;
      }
      // Only opening starts a new session; invoice refreshes must not rotate the key.
      untracked(() => {
        const remaining = this.store.invoice()?.remainingCredit ?? 0;
        // A new dialog session is a new operation; retries inside it reuse this key.
        this.operationId = newOperationId();
        const amount = this.form.controls.amount;
        amount.setValidators([
          Validators.required,
          Validators.min(0.01),
          Validators.max(remaining),
          Validators.pattern(AMOUNT_PATTERN),
        ]);
        this.form.reset({ amount: null, reason: 'BillingCorrection' });
      });
    });
  }

  async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    if (this.store.issuing()) {
      return;
    }
    const { amount, reason } = this.form.getRawValue();
    try {
      const note = await this.store.issueCreditNote(this.operationId, Number(amount), reason);
      this.notifier.success('invoices.creditNote.issued', { number: note.number });
      this.open.set(false);
    } catch (error) {
      reportFormError(this.form, error, this.notifier);
    }
  }
}
