import { HttpErrorResponse } from '@angular/common/http';

/** Error raised by every API call; `messageKey` is a translation key for user-facing text. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly messageKey: string,
    readonly detail?: string,
    readonly fieldErrors: Readonly<Record<string, readonly string[]>> = {},
    readonly traceId?: string,
    readonly retryAfterSeconds?: number,
  ) {
    super(detail ?? messageKey);
    this.name = 'ApiError';
  }
}

const KNOWN_STATUSES = new Set([0, 400, 401, 403, 404, 409, 413, 422, 429]);

function errorKey(status: number): string {
  if (KNOWN_STATUSES.has(status)) {
    return `errors.http.${status}`;
  }
  return status >= 500 ? 'errors.http.5xx' : 'errors.http.unknown';
}

function readFieldErrors(value: unknown): Record<string, string[]> {
  if (!value || typeof value !== 'object') {
    return {};
  }
  const result: Record<string, string[]> = {};
  for (const [field, messages] of Object.entries(value as Record<string, unknown>)) {
    if (Array.isArray(messages)) {
      // Server keys are PascalCase property paths; forms use camelCase.
      const key = field.charAt(0).toLowerCase() + field.slice(1);
      result[key] = messages.filter((message): message is string => typeof message === 'string');
    }
  }
  return result;
}

/** Converts an HttpClient failure carrying RFC 9457 ProblemDetails into an ApiError. */
export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) {
    return error;
  }
  if (!(error instanceof HttpErrorResponse)) {
    return new ApiError(0, 'errors.http.unknown');
  }
  const problem =
    error.error && typeof error.error === 'object' ? (error.error as Record<string, unknown>) : {};
  const detail = typeof problem['detail'] === 'string' ? problem['detail'] : undefined;
  const traceId = typeof problem['traceId'] === 'string' ? problem['traceId'] : undefined;
  const retryAfter = Number(error.headers?.get('Retry-After'));
  return new ApiError(
    error.status,
    errorKey(error.status),
    detail,
    readFieldErrors(problem['errors']),
    traceId,
    Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : undefined,
  );
}
