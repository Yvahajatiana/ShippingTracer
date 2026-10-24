import { ChangeDetectionStrategy, Component, effect, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { AuthService, Notifier, Profile, ProfileBody, trimmedRequired } from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { FormFieldComponent } from '../core/form-field.component';
import { LIMITS } from '../core/forms';
import { reportFormError } from '../core/result';
import { ProfileStore } from './profile.store';

/** Creates the profile (onboarding, `profile` null) or edits the existing one. */
@Component({
  selector: 'pf-profile-form',
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    HlmButtonImports,
    HlmSpinnerImports,
    FormFieldComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="grid gap-4" [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <div class="grid gap-4 sm:grid-cols-2">
        <pf-form-field
          fieldId="pf-profile-first-name"
          labelKey="profile.fields.firstName"
          autocomplete="given-name"
          [control]="form.controls.firstName"
        />
        <pf-form-field
          fieldId="pf-profile-last-name"
          labelKey="profile.fields.lastName"
          autocomplete="family-name"
          [control]="form.controls.lastName"
        />
        <pf-form-field
          fieldId="pf-profile-email"
          labelKey="profile.fields.email"
          type="email"
          autocomplete="email"
          [control]="form.controls.email"
        />
        <pf-form-field
          fieldId="pf-profile-phone"
          labelKey="profile.fields.phone"
          type="tel"
          autocomplete="tel"
          required="false"
          [control]="form.controls.phone"
        />
      </div>
      <div class="flex justify-end">
        <button hlmBtn type="submit" [disabled]="store.saving()">
          @if (store.saving()) {
            <hlm-spinner [aria-label]="'common.loading' | transloco" />
          }
          {{ (profile() ? 'common.save' : 'profile.onboarding.submit') | transloco }}
        </button>
      </div>
    </form>
  `,
})
export class ProfileFormComponent {
  protected readonly store = inject(ProfileStore);
  private readonly notifier = inject(Notifier);
  private readonly auth = inject(AuthService);

  /** Existing profile to edit; null when onboarding. */
  readonly profile = input<Profile | null>(null);

  protected readonly form = inject(NonNullableFormBuilder).group({
    firstName: ['', [trimmedRequired, Validators.maxLength(100)]],
    lastName: ['', [trimmedRequired, Validators.maxLength(100)]],
    email: ['', [trimmedRequired, Validators.email, Validators.maxLength(LIMITS.email)]],
    phone: ['', [Validators.maxLength(LIMITS.phone)]],
  });

  constructor() {
    effect(() => {
      const profile = this.profile();
      if (profile) {
        this.form.reset({
          firstName: profile.firstName,
          lastName: profile.lastName,
          email: profile.email,
          phone: profile.phone ?? '',
        });
        return;
      }
      // Onboarding: prefill from the identity provider claims.
      const user = this.auth.user();
      const [firstName, ...rest] = (user?.displayName ?? '').split(' ');
      this.form.reset({
        firstName: firstName ?? '',
        lastName: rest.join(' '),
        email: user?.email ?? '',
        phone: '',
      });
    });
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    const body: ProfileBody = {
      firstName: value.firstName.trim(),
      lastName: value.lastName.trim(),
      email: value.email.trim(),
      phone: value.phone.trim() || null,
    };
    const editing = this.profile() !== null;
    const result = editing ? await this.store.update(body) : await this.store.create(body);
    if (result.ok) {
      this.notifier.success(editing ? 'common.saved' : 'profile.onboarding.created');
      this.form.markAsPristine();
    } else {
      reportFormError(this.form, result.error, this.notifier);
    }
  }
}
