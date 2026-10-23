import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Translation, TranslocoLoader } from '@jsverse/transloco';
import { Observable, forkJoin, map } from 'rxjs';

type Tree = Record<string, unknown>;

function isTree(value: unknown): value is Tree {
  return !!value && typeof value === 'object' && !Array.isArray(value);
}

/** Recursively merges `override` into `base`; portal keys win over shared keys. */
export function deepMerge(base: Tree, override: Tree): Tree {
  const result: Tree = { ...base };
  for (const [key, value] of Object.entries(override)) {
    const existing = result[key];
    result[key] = isTree(existing) && isTree(value) ? deepMerge(existing, value) : value;
  }
  return result;
}

/** Loads `i18n/shared/{lang}.json` (from the shared library) merged with the portal's `i18n/{lang}.json`. */
@Injectable({ providedIn: 'root' })
export class MergedTranslationLoader implements TranslocoLoader {
  private readonly http = inject(HttpClient);

  getTranslation(lang: string): Observable<Translation> {
    return forkJoin([
      this.http.get<Tree>(`i18n/shared/${lang}.json`),
      this.http.get<Tree>(`i18n/${lang}.json`),
    ]).pipe(map(([shared, portal]) => deepMerge(shared, portal) as Translation));
  }
}
