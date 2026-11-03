import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { FormFieldComponent } from '../core/form-field.component';
import { ShipmentAddressForm } from '../core/forms';

/** Contact and postal fields of a shipment sender or recipient. */
@Component({
  selector: 'pf-shipment-address-fields',
  imports: [FormFieldComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'grid gap-4 sm:grid-cols-2' },
  template: `
    <pf-form-field
      class="sm:col-span-2"
      [fieldId]="prefix() + '-name'"
      labelKey="newShipment.fields.name"
      [autocomplete]="section() + ' name'"
      [control]="group().controls.name"
    />
    <pf-form-field
      [fieldId]="prefix() + '-email'"
      labelKey="newShipment.fields.email"
      type="email"
      [autocomplete]="section() + ' email'"
      [control]="group().controls.email"
    />
    <pf-form-field
      [fieldId]="prefix() + '-phone'"
      labelKey="newShipment.fields.phone"
      type="tel"
      [autocomplete]="section() + ' tel'"
      [control]="group().controls.phone"
    />
    <pf-form-field
      class="sm:col-span-2"
      [fieldId]="prefix() + '-line1'"
      labelKey="address.fields.line1"
      [autocomplete]="section() + ' address-line1'"
      [control]="group().controls.line1"
    />
    <pf-form-field
      class="sm:col-span-2"
      [fieldId]="prefix() + '-line2'"
      labelKey="address.fields.line2"
      required="false"
      [autocomplete]="section() + ' address-line2'"
      [control]="group().controls.line2"
    />
    <pf-form-field
      [fieldId]="prefix() + '-postal-code'"
      labelKey="address.fields.postalCode"
      [autocomplete]="section() + ' postal-code'"
      [control]="group().controls.postalCode"
    />
    <pf-form-field
      [fieldId]="prefix() + '-city'"
      labelKey="address.fields.city"
      [autocomplete]="section() + ' address-level2'"
      [control]="group().controls.city"
    />
    <pf-form-field
      [fieldId]="prefix() + '-country'"
      labelKey="address.fields.countryCode"
      uppercase
      [maxLength]="2"
      [autocomplete]="section() + ' country'"
      [control]="group().controls.countryCode"
    />
  `,
})
export class ShipmentAddressFieldsComponent {
  readonly group = input.required<ShipmentAddressForm>();
  /** Unique id prefix for the inputs (labels need distinct ids). */
  readonly prefix = input.required<string>();
  /** Autofill section token: `shipping` for the sender, `section-recipient` for the recipient. */
  readonly section = input.required<string>();
}
