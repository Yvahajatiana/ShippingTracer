import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { MockPaymentOutcome, Notifier, PfMoneyPipe, newOperationId } from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmRadioGroupImports } from '@spartan-ng/helm/radio-group';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { InvoiceDetailStore } from './invoice-detail.store';

const OUTCOMES: readonly MockPaymentOutcome[] = ['Approved', 'Declined'];

/**
 * Mock payment dialog. One operation id is generated per opening, so retrying the same
 * attempt after a network failure cannot charge twice.
 */
@Component({
  selector: 'pf-payment-dialog',
  imports: [
    TranslocoPipe,
    PfMoneyPipe,
    HlmButtonImports,
    HlmDialogImports,
    HlmLabelImports,
    HlmRadioGroupImports,
    HlmSpinnerImports,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <hlm-dialog [state]="open() ? 'open' : 'closed'" (closed)="closed.emit()">
      <hlm-dialog-content
        *hlmDialogPortal="let ctx"
        class="sm:max-w-md"
        [closeLabel]="'common.close' | transloco"
      >
        <hlm-dialog-header>
          <h2 hlmDialogTitle>{{ 'payment.dialogTitle' | transloco }}</h2>
          <p hlmDialogDescription>
            @if (store.invoice(); as invoice) {
              {{
                'payment.dialogDescription'
                  | transloco
                    : {
                        number: invoice.number,
                        amount: (invoice.amount | pfMoney: invoice.currency),
                      }
              }}
            }
          </p>
        </hlm-dialog-header>

        <div class="grid gap-3">
          <p id="pf-payment-outcome-label" class="text-sm font-medium">
            {{ 'payment.outcomeLabel' | transloco }}
          </p>
          <hlm-radio-group
            aria-labelledby="pf-payment-outcome-label"
            [value]="outcome()"
            (valueChange)="selectOutcome($event)"
          >
            @for (option of outcomes; track option) {
              <div class="flex items-center gap-3">
                <hlm-radio [value]="option" [inputId]="'pf-payment-' + option">
                  <hlm-radio-indicator indicator />
                </hlm-radio>
                <label hlmLabel [for]="'pf-payment-' + option">
                  {{ 'payment.outcomes.' + option | transloco }}
                </label>
              </div>
            }
          </hlm-radio-group>
          <p class="text-xs text-muted-foreground">{{ 'payment.mockNotice' | transloco }}</p>
        </div>

        <hlm-dialog-footer>
          <button hlmBtn variant="outline" type="button" (click)="closed.emit()">
            {{ 'common.cancel' | transloco }}
          </button>
          <button
            hlmBtn
            type="button"
            [variant]="outcome() === 'Declined' ? 'destructive' : 'default'"
            [disabled]="store.paying()"
            (click)="pay()"
          >
            @if (store.paying()) {
              <hlm-spinner [aria-label]="'common.loading' | transloco" />
            }
            {{ confirmKey() | transloco }}
          </button>
        </hlm-dialog-footer>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class PaymentDialogComponent {
  protected readonly store = inject(InvoiceDetailStore);
  private readonly notifier = inject(Notifier);

  readonly open = input.required<boolean>();
  readonly closed = output<void>();

  protected readonly outcomes = OUTCOMES;
  protected readonly outcome = signal<MockPaymentOutcome>('Approved');
  protected readonly confirmKey = computed(() =>
    this.outcome() === 'Approved' ? 'payment.confirmApproved' : 'payment.confirmDeclined',
  );
  /** Idempotency key of the current attempt; renewed each time the dialog opens. */
  private operationId = newOperationId();

  constructor() {
    effect(() => {
      if (this.open()) {
        untracked(() => {
          this.operationId = newOperationId();
          this.outcome.set('Approved');
        });
      }
    });
  }

  protected selectOutcome(value: unknown): void {
    if (OUTCOMES.includes(value as MockPaymentOutcome)) {
      this.outcome.set(value as MockPaymentOutcome);
    }
  }

  protected async pay(): Promise<void> {
    const result = await this.store.pay(this.operationId, this.outcome());
    if (!result.ok) {
      // Keep the dialog and its operation id: retrying is safe.
      this.notifier.error(result.error);
      return;
    }
    if (result.value.outcome === 'Approved') {
      this.notifier.success('payment.approved');
    } else {
      this.notifier.info('payment.declined');
    }
    this.closed.emit();
  }
}
