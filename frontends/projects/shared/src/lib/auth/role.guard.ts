import { inject } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { CanActivateFn, CanMatchFn, Router } from '@angular/router';
import { filter, firstValueFrom } from 'rxjs';
import { AuthService, Role } from './auth.service';

async function checkAccess(roles: readonly Role[], targetUrl: string) {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.ready()) {
    await firstValueFrom(toObservable(auth.ready).pipe(filter(Boolean)));
  }
  if (!auth.authenticated()) {
    await auth.login(targetUrl);
    // Keycloak navigates away; the mock adapter signs in synchronously.
    if (!auth.authenticated()) {
      return false;
    }
  }
  if (roles.length === 0 || auth.hasAnyRole(...roles)) {
    return true;
  }
  return router.createUrlTree(['/forbidden']);
}

/** Requires an authenticated user holding at least one of the given roles. */
export function roleGuard(...roles: Role[]): CanActivateFn {
  return (_route, state) => checkAccess(roles, state.url);
}

/** Same as roleGuard, for lazy route matching. */
export function roleMatchGuard(...roles: Role[]): CanMatchFn {
  return (_route, segments) =>
    checkAccess(roles, '/' + segments.map((segment) => segment.path).join('/'));
}
