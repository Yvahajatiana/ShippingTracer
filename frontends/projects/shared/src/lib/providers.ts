import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  EnvironmentProviders,
  Provider,
  Type,
  inject,
  isDevMode,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { provideTransloco } from '@jsverse/transloco';
import {
  AutoRefreshTokenService,
  UserActivityService,
  provideKeycloak,
  withAutoRefreshToken,
} from 'keycloak-angular';
import { AuthService } from './auth/auth.service';
import { KeycloakAuthService } from './auth/keycloak-auth.service';
import { MOCK_DEFAULT_SUBJECT, MockAuthService } from './auth/mock-auth.service';
import { APP_CONFIG, AppConfig, Portal, loadAppConfig } from './config/app-config';
import { apiInterceptor } from './http/api.interceptor';
import { LANGUAGES, LanguageService } from './i18n/language.service';
import { MergedTranslationLoader } from './i18n/translation.loader';

export interface PortalOptions {
  portal: Portal;
  /** Keycloak behaviour on load: staff portals require login, the customer portal has public pages. */
  keycloakOnLoad: 'login-required' | 'check-sso';
  /** Mock identity used when a protected page is opened in mock mode. */
  mockDefaultSubject: string;
}

/** Idle time after which a Keycloak session is closed. */
const SESSION_TIMEOUT_MS = 30 * 60 * 1000;

function authProviders(
  config: AppConfig,
  options: PortalOptions,
): (Provider | EnvironmentProviders)[] {
  if (config.authMode === 'mock') {
    return [
      { provide: MOCK_DEFAULT_SUBJECT, useValue: options.mockDefaultSubject },
      { provide: AuthService, useClass: MockAuthService },
    ];
  }
  return [
    provideKeycloak({
      config: config.keycloak,
      initOptions: { onLoad: options.keycloakOnLoad, pkceMethod: 'S256', checkLoginIframe: false },
      features: [
        withAutoRefreshToken({ onInactivityTimeout: 'logout', sessionTimeout: SESSION_TIMEOUT_MS }),
      ],
      providers: [AutoRefreshTokenService, UserActivityService],
    }),
    { provide: AuthService, useClass: KeycloakAuthService },
  ];
}

/** Common providers for every portal: HTTP, auth and i18n. */
export function provideParcelFlow(
  config: AppConfig,
  options: PortalOptions,
): (Provider | EnvironmentProviders)[] {
  return [
    { provide: APP_CONFIG, useValue: config },
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withFetch(), withInterceptors([apiInterceptor])),
    provideTransloco({
      config: {
        availableLangs: [...LANGUAGES],
        defaultLang: 'fr',
        fallbackLang: 'fr',
        reRenderOnLangChange: true,
        missingHandler: { useFallbackTranslation: true, logMissingKey: isDevMode() },
        prodMode: !isDevMode(),
      },
      loader: MergedTranslationLoader,
    }),
    provideAppInitializer(() => inject(LanguageService).init()),
    ...authProviders(config, options),
  ];
}

function renderFatalError(error: unknown): void {
  console.error(error);
  const french = !navigator.language?.toLowerCase().startsWith('en');
  const message = french
    ? "La configuration de l'application est invalide ou inaccessible."
    : 'The application configuration is invalid or unreachable.';
  const paragraph = document.createElement('p');
  paragraph.setAttribute('role', 'alert');
  paragraph.style.cssText =
    'font-family:system-ui,sans-serif;margin:3rem auto;max-width:32rem;text-align:center';
  paragraph.textContent = message;
  document.body.replaceChildren(paragraph);
}

/**
 * Loads the runtime configuration, starts the mock API when requested, then bootstraps the portal.
 * `appConfig` receives the loaded configuration to add portal-specific providers (router, …).
 */
export async function bootstrapPortal(
  root: Type<unknown>,
  options: PortalOptions,
  appConfig: (config: AppConfig) => ApplicationConfig,
): Promise<void> {
  try {
    const config = await loadAppConfig();
    if (config.mockApi) {
      const { startMockApi } = await import('./mocks/browser');
      await startMockApi();
    }
    const portal = appConfig(config);
    await bootstrapApplication(root, {
      providers: [...provideParcelFlow(config, options), ...portal.providers],
    });
  } catch (error) {
    renderFatalError(error);
  }
}
