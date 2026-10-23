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
import { TranslocoPipe } from '@jsverse/transloco';
import {
  Company,
  CompanyBody,
  FieldErrorComponent,
  Notifier,
  trimmedRequired,
} from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { reportFormError } from '../core/form-errors';
import { CompaniesStore } from './companies.store';

/** Create (company = null) or edit dialog for a company. */
@Component({
  selector: 'pf-company-dialog',
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
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
        class="sm:max-w-lg"
        [closeLabel]="'common.close' | transloco"
      >
        <hlm-dialog-header>
          <h2 hlmDialogTitle>
            {{ (editing() ? 'companies.editTitle' : 'companies.createTitle') | transloco }}
          </h2>
          <p hlmDialogDescription>{{ 'companies.dialogDescription' | transloco }}</p>
        </hlm-dialog-header>
        <form class="grid gap-4" [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <div hlmField>
            <label hlmFieldLabel for="company-name">{{
              'companies.fields.name' | transloco
            }}</label>
            <input hlmInput id="company-name" formControlName="name" autocomplete="organization" />
            <pf-field-error [control]="form.controls.name" />
          </div>
          <div hlmField>
            <label hlmFieldLabel for="company-registration">
              {{ 'companies.fields.registrationNumber' | transloco }}
            </label>
            <input hlmInput id="company-registration" formControlName="registrationNumber" />
            <pf-field-error [control]="form.controls.registrationNumber" />
          </div>
          <div hlmField>
            <label hlmFieldLabel for="company-email">{{
              'companies.fields.email' | transloco
            }}</label>
            <input
              hlmInput
              id="company-email"
              type="email"
              formControlName="email"
              autocomplete="email"
            />
            <pf-field-error [control]="form.controls.email" />
          </div>
          <hlm-dialog-footer>
            <button hlmBtn type="button" variant="outline" hlmDialogClose>
              {{ 'common.cancel' | transloco }}
            </button>
            <button hlmBtn type="submit" [disabled]="store.saving()">
              @if (store.saving()) {
                <hlm-spinner class="size-4" />
              }
              {{ (editing() ? 'common.save' : 'common.create') | transloco }}
            </button>
          </hlm-dialog-footer>
        </form>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class CompanyDialogComponent {
  protected readonly store = inject(CompaniesStore);
  private readonly notifier = inject(Notifier);

  readonly open = model(false);
  readonly company = input<Company | null>(null);
  protected readonly editing = computed(() => this.company() !== null);

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [trimmedRequired, Validators.maxLength(200)]],
    registrationNumber: ['', [trimmedRequired, Validators.maxLength(80)]],
    email: ['', [trimmedRequired, Validators.email, Validators.maxLength(254)]],
  });

  constructor() {
    // Reset the form with the edited company each time the dialog opens.
    effect(() => {
      if (this.open()) {
        const company = this.company();
        untracked(() =>
          this.form.reset({
            name: company?.name ?? '',
            registrationNumber: company?.registrationNumber ?? '',
            email: company?.email ?? '',
          }),
        );
      }
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
    const body: CompanyBody = {
      name: raw.name.trim(),
      registrationNumber: raw.registrationNumber.trim(),
      email: raw.email.trim(),
    };
    const company = this.company();
    try {
      if (company) {
        await this.store.update(company.id, body);
        this.notifier.success('common.saved');
      } else {
        await this.store.create(body);
        this.notifier.success('companies.created');
      }
      this.open.set(false);
    } catch (error) {
      reportFormError(this.form, error, this.notifier);
    }
  }
}
