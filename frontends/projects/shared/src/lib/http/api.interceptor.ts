import { HttpContextToken, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { APP_CONFIG } from '../config/app-config';
import { ApiError, toApiError } from './api-error';

/** Marks a request as public: no bearer token is attached (e.g. public tracking). */
export const ANONYMOUS = new HttpContextToken<boolean>(() => false);

/** Builds an absolute or same-origin URL for a gateway path, rejecting anything that could leave the API origin. */
export function buildApiUrl(apiBaseUrl: string, path: string): string {
  if (
    !path.startsWith('/api/') ||
    path.includes('\\') ||
    /[\r\n]/.test(path) ||
    path.includes('..')
  ) {
    throw new Error(`Invalid API path: ${path}`);
  }
  return apiBaseUrl + path;
}

function isApiRequest(url: string, apiBaseUrl: string): boolean {
  if (apiBaseUrl) {
    return url.startsWith(apiBaseUrl + '/api/');
  }
  return url.startsWith('/api/');
}

/**
 * Attaches the bearer token to gateway calls and normalizes failures into ApiError.
 * Requests to any other origin are left untouched so tokens never leak.
 */
export const apiInterceptor: HttpInterceptorFn = (request, next) => {
  const config = inject(APP_CONFIG);
  const auth = inject(AuthService);
  if (!isApiRequest(request.url, config.apiBaseUrl)) {
    return next(request);
  }
  const anonymous = request.context.get(ANONYMOUS);
  const prepared$ = anonymous
    ? from(Promise.resolve(request))
    : from(auth.token()).pipe(
        switchMap(async (token) => {
          if (!token) {
            throw new ApiError(401, 'errors.http.401');
          }
          return request.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
        }),
      );
  return prepared$.pipe(
    switchMap((prepared) => next(prepared.clone({ withCredentials: false }))),
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse || error instanceof ApiError) {
        return throwError(() => toApiError(error));
      }
      return throwError(() => error);
    }),
  );
};
