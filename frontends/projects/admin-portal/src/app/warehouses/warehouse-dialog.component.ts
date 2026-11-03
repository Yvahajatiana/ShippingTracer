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
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import {
  FieldErrorComponent,
  Notifier,
  WAREHOUSE_KINDS,
  Warehouse,
  WarehouseKind,
  countryCodeValidator,
  trimmedRequired,
} from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { reportFormError, setServerError } from '../core/form-errors';
import { WarehousesStore } from './warehouses.store';

/** Mirrors WarehouseRules.IsCode on the server (uppercase letters, digits and dashes). */
export const WAREHOUSE_CODE_PATTERN = /^[A-Z0-9][A-Z0-9-]{0,39}$/;

/** Create (warehouse = null) or edit dialog; code and kind are immutable after creation. */
@Component({
  selector: 'pf-warehouse-dialog',
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    FieldErrorComponent,
    HlmButtonImports,
    HlmDialogImports,
    HlmFieldImports,
    HlmInputImports,
    HlmNativeSelectImports,
    HlmSpinnerImports,
    HlmSwitchImports,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <hlm-dialog [state]="open() ? 'open' : 'closed'" (closed)="open.set(false)">
      <hlm-dialog-content
        *hlmDialogPortal
        class="max-h-[90vh] overflow-y-auto sm:max-w-xl"
        [closeLabel]="'common.close' | transloco"
      >
        <hlm-dialog-header>
          <h2 hlmDialogTitle>
            {{ (editing() ? 'warehouses.editTitle' : 'warehouses.createTitle') | transloco }}
          </h2>
          <p hlmDialogDescription>
            {{
              (editing() ? 'warehouses.editDescription' : 'warehouses.createDescription')
                | transloco
            }}
          </p>
        </hlm-dialog-header>
        <form class="grid gap-4 sm:grid-cols-2" [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <div hlmField>
            <label hlmFieldLabel for="warehouse-code">{{
              'warehouses.fields.code' | transloco
            }}</label>
            <input
              hlmInput
              id="warehouse-code"
              class="uppercase"
              formControlName="code"
              autocomplete="off"
              maxlength="40"
              aria-describedby="warehouse-code-hint"
            />
            <p id="warehouse-code-hint" class="text-xs text-muted-foreground">
              {{ 'warehouses.codeHint' | transloco }}
            </p>
            <pf-field-error [control]="form.controls.code" />
          </div>
          <div hlmField>
            <label hlmFieldLabel for="warehouse-kind">{{
              'warehouses.fields.kind' | transloco
            }}</label>
            <hlm-native-select class="w-full" selectId="warehouse-kind" formControlName="kind">
              @for (kind of kinds; track kind) {
                <option [value]="kind">{{ 'enum.WarehouseKind.' + kind | transloco }}</option>
              }
            </hlm-native-select>
          </div>
          <div hlmField class="sm:col-span-2">
            <label hlmFieldLabel for="warehouse-name">{{
              'warehouses.fields.name' | transloco
            }}</label>
            <input hlmInput id="warehouse-name" formControlName="name" maxlength="160" />
            <pf-field-error [control]="form.controls.name" />
          </div>
          <div hlmField class="sm:col-span-2">
            <label hlmFieldLabel for="warehouse-street">
              {{ 'warehouses.fields.street' | transloco }}
            </label>
            <input
              hlmInput
              id="warehouse-street"
              formControlName="street"
              autocomplete="street-address"
              maxlength="200"
            />
            <pf-field-error [control]="form.controls.street" />
          </div>
          <div hlmField>
            <label hlmFieldLabel for="warehouse-postal">
              {{ 'warehouses.fields.postalCode' | transloco }}
            </label>
            <input
              hlmInput
              id="warehouse-postal"
              formControlName="postalCode"
              autocomplete="postal-code"
              maxlength="20"
            />
            <pf-field-error [control]="form.controls.postalCode" />
          </div>
          <div hlmField>
            <label hlmFieldLabel for="warehouse-city">{{
              'warehouses.fields.city' | transloco
            }}</label>
            <input
              hlmInput
              id="warehouse-city"
              formControlName="city"
              autocomplete="address-level2"
              maxlength="100"
            />
            <pf-field-error [control]="form.controls.city" />
          </div>
          <div hlmField>
            <label hlmFieldLabel for="warehouse-country">
              {{ 'warehouses.fields.countryCode' | transloco }}
            </label>
            <input
              hlmInput
              id="warehouse-country"
              class="uppercase"
              formControlName="countryCode"
              autocomplete="country"
              maxlength="2"
            />
            <pf-field-error [control]="form.controls.countryCode" />
          </div>
          @if (editing()) {
            <div hlmField orientation="horizontal" class="self-end">
              <hlm-switch inputId="warehouse-active" formControlName="isActive" />
              <label hlmFieldLabel for="warehouse-active">{{ 'common.active' | transloco }}</label>
            </div>
          }
          <hlm-dialog-footer class="sm:col-span-2">
            <button hlmBtn type="button" variant="outline" hlmDialogClose>
              {{ 'common.cancel' | transloco }}
            </button>
            <button hlmBtn type="submit" [disabled]="store.saving()">
              @if (store.saving()) {
                <hlm-spinner class="size-4" [aria-label]="'common.loading' | transloco" />
              }
              {{ (editing() ? 'common.save' : 'common.create') | transloco }}
            </button>
          </hlm-dialog-footer>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class WarehouseDialogComponent {
  protected readonly store = inject(WarehousesStore);
  private readonly notifier = inject(Notifier);
  private readonly transloco = inject(TranslocoService);

  readonly open = model(false);
  readonly warehouse = input<Warehouse | null>(null);
  protected readonly editing = computed(() => this.warehouse() !== null);
  protected readonly kinds = WAREHOUSE_KINDS;

  protected readonly form = inject(NonNullableFormBuilder).group({
    code: ['', [trimmedRequired, Validators.pattern(WAREHOUSE_CODE_PATTERN)]],
    kind: ['Warehouse' as WarehouseKind, Validators.required],
    name: ['', [trimmedRequired, Validators.maxLength(160)]],
    street: ['', [trimmedRequired, Validators.maxLength(200)]],
    postalCode: ['', [trimmedRequired, Validators.maxLength(20)]],
    city: ['', [trimmedRequired, Validators.maxLength(100)]],
    countryCode: ['FR', [trimmedRequired, countryCodeValidator]],
    isActive: [true],
  });

  constructor() {
    // Codes are uppercase on the server: normalize while typing so validation matches.
    for (const control of [this.form.controls.code, this.form.controls.countryCode]) {
      control.valueChanges.subscribe((value) => {
        const upper = value.toUpperCase();
        if (upper !== value) {
          control.setValue(upper, { emitEvent: false });
        }
      });
    }

    effect(() => {
      if (!this.open()) {
        return;
      }
      const warehouse = this.warehouse();
      untracked(() => {
        this.form.reset({
          code: warehouse?.code ?? '',
          kind: warehouse?.kind ?? 'Warehouse',
          name: warehouse?.name ?? '',
          street: warehouse?.street ?? '',
          postalCode: warehouse?.postalCode ?? '',
          city: warehouse?.city ?? '',
          countryCode: warehouse?.countryCode ?? 'FR',
          isActive: warehouse?.isActive ?? true,
        });
        // Code and kind cannot change once the warehouse exists.
        if (warehouse) {
          this.form.controls.code.disable();
          this.form.controls.kind.disable();
        } else {
          this.form.controls.code.enable();
          this.form.controls.kind.enable();
        }
      });
    });
  }

  async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    if (this.store.saving()) {
      return;
    }
    const raw = this.form.getRawValue();
    const address = {
      name: raw.name.trim(),
      street: raw.street.trim(),
      postalCode: raw.postalCode.trim(),
      city: raw.city.trim(),
      countryCode: raw.countryCode.trim().toUpperCase(),
    };
    const warehouse = this.warehouse();
    try {
      if (warehouse) {
        await this.store.update(warehouse.id, { ...address, isActive: raw.isActive });
        this.notifier.success('common.saved');
      } else {
        await this.store.create({
          ...address,
          code: raw.code.trim().toUpperCase(),
          kind: raw.kind,
        });
        this.notifier.success('warehouses.created');
      }
      this.open.set(false);
    } catch (error) {
      const apiError = reportFormError(this.form, error, this.notifier);
      if (apiError.status === 409 && !warehouse) {
        // Duplicate code: point at the field in addition to the toast.
        setServerError(this.form.controls.code, this.transloco.translate('warehouses.codeTaken'));
      }
    }
  }
}
