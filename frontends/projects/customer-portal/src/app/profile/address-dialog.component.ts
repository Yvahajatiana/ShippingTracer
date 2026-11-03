import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  untracked,
} from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { Address, Notifier } from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { FormFieldComponent } from '../core/form-field.component';
import { createAddressForm, toAddressBody } from '../core/forms';
import { reportFormError } from '../core/result';
import { ProfileStore } from './profile.store';

/** Value of the dialog: closed, adding a new address, or editing an existing one. */
export type AddressDialogMode =
  | { kind: 'closed' }
  | { kind: 'add' }
  | { kind: 'edit'; address: Address };

/** Add/edit dialog for a saved address. */
@Component({
  selector: 'pf-address-dialog',
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    HlmButtonImports,
    HlmDialogImports,
    HlmSpinnerImports,
    FormFieldComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <hlm-dialog [state]="state()" (closed)="closed.emit()">
      <hlm-dialog-content
        *hlmDialogPortal="let ctx"
        class="max-h-[90dvh] overflow-y-auto sm:max-w-lg"
        [closeLabel]="'common.close' | transloco"
      >
        <hlm-dialog-header>
          <h2 hlmDialogTitle>
            {{ (mode().kind === 'edit' ? 'address.editTitle' : 'address.addTitle') | transloco }}
          </h2>
          <p hlmDialogDescription>{{ 'address.dialogDescription' | transloco }}</p>
        </hlm-dialog-header>
        <form class="grid gap-4" [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <pf-form-field
            fieldId="pf-address-label"
            labelKey="address.fields.label"
            [control]="form.controls.label"
          />
          <pf-form-field
            fieldId="pf-address-line1"
            labelKey="address.fields.line1"
            autocomplete="address-line1"
            [control]="form.controls.line1"
          />
          <pf-form-field
            fieldId="pf-address-line2"
            labelKey="address.fields.line2"
            autocomplete="address-line2"
            required="false"
            [control]="form.controls.line2"
          />
          <div class="grid gap-4 sm:grid-cols-[8rem_1fr_6rem]">
            <pf-form-field
              fieldId="pf-address-postal-code"
              labelKey="address.fields.postalCode"
              autocomplete="postal-code"
              [control]="form.controls.postalCode"
            />
            <pf-form-field
              fieldId="pf-address-city"
              labelKey="address.fields.city"
              autocomplete="address-level2"
              [control]="form.controls.city"
            />
            <pf-form-field
              fieldId="pf-address-country"
              labelKey="address.fields.countryCode"
              autocomplete="country"
              uppercase
              [maxLength]="2"
              [control]="form.controls.countryCode"
            />
          </div>
          <hlm-dialog-footer>
            <button hlmBtn variant="outline" type="button" (click)="closed.emit()">
              {{ 'common.cancel' | transloco }}
            </button>
            <button hlmBtn type="submit" [disabled]="store.saving()">
              @if (store.saving()) {
                <hlm-spinner [aria-label]="'common.loading' | transloco" />
              }
              {{ 'common.save' | transloco }}
            </button>
          </hlm-dialog-footer>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class AddressDialogComponent {
  protected readonly store = inject(ProfileStore);
  private readonly notifier = inject(Notifier);

  readonly mode = input.required<AddressDialogMode>();
  readonly closed = output<void>();

  protected readonly form = createAddressForm(inject(NonNullableFormBuilder));
  protected readonly state = computed(() => (this.mode().kind === 'closed' ? 'closed' : 'open'));

  constructor() {
    // Reset the form each time the dialog opens.
    effect(() => {
      const mode = this.mode();
      untracked(() => {
        if (mode.kind === 'edit') {
          const { label, line1, line2, postalCode, city, countryCode } = mode.address;
          this.form.reset({ label, line1, line2: line2 ?? '', postalCode, city, countryCode });
        } else if (mode.kind === 'add') {
          this.form.reset();
        }
      });
    });
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const mode = this.mode();
    const body = toAddressBody(this.form);
    const result =
      mode.kind === 'edit'
        ? await this.store.updateAddress(mode.address.id, body)
        : await this.store.addAddress(body);
    if (result.ok) {
      this.notifier.success(mode.kind === 'edit' ? 'common.saved' : 'address.added');
      this.closed.emit();
    } else {
      reportFormError(this.form, result.error, this.notifier);
    }
  }
}
