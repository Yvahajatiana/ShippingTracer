import { HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { APP_CONFIG } from '../config/app-config';
import { buildApiUrl } from '../http/api.interceptor';

@Injectable({ providedIn: 'root' })
export class ApiUrl {
  private readonly config = inject(APP_CONFIG);

  /** Joins encoded path segments under `/api/v1`. */
  to(...segments: (string | number)[]): string {
    const path =
      '/api/v1/' + segments.map((segment) => encodeURIComponent(String(segment))).join('/');
    return buildApiUrl(this.config.apiBaseUrl, path);
  }
}

/** Builds query params, skipping null and undefined values. */
export function queryParams(
  values: Record<string, string | number | boolean | null | undefined>,
): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(values)) {
    if (value !== null && value !== undefined) {
      params = params.set(key, String(value));
    }
  }
  return params;
}
