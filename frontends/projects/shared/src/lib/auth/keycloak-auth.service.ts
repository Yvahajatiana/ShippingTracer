import { Injectable, computed, effect, inject, signal } from '@angular/core';
import Keycloak from 'keycloak-js';
import { KEYCLOAK_EVENT_SIGNAL, KeycloakEventType } from 'keycloak-angular';
import { AuthService, AuthUser, isRole } from './auth.service';

/** Minimum remaining validity, in seconds, before a token is refreshed. */
const MIN_TOKEN_VALIDITY = 30;

@Injectable()
export class KeycloakAuthService extends AuthService {
  private readonly keycloak = inject(Keycloak);
  private readonly events = inject(KEYCLOAK_EVENT_SIGNAL);
  // Bumped on every Keycloak event so derived signals re-read the adapter state.
  private readonly revision = signal(0);

  readonly ready = computed(() => {
    this.revision();
    return this.keycloak.didInitialize;
  });

  readonly user = computed<AuthUser | null>(() => {
    this.revision();
    const parsed = this.keycloak.tokenParsed;
    if (!this.keycloak.authenticated || !parsed?.sub) {
      return null;
    }
    const roles = (parsed.realm_access?.roles ?? []).filter(isRole);
    const claims = parsed as Record<string, unknown>;
    const name = typeof claims['name'] === 'string' ? claims['name'] : undefined;
    const username =
      typeof claims['preferred_username'] === 'string' ? claims['preferred_username'] : undefined;
    const email = typeof claims['email'] === 'string' ? claims['email'] : undefined;
    return { subject: parsed.sub, displayName: name ?? username ?? parsed.sub, email, roles };
  });

  constructor() {
    super();
    effect(() => {
      // Refresh failures leave stale tokens behind; drop them so guards send users back to login.
      if (this.events().type === KeycloakEventType.AuthRefreshError) {
        this.keycloak.clearToken();
      }
      this.revision.update((value) => value + 1);
    });
  }

  async login(redirectPath?: string): Promise<void> {
    const redirectUri = redirectPath
      ? new URL(redirectPath, window.location.origin).href
      : window.location.href;
    await this.keycloak.login({ redirectUri });
  }

  async logout(): Promise<void> {
    await this.keycloak.logout({ redirectUri: window.location.origin + '/' });
  }

  async token(): Promise<string | null> {
    if (!this.keycloak.authenticated) {
      return null;
    }
    try {
      await this.keycloak.updateToken(MIN_TOKEN_VALIDITY);
    } catch {
      this.keycloak.clearToken();
      this.revision.update((value) => value + 1);
      return null;
    }
    return this.keycloak.token ?? null;
  }
}
