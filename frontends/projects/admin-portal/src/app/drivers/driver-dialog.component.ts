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
import { Driver, FieldErrorComponent, Notifier, trimmedRequired } from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { reportFormError, setServerError } from '../core/form-errors';
import { DriversStore } from './drivers.store';

/** Create (driver = null) or edit dialog; the Keycloak subject is the immutable key. */
@Component({
  selector: 'pf-driver-dialog',
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    FieldErrorComponent,
    HlmButtonImports,
    HlmDialogImports,
    HlmFieldImports,
    HlmInputImports,
    HlmSpinnerImports,
    HlmSwitchImports,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <hlm-dialog [state]="open() ? 'open' : 'closed'" (closed)="open.set(false)">
      <hlm-dialog-content
        *hlmDialogPortal
        class="sm:max-w-md"
        [closeLabel]="'common.close' | transloco"
      >
        <hlm-dialog-header>
          <h2 hlmDialogTitle>
            {{ (editing() ? 'drivers.editTitle' : 'drivers.createTitle') | transloco }}
          </h2>
          <p hlmDialogDescription>{{ 'drivers.dialogDescription' | transloco }}</p>
        </hlm-dialog-header>
        <form class="grid gap-4" [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <div hlmField>
            <label hlmFieldLabel for="driver-subject">{{
              'drivers.fields.subject' | transloco
            }}</label>
            <input
              hlmInput
              id="driver-subject"
              class="font-mono"
              formControlName="subject"
              autocomplete="off"
              maxlength="200"
              aria-describedby="driver-subject-hint"
            />
            <p id="driver-subject-hint" class="text-xs text-muted-foreground">
              {{ 'drivers.subjectHint' | transloco }}
            </p>
            <pf-field-error [control]="form.controls.subject" />
          </div>
          <div hlmField>
            <label hlmFieldLabel for="driver-name">{{
              'drivers.fields.displayName' | transloco
            }}</label>
            <input
              hlmInput
              id="driver-name"
              formControlName="displayName"
              autocomplete="name"
              maxlength="120"
            />
            <pf-field-error [control]="form.controls.displayName" />
          </div>
          @if (editing()) {
            <div hlmField orientation="horizontal">
              <hlm-switch inputId="driver-active" formControlName="isActive" />
              <label hlmFieldLabel for="driver-active">{{ 'common.active' | transloco }}</label>
            </div>
          }
          <hlm-dialog-footer>
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
export class DriverDialogComponent {
  protected readonly store = inject(DriversStore);
  private readonly notifier = inject(Notifier);
  private readonly transloco = inject(TranslocoService);

  readonly open = model(false);
  readonly driver = input<Driver | null>(null);
  protected readonly editing = computed(() => this.driver() !== null);

  protected readonly form = inject(NonNullableFormBuilder).group({
    subject: ['', [trimmedRequired, Validators.maxLength(200)]],
    displayName: ['', [trimmedRequired, Validators.maxLength(120)]],
    isActive: [true],
  });

  constructor() {
    effect(() => {
      if (!this.open()) {
        return;
      }
      const driver = this.driver();
      untracked(() => {
        this.form.reset({
          subject: driver?.subject ?? '',
          displayName: driver?.displayName ?? '',
          isActive: driver?.isActive ?? true,
        });
        if (driver) {
          this.form.controls.subject.disable();
        } else {
          this.form.controls.subject.enable();
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
    const displayName = raw.displayName.trim();
    const driver = this.driver();
    try {
      if (driver) {
        await this.store.update(driver.subject, displayName, raw.isActive);
        this.notifier.success('common.saved');
      } else {
        await this.store.create(raw.subject.trim(), displayName);
        this.notifier.success('drivers.created');
      }
      this.open.set(false);
    } catch (error) {
      const apiError = reportFormError(this.form, error, this.notifier);
      if (apiError.status === 409 && !driver) {
        setServerError(
          this.form.controls.subject,
          this.transloco.translate('drivers.subjectTaken'),
        );
      }
    }
  }
}
