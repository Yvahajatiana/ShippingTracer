import { Signal, computed } from '@angular/core';

export type Role = 'customer' | 'operator' | 'admin';

export const ROLES: readonly Role[] = ['customer', 'operator', 'admin'];

export interface AuthUser {
  subject: string;
  displayName: string;
  email?: string;
  roles: readonly Role[];
}

/**
 * Authentication port. The Keycloak adapter is used against the real gateway,
 * the mock adapter lets every portal run without an identity provider.
 */
export abstract class AuthService {
  abstract readonly ready: Signal<boolean>;
  abstract readonly user: Signal<AuthUser | null>;

  readonly authenticated = computed(() => this.user() !== null);
  readonly roles = computed<readonly Role[]>(() => this.user()?.roles ?? []);

  abstract login(redirectPath?: string): Promise<void>;
  abstract logout(): Promise<void>;
  /** Returns a fresh access token, or null when the session is gone. */
  abstract token(): Promise<string | null>;

  hasAnyRole(...roles: readonly Role[]): boolean {
    const granted = this.roles();
    return roles.some((role) => granted.includes(role));
  }
}

export function isRole(value: string): value is Role {
  return (ROLES as readonly string[]).includes(value);
}
