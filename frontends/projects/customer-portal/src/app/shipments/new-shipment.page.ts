import { ChangeDetectionStrategy, Component, effect, inject, untracked } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowLeft, lucideCalculator, lucidePackagePlus } from '@ng-icons/lucide';
import {
  Address,
  Notifier,
  PageHeaderComponent,
  PfMoneyPipe,
  PfNumberPipe,
  Profile,
  ShipmentBody,
} from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSeparatorImports } from '@spartan-ng/helm/separator';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { map, startWith } from 'rxjs';
import { FormFieldComponent } from '../core/form-field.component';
import {
  LIMITS,
  createShipmentAddressForm,
  toShipmentAddress,
  volumetricWeightValidator,
} from '../core/forms';
import { reportFormError } from '../core/result';
import { ProfileStore } from '../profile/profile.store';
import { NewShipmentStore } from './new-shipment.store';
import { ShipmentAddressFieldsComponent } from './shipment-address-fields.component';

/** Shipment creation form with a live, debounced quote. */
@Component({
  selector: 'pf-new-shipment-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    NgIcon,
    PageHeaderComponent,
    PfMoneyPipe,
    PfNumberPipe,
    HlmButtonImports,
    HlmCardImports,
    HlmLabelImports,
    HlmNativeSelectImports,
    HlmSeparatorImports,
    HlmSpinnerImports,
    FormFieldComponent,
    ShipmentAddressFieldsComponent,
  ],
  providers: [
    NewShipmentStore,
    ProfileStore,
    provideIcons({ lucideArrowLeft, lucideCalculator, lucidePackagePlus }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <pf-page-header
      [title]="'newShipment.title' | transloco"
      [subtitle]="'newShipment.subtitle' | transloco"
    >
      <a hlmBtn variant="outline" routerLink="/shipments">
        <ng-icon name="lucideArrowLeft" />
        {{ 'shipments.backToList' | transloco }}
      </a>
    </pf-page-header>

    <form
      class="grid items-start gap-6 lg:grid-cols-[minmax(0,1fr)_20rem]"
      [formGroup]="form"
      (ngSubmit)="submit()"
      novalidate
    >
      <div class="grid min-w-0 gap-6">
        <section hlmCard class="min-w-0" aria-labelledby="pf-sender-title">
          <div hlmCardHeader>
            <h2 hlmCardTitle id="pf-sender-title">1. {{ 'newShipment.sender' | transloco }}</h2>
            <p hlmCardDescription>
              {{ 'newShipment.senderHint' | transloco }}
            </p>
          </div>
          <div hlmCardContent class="grid gap-4">
            @if (profiles.addresses().length > 0) {
              <div class="flex flex-col gap-1.5">
                <label hlmLabel for="pf-sender-saved">{{
                  'newShipment.savedAddress' | transloco
                }}</label>
                <hlm-native-select
                  class="w-full"
                  selectId="pf-sender-saved"
                  [value]="''"
                  (valueChange)="useSavedAddress($event)"
                >
                  <option value="">{{ 'newShipment.chooseAddress' | transloco }}</option>
                  @for (address of profiles.addresses(); track address.id) {
                    <option [value]="address.id">
                      {{ address.label }} — {{ address.line1 }}, {{ address.city }}
                    </option>
                  }
                </hlm-native-select>
              </div>
            }
            <pf-shipment-address-fields
              prefix="pf-sender"
              section="section-sender"
              [group]="form.controls.sender"
            />
          </div>
        </section>

        <section hlmCard class="min-w-0" aria-labelledby="pf-recipient-title">
          <div hlmCardHeader>
            <h2 hlmCardTitle id="pf-recipient-title">
              2. {{ 'newShipment.recipient' | transloco }}
            </h2>
          </div>
          <div hlmCardContent>
            <pf-shipment-address-fields
              prefix="pf-recipient"
              section="section-recipient"
              [group]="form.controls.recipient"
            />
          </div>
        </section>

        <section hlmCard class="min-w-0" aria-labelledby="pf-parcel-title">
          <div hlmCardHeader>
            <h2 hlmCardTitle id="pf-parcel-title">3. {{ 'newShipment.parcel' | transloco }}</h2>
            <p hlmCardDescription>
              {{ 'newShipment.parcelHint' | transloco: limits }}
            </p>
          </div>
          <div hlmCardContent class="grid grid-cols-2 gap-4 sm:grid-cols-4">
            <pf-form-field
              fieldId="pf-parcel-weight"
              labelKey="newShipment.fields.weightKg"
              type="number"
              inputMode="decimal"
              step="0.01"
              [min]="0"
              [max]="limits.maxWeightKg"
              [control]="form.controls.parcel.controls.weightKg"
            />
            <pf-form-field
              fieldId="pf-parcel-length"
              labelKey="newShipment.fields.lengthCm"
              type="number"
              inputMode="decimal"
              [min]="0"
              [max]="limits.maxDimensionCm"
              [control]="form.controls.parcel.controls.lengthCm"
            />
            <pf-form-field
              fieldId="pf-parcel-width"
              labelKey="newShipment.fields.widthCm"
              type="number"
              inputMode="decimal"
              [min]="0"
              [max]="limits.maxDimensionCm"
              [control]="form.controls.parcel.controls.widthCm"
            />
            <pf-form-field
              fieldId="pf-parcel-height"
              labelKey="newShipment.fields.heightCm"
              type="number"
              inputMode="decimal"
              [min]="0"
              [max]="limits.maxDimensionCm"
              [control]="form.controls.parcel.controls.heightCm"
            />
            @if (form.controls.parcel.hasError('volumetricWeight')) {
              <p
                class="col-span-full text-sm text-destructive"
                role="alert"
                data-testid="volumetric-error"
              >
                {{ 'newShipment.volumetricTooHigh' | transloco: limits }}
              </p>
            }
          </div>
        </section>
      </div>

      <aside class="lg:sticky lg:top-20" aria-labelledby="pf-quote-title">
        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle id="pf-quote-title" class="flex items-center gap-2">
              <ng-icon name="lucideCalculator" aria-hidden="true" />
              {{ 'newShipment.quote.title' | transloco }}
            </h2>
          </div>
          <div hlmCardContent class="grid gap-3" aria-live="polite">
            @if (store.quoting()) {
              <p class="flex items-center gap-2 text-sm text-muted-foreground">
                <hlm-spinner [aria-label]="'common.loading' | transloco" />
                {{ 'newShipment.quote.loading' | transloco }}
              </p>
            } @else if (store.quote(); as quote) {
              <p class="text-3xl font-semibold tabular-nums" data-testid="quote-price">
                {{ quote.price | pfMoney: quote.currency }}
              </p>
              <hlm-separator />
              <dl class="grid grid-cols-2 gap-2 text-sm">
                <dt class="text-muted-foreground">{{ 'newShipment.quote.zone' | transloco }}</dt>
                <dd class="text-end font-medium">
                  {{ 'enum.DeliveryZone.' + quote.zone | transloco }}
                </dd>
                <dt class="text-muted-foreground">
                  {{ 'newShipment.quote.billableWeight' | transloco }}
                </dt>
                <dd class="text-end font-medium tabular-nums">
                  {{
                    'newShipment.quote.kg'
                      | transloco: { value: (quote.billableWeightKg | pfNumber) }
                  }}
                </dd>
              </dl>
            } @else if (store.quoteError()) {
              <p class="text-sm text-destructive">{{ 'newShipment.quote.error' | transloco }}</p>
            } @else {
              <p class="text-sm text-muted-foreground">
                {{ 'newShipment.quote.hint' | transloco }}
              </p>
            }
          </div>
          <div hlmCardFooter class="flex-col items-stretch gap-2">
            <button hlmBtn size="lg" type="submit" [disabled]="store.creating()">
              @if (store.creating()) {
                <hlm-spinner [aria-label]="'common.loading' | transloco" />
              } @else {
                <ng-icon name="lucidePackagePlus" />
              }
              {{ 'newShipment.submit' | transloco }}
            </button>
            <p class="text-xs text-muted-foreground">
              {{ 'newShipment.paymentNotice' | transloco }}
            </p>
          </div>
        </section>
      </aside>
    </form>
  `,
})
export class NewShipmentPage {
  protected readonly store = inject(NewShipmentStore);
  protected readonly profiles = inject(ProfileStore);
  private readonly notifier = inject(Notifier);
  private readonly router = inject(Router);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly limits = LIMITS;

  protected readonly form = this.fb.group({
    sender: createShipmentAddressForm(this.fb),
    recipient: createShipmentAddressForm(this.fb),
    parcel: this.fb.group(
      {
        weightKg: [
          1,
          [Validators.required, Validators.min(0.01), Validators.max(LIMITS.maxWeightKg)],
        ],
        lengthCm: [30, this.dimensionValidators()],
        widthCm: [20, this.dimensionValidators()],
        heightCm: [15, this.dimensionValidators()],
      },
      { validators: volumetricWeightValidator },
    ),
  });

  constructor() {
    this.profiles.load();
    // Prefill the sender once from the profile, unless the customer already typed.
    let prefilled = false;
    effect(() => {
      const profile = this.profiles.profile();
      if (profile && !prefilled) {
        prefilled = true;
        untracked(() => this.prefillSender(profile));
      }
    });
    this.store.requestQuote(
      this.form.valueChanges.pipe(
        startWith(null),
        map(() => (this.form.valid ? this.body() : null)),
      ),
    );
  }

  private dimensionValidators() {
    // Shipment accepts any positive dimension up to the limit (two decimals).
    return [Validators.required, Validators.min(0.01), Validators.max(LIMITS.maxDimensionCm)];
  }

  private prefillSender(profile: Profile): void {
    const sender = this.form.controls.sender;
    if (sender.dirty) {
      return;
    }
    sender.patchValue({
      name: `${profile.firstName} ${profile.lastName}`.trim(),
      email: profile.email,
      phone: profile.phone ?? '',
    });
    if (profile.addresses[0]) {
      this.applyAddress(profile.addresses[0]);
    }
  }

  protected useSavedAddress(addressId: string | null | undefined): void {
    const address = this.profiles.addresses().find((item) => item.id === addressId);
    if (address) {
      this.applyAddress(address);
    }
  }

  private applyAddress(address: Address): void {
    this.form.controls.sender.patchValue({
      line1: address.line1,
      line2: address.line2 ?? '',
      postalCode: address.postalCode,
      city: address.city,
      countryCode: address.countryCode,
    });
  }

  private body(): ShipmentBody {
    const parcel = this.form.controls.parcel.getRawValue();
    return {
      sender: toShipmentAddress(this.form.controls.sender),
      recipient: toShipmentAddress(this.form.controls.recipient),
      parcel: {
        weightKg: Number(parcel.weightKg),
        lengthCm: Number(parcel.lengthCm),
        widthCm: Number(parcel.widthCm),
        heightCm: Number(parcel.heightCm),
      },
    };
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.notifier.info('newShipment.fixErrors');
      return;
    }
    const result = await this.store.create(this.body());
    if (result.ok) {
      this.notifier.success('newShipment.created', { number: result.value.trackingNumber });
      await this.router.navigate(['/shipments', result.value.id]);
    } else {
      reportFormError(this.form, result.error, this.notifier);
    }
  }
}
