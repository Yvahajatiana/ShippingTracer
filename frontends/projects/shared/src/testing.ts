/*
 * Test helpers of @parcelflow/shared (`@parcelflow/shared/testing`). Never import from application code.
 */
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { EnvironmentProviders, Provider, importProvidersFrom } from '@angular/core';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { AuthService, Role } from './lib/auth/auth.service';
import {
  MOCK_DEFAULT_SUBJECT,
  MOCK_IDENTITIES,
  MockAuthService,
} from './lib/auth/mock-auth.service';
import { APP_CONFIG, AppConfig } from './lib/config/app-config';
import { apiInterceptor } from './lib/http/api.interceptor';

export const TEST_CONFIG: AppConfig = {
  apiBaseUrl: '',
  authMode: 'mock',
  mockApi: false,
  keycloak: { url: '', realm: '', clientId: '' },
};

export interface TestingOptions {
  /** Mock identity signed in before the test; null keeps the visitor anonymous. */
  subject?: string | null;
  /** Translations used by the test; keys are returned as-is when omitted. */
  translations?: Record<string, unknown>;
}

/**
 * Providers for portal unit tests: mock auth, HttpClient with the real interceptor plus
 * HttpTestingController and an in-memory French Transloco setup.
 */
export function provideParcelFlowTesting(
  options: TestingOptions = {},
): (Provider | EnvironmentProviders)[] {
  const subject = options.subject === undefined ? MOCK_IDENTITIES[0].subject : options.subject;
  try {
    localStorage.removeItem('pf.mock.session');
  } catch {
    // Ignore unavailable storage in the test environment.
  }
  return [
    { provide: APP_CONFIG, useValue: TEST_CONFIG },
    { provide: MOCK_DEFAULT_SUBJECT, useValue: subject ?? MOCK_IDENTITIES[0].subject },
    {
      provide: AuthService,
      useFactory: () => {
        const auth = new MockAuthService();
        if (subject) {
          auth.signInAs(subject);
        }
        return auth;
      },
    },
    provideHttpClient(withInterceptors([apiInterceptor])),
    provideHttpClientTesting(),
    importProvidersFrom(
      TranslocoTestingModule.forRoot({
        langs: { fr: options.translations ?? {}, en: {} },
        translocoConfig: {
          availableLangs: ['fr', 'en'],
          defaultLang: 'fr',
          missingHandler: { logMissingKey: false },
        },
        preloadLangs: true,
      }),
    ),
  ];
}

/** Mock identity subject per role, for tests that need a specific role. */
export function subjectFor(role: Role): string {
  return MOCK_IDENTITIES.find((identity) => identity.roles.includes(role))!.subject;
}
