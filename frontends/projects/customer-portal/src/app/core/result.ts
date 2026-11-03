import { FormGroup } from '@angular/forms';
import { ApiError, Notifier, applyServerErrors, toApiError } from '@parcelflow/shared';
import { Observable, firstValueFrom } from 'rxjs';

/** Outcome of a mutation: forms need the error to map server-side field errors. */
export type Result<T> = { ok: true; value: T } | { ok: false; error: ApiError };

/** Runs a single-shot request and converts its failure into a typed result instead of throwing. */
export async function attempt<T>(source: Observable<T>): Promise<Result<T>> {
  try {
    return { ok: true, value: await firstValueFrom(source, { defaultValue: undefined as T }) };
  } catch (error) {
    return { ok: false, error: toApiError(error) };
  }
}

/** Maps a 400 onto form controls when possible, otherwise shows a toast. */
export function reportFormError(form: FormGroup, error: ApiError, notifier: Notifier): void {
  if (error.status === 400 && applyServerErrors(form, error)) {
    return;
  }
  notifier.error(error);
}

/** Narrows an unknown load error to an ApiError, keeping null for "no error". */
export function asApiError(error: unknown): ApiError | null {
  return error ? toApiError(error) : null;
}
