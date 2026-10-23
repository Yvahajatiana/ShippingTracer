import { ChangeDetectionStrategy, Component, computed, effect, input, signal } from '@angular/core';
import {
  AbstractControl,
  FormGroup,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { ApiError, toApiError } from '../http/api-error';

/** ISO 3166-1 alpha-2, as validated by the services. */
export const countryCodeValidator: ValidatorFn = (control) => {
  const value = control.value as string | null;
  if (!value) {
    return null;
  }
  return /^[A-Z]{2}$/.test(value) ? null : { countryCode: true };
};

export const trimmedRequired: ValidatorFn = (control) =>
  typeof control.value === 'string' && control.value.trim() === ''
    ? { required: true }
    : Validators.required(control);

/**
 * Copies server-side field errors (ProblemDetails `errors`) onto matching form controls.
 * Returns true when at least one field error was applied.
 */
export function applyServerErrors(form: FormGroup, error: unknown): boolean {
  const apiError: ApiError = toApiError(error);
  let applied = false;
  for (const [path, messages] of Object.entries(apiError.fieldErrors)) {
    const control = form.get(
      path
        .split('.')
        .map((part) => part.charAt(0).toLowerCase() + part.slice(1))
        .join('.'),
    );
    if (control && messages.length > 0) {
      control.setErrors({ ...(control.errors ?? {}), server: messages[0] });
      control.markAsTouched();
      applied = true;
    }
  }
  return applied;
}

interface DisplayedError {
  key: string;
  params: Record<string, unknown>;
  raw?: string;
}

function firstError(errors: ValidationErrors | null): DisplayedError | null {
  if (!errors) {
    return null;
  }
  const [key, value] = Object.entries(errors)[0] ?? [];
  if (!key) {
    return null;
  }
  if (key === 'server') {
    return { key, params: {}, raw: String(value) };
  }
  const known = [
    'required',
    'email',
    'minlength',
    'maxlength',
    'min',
    'max',
    'pattern',
    'countryCode',
  ];
  const params = value && typeof value === 'object' ? (value as Record<string, unknown>) : {};
  return { key: known.includes(key) ? `validation.${key}` : 'validation.invalid', params };
}

/** Displays the first translated validation error of a touched control. */
@Component({
  selector: 'pf-field-error',
  imports: [TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (error(); as current) {
      <small class="text-sm text-destructive" role="alert">
        {{ current.raw ?? (current.key | transloco: current.params) }}
      </small>
    }
  `,
})
export class FieldErrorComponent {
  readonly control = input.required<AbstractControl>();
  // Bumped on every control event (value, status, touched) so the computed re-evaluates.
  private readonly tick = signal(0);

  constructor() {
    effect((onCleanup) => {
      const subscription = this.control().events.subscribe(() =>
        this.tick.update((value) => value + 1),
      );
      onCleanup(() => subscription.unsubscribe());
    });
  }

  readonly error = computed(() => {
    this.tick();
    const control = this.control();
    return control.touched || control.dirty ? firstError(control.errors) : null;
  });
}
