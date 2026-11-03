import { Directive, inject } from '@angular/core';
import {
  AbstractControl,
  NgControl,
  NonNullableFormBuilder,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import {
  AddressBody,
  ShipmentAddress,
  countryCodeValidator,
  trimmedRequired,
} from '@parcelflow/shared';

/**
 * Server-side limits mirrored by the forms: Shipment `ParcelInputValidator` / `ShipmentAddressValidator`
 * and Customer profile and address validators.
 */
export const LIMITS = {
  maxWeightKg: 30,
  maxDimensionCm: 200,
  /** Volumetric weight (L × W × H / divisor) may not exceed this either. */
  maxVolumetricWeightKg: 30,
  volumetricDivisor: 5000,
  text: 200,
  name: 100,
  city: 100,
  label: 80,
  phone: 30,
  email: 254,
} as const;

/** Volumetric weight of a parcel in kg, as computed by the Shipment service. */
export function volumetricWeightKg(lengthCm: number, widthCm: number, heightCm: number): number {
  return (lengthCm * widthCm * heightCm) / LIMITS.volumetricDivisor;
}

/** Group validator rejecting parcels whose volumetric weight exceeds the carrier limit. */
export const volumetricWeightValidator: ValidatorFn = (group: AbstractControl) => {
  const { lengthCm, widthCm, heightCm } = group.value as Record<string, number | null>;
  if (!lengthCm || !widthCm || !heightCm) {
    return null;
  }
  return volumetricWeightKg(lengthCm, widthCm, heightCm) > LIMITS.maxVolumetricWeightKg
    ? { volumetricWeight: { max: LIMITS.maxVolumetricWeightKg } }
    : null;
};

/** Upper-cases the bound control value while typing (ISO country codes). */
@Directive({
  selector: 'input[pfUppercase]',
  host: { '(input)': 'normalize()', autocapitalize: 'characters' },
})
export class UppercaseDirective {
  private readonly control = inject(NgControl, { self: true });

  protected normalize(): void {
    const value = this.control.value;
    if (typeof value === 'string' && value !== value.toUpperCase()) {
      this.control.control?.setValue(value.toUpperCase());
    }
  }
}

const text = (required = true, max: number = LIMITS.text) =>
  required ? [trimmedRequired, Validators.maxLength(max)] : [Validators.maxLength(max)];

/** Saved customer address (label + postal address). */
export function createAddressForm(fb: NonNullableFormBuilder) {
  return fb.group({
    label: ['', text(true, LIMITS.label)],
    line1: ['', text()],
    line2: ['', text(false)],
    postalCode: ['', [trimmedRequired, Validators.maxLength(20)]],
    city: ['', text(true, LIMITS.city)],
    countryCode: ['FR', [trimmedRequired, countryCodeValidator]],
  });
}
export type AddressForm = ReturnType<typeof createAddressForm>;

export function toAddressBody(form: AddressForm): AddressBody {
  const value = form.getRawValue();
  return {
    label: value.label.trim(),
    line1: value.line1.trim(),
    line2: value.line2.trim() || null,
    postalCode: value.postalCode.trim(),
    city: value.city.trim(),
    countryCode: value.countryCode.trim(),
  };
}

/** Sender or recipient of a shipment (contact + postal address). */
export function createShipmentAddressForm(fb: NonNullableFormBuilder) {
  return fb.group({
    name: ['', text(true, LIMITS.name)],
    email: ['', [trimmedRequired, Validators.email, Validators.maxLength(LIMITS.email)]],
    phone: ['', [trimmedRequired, Validators.maxLength(LIMITS.phone)]],
    line1: ['', text()],
    line2: ['', text(false)],
    postalCode: ['', [trimmedRequired, Validators.maxLength(20)]],
    city: ['', text(true, LIMITS.city)],
    countryCode: ['FR', [trimmedRequired, countryCodeValidator]],
  });
}
export type ShipmentAddressForm = ReturnType<typeof createShipmentAddressForm>;

export function toShipmentAddress(form: ShipmentAddressForm): ShipmentAddress {
  const value = form.getRawValue();
  return {
    name: value.name.trim(),
    email: value.email.trim(),
    phone: value.phone.trim(),
    line1: value.line1.trim(),
    line2: value.line2.trim() || null,
    postalCode: value.postalCode.trim(),
    city: value.city.trim(),
    countryCode: value.countryCode.trim(),
  };
}
