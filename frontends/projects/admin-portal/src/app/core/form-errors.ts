import { AbstractControl, FormGroup } from '@angular/forms';
import { ApiError, Notifier, applyServerErrors, toApiError } from '@parcelflow/shared';

/**
 * Standard mutation failure handling for admin forms:
 * 400 with field errors -> errors shown on the controls; anything else -> toast.
 * Returns the normalized error so callers can react to specific statuses.
 */
export function reportFormError(form: FormGroup, error: unknown, notifier: Notifier): ApiError {
  const apiError = toApiError(error);
  if (apiError.status === 400 && applyServerErrors(form, apiError)) {
    return apiError;
  }
  notifier.error(apiError);
  return apiError;
}

/** Shows a translated server-side message on a control (rendered by pf-field-error). */
export function setServerError(control: AbstractControl, message: string): void {
  control.setErrors({ ...(control.errors ?? {}), server: message });
  control.markAsTouched();
}

/** GUID check used by optional identifier filters. */
export const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
