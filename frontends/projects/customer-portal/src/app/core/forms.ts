import { Directive, inject } from '@angular/core';
import { NgControl, NonNullableFormBuilder, Validators } from '@angular/forms';
import {
  AddressBody,
  ShipmentAddress,
  countryCodeValidator,
  trimmedRequired,
} from '@parcelflow/shared';

/** Server-side limits mirrored by the forms. */
export const LIMITS = {
  maxWeightKg: 30,
  maxDimensionCm: 150,
  text: 200,
} as const;

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

const text = (required = true) =>
  required
    ? [trimmedRequired, Validators.maxLength(LIMITS.text)]
    : [Validators.maxLength(LIMITS.text)];

/** Saved customer address (label + postal address). */
export function createAddressForm(fb: NonNullableFormBuilder) {
  return fb.group({
    label: ['', text()],
    line1: ['', text()],
    line2: ['', text(false)],
    postalCode: ['', [trimmedRequired, Validators.maxLength(20)]],
    city: ['', text()],
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
    name: ['', text()],
    email: ['', [trimmedRequired, Validators.email]],
    phone: ['', [trimmedRequired, Validators.maxLength(32)]],
    line1: ['', text()],
    line2: ['', text(false)],
    postalCode: ['', [trimmedRequired, Validators.maxLength(20)]],
    city: ['', text()],
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
