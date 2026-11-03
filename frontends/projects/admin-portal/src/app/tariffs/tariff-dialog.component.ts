import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  model,
  untracked,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { FieldErrorComponent, Notifier, PfMoneyPipe, Tariff, enumKey } from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { reportFormError } from '../core/form-errors';
import { TariffsStore } from './tariffs.store';

/** Prices are decimals with at most two fraction digits. */
const MONEY_PATTERN = /^\d+(\.\d{1,2})?$/;

/** Edit dialog for one zone tariff. */
@Component({
  selector: 'pf-tariff-dialog',
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    PfMoneyPipe,
    FieldErrorComponent,
    HlmButtonImports,
    HlmDialogImports,
    HlmFieldImports,
    HlmInputImports,
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
        @if (tariff(); as current) {
          <hlm-dialog-header>
            <h2 hlmDialogTitle>
              {{ 'tariffs.editTitle' | transloco: { zone: (zoneKey() | transloco) } }}
            </h2>
            <p hlmDialogDescription>
              {{ 'tariffs.dialogDescription' | transloco: { currency: current.currency } }}
            </p>
          </hlm-dialog-header>
          <form class="grid gap-4" [formGroup]="form" (ngSubmit)="submit()" novalidate>
            <div hlmField>
              <label hlmFieldLabel for="tariff-base">
                {{ 'tariffs.basePrice' | transloco }} ({{ current.currency }})
              </label>
              <input
                hlmInput
                id="tariff-base"
                type="number"
                inputmode="decimal"
                min="0"
                step="0.01"
                formControlName="basePrice"
              />
              <pf-field-error [control]="form.controls.basePrice" />
            </div>
            <div hlmField>
              <label hlmFieldLabel for="tariff-per-kg">
                {{ 'tariffs.pricePerKg' | transloco }} ({{ current.currency }})
              </label>
              <input
                hlmInput
                id="tariff-per-kg"
                type="number"
                inputmode="decimal"
                min="0.01"
                step="0.01"
                formControlName="pricePerKg"
              />
              <pf-field-error [control]="form.controls.pricePerKg" />
            </div>
            <p class="text-muted-foreground">
              {{
                'tariffs.example' | transloco: { price: (example() | pfMoney: current.currency) }
              }}
            </p>
            <hlm-dialog-footer>
              <button hlmBtn type="button" variant="outline" hlmDialogClose>
                {{ 'common.cancel' | transloco }}
              </button>
              <button hlmBtn type="submit" [disabled]="store.savingZone() !== null">
                @if (store.savingZone() !== null) {
                  <hlm-spinner class="size-4" [aria-label]="'common.loading' | transloco" />
                }
                {{ 'common.save' | transloco }}
              </button>
            </hlm-dialog-footer>
          </form>
        }
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class TariffDialogComponent {
  protected readonly store = inject(TariffsStore);
  private readonly notifier = inject(Notifier);

  readonly open = model(false);
  readonly tariff = input<Tariff | null>(null);
  protected readonly zoneKey = computed(() =>
    enumKey('DeliveryZone', this.tariff()?.zone ?? 'Domestic'),
  );

  protected readonly form = inject(NonNullableFormBuilder).group({
    // Mirrors the Shipment service: 0 <= base <= 10000, 0 < per kg <= 10000, two decimals.
    basePrice: [
      0,
      [
        Validators.required,
        Validators.min(0),
        Validators.max(10000),
        Validators.pattern(MONEY_PATTERN),
      ],
    ],
    pricePerKg: [
      0,
      [
        Validators.required,
        Validators.min(0.01),
        Validators.max(10000),
        Validators.pattern(MONEY_PATTERN),
      ],
    ],
  });

  private readonly formValue = toSignal(this.form.valueChanges, {
    initialValue: this.form.getRawValue(),
  });
  /** Price of a 2 kg parcel with the edited values, as a sanity check for the admin. */
  protected readonly example = computed(() => {
    const { basePrice, pricePerKg } = this.formValue();
    return Math.round(((Number(basePrice) || 0) + (Number(pricePerKg) || 0) * 2) * 100) / 100;
  });

  constructor() {
    effect(() => {
      const tariff = this.tariff();
      if (this.open() && tariff) {
        untracked(() =>
          this.form.reset({ basePrice: tariff.basePrice, pricePerKg: tariff.pricePerKg }),
        );
      }
    });
  }

  async submit(): Promise<void> {
    const tariff = this.tariff();
    if (!tariff || this.store.savingZone() !== null) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { basePrice, pricePerKg } = this.form.getRawValue();
    try {
      await this.store.update(tariff.zone, {
        basePrice: Number(basePrice),
        pricePerKg: Number(pricePerKg),
      });
      this.notifier.success('tariffs.updated');
      this.open.set(false);
    } catch (error) {
      reportFormError(this.form, error, this.notifier);
    }
  }
}
