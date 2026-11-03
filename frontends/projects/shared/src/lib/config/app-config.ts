import { InjectionToken } from '@angular/core';

export type Portal = 'customer' | 'operations' | 'admin';
export type AuthMode = 'keycloak' | 'mock';

export interface KeycloakSettings {
  url: string;
  realm: string;
  clientId: string;
}

export interface AppConfig {
  /** Gateway origin; empty string means same origin (dev proxy or ingress). */
  apiBaseUrl: string;
  authMode: AuthMode;
  /** Starts the in-browser MSW mock API before bootstrap. */
  mockApi: boolean;
  keycloak: KeycloakSettings;
}

export const APP_CONFIG = new InjectionToken<AppConfig>('APP_CONFIG');

const LOOPBACK_HOSTS = new Set(['localhost', '127.0.0.1', '[::1]']);

/**
 * Validates an absolute origin: no credentials, http(s) only, and plain http only on loopback.
 * Returns the normalized value without trailing slash.
 */
export function safeOrigin(value: string): string {
  const url = new URL(value);
  const allowedProtocol =
    url.protocol === 'https:' || (url.protocol === 'http:' && LOOPBACK_HOSTS.has(url.hostname));
  if (url.username || url.password || !allowedProtocol) {
    throw new Error(`Invalid origin in configuration: ${value}`);
  }
  return url.href.replace(/\/$/, '');
}

export function parseAppConfig(raw: unknown): AppConfig {
  if (!raw || typeof raw !== 'object') {
    throw new Error('Invalid configuration document.');
  }
  const source = raw as Partial<AppConfig> & { keycloak?: Partial<KeycloakSettings> };
  const authMode: AuthMode = source.authMode === 'mock' ? 'mock' : 'keycloak';
  const apiBaseUrl = source.apiBaseUrl ? safeOrigin(source.apiBaseUrl) : '';
  const keycloak: Partial<KeycloakSettings> = source.keycloak ?? {};
  if (authMode === 'keycloak' && (!keycloak.url || !keycloak.realm || !keycloak.clientId)) {
    throw new Error('Keycloak configuration is incomplete.');
  }
  return {
    apiBaseUrl,
    authMode,
    mockApi: source.mockApi === true,
    keycloak: {
      url: keycloak.url ? safeOrigin(keycloak.url) : '',
      realm: keycloak.realm ?? '',
      clientId: keycloak.clientId ?? '',
    },
  };
}

/** Loads `/config.json` before bootstrap so one build can be deployed to any environment. */
export async function loadAppConfig(path = 'config.json'): Promise<AppConfig> {
  const response = await fetch(path, { cache: 'no-store', credentials: 'omit' });
  if (!response.ok) {
    throw new Error(`Unable to load ${path} (${response.status}).`);
  }
  return parseAppConfig(await response.json());
}
