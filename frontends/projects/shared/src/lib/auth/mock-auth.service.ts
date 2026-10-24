import { Injectable, InjectionToken, inject, signal } from '@angular/core';
import { AuthService, AuthUser, Role, isRole } from './auth.service';

const STORAGE_KEY = 'pf.mock.session';

export interface MockIdentity {
  subject: string;
  displayName: string;
  email: string;
  roles: readonly Role[];
}

/** Identities offered by the mock login screen; subjects match the MSW fixtures. */
export const MOCK_IDENTITIES: readonly MockIdentity[] = [
  {
    subject: 'customer-alice',
    displayName: 'Alice Martin',
    email: 'alice@example.test',
    roles: ['customer'],
  },
  {
    subject: 'customer-bob',
    displayName: 'Bob Durand',
    email: 'bob@example.test',
    roles: ['customer'],
  },
  {
    subject: 'operator-chloe',
    displayName: 'Chloé Petit',
    email: 'chloe@parcelflow.test',
    roles: ['operator'],
  },
  {
    subject: 'admin-david',
    displayName: 'David Leroy',
    email: 'david@parcelflow.test',
    roles: ['admin'],
  },
];

/** Identity selected automatically when a protected route is opened in mock mode. */
export const MOCK_DEFAULT_SUBJECT = new InjectionToken<string>('MOCK_DEFAULT_SUBJECT');

function readStoredSession(): AuthUser | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return null;
    }
    const parsed = JSON.parse(raw) as Partial<AuthUser>;
    if (
      typeof parsed.subject !== 'string' ||
      typeof parsed.displayName !== 'string' ||
      !Array.isArray(parsed.roles)
    ) {
      return null;
    }
    return {
      subject: parsed.subject,
      displayName: parsed.displayName,
      email: parsed.email,
      roles: parsed.roles.filter(isRole),
    };
  } catch {
    return null;
  }
}

function storeSession(user: AuthUser | null): void {
  try {
    if (user) {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(user));
    } else {
      localStorage.removeItem(STORAGE_KEY);
    }
  } catch {
    // Storage can be unavailable (private mode); the session then lives in memory only.
  }
}

@Injectable()
export class MockAuthService extends AuthService {
  private readonly defaultSubject = inject(MOCK_DEFAULT_SUBJECT, { optional: true });
  private readonly state = signal<AuthUser | null>(readStoredSession());

  readonly ready = signal(true).asReadonly();
  readonly user = this.state.asReadonly();
  readonly identities = MOCK_IDENTITIES;

  async login(): Promise<void> {
    const identity =
      MOCK_IDENTITIES.find((item) => item.subject === this.defaultSubject) ?? MOCK_IDENTITIES[0];
    this.signInAs(identity.subject);
  }

  signInAs(subject: string): void {
    const identity = MOCK_IDENTITIES.find((item) => item.subject === subject);
    if (!identity) {
      throw new Error(`Unknown mock identity: ${subject}`);
    }
    const user: AuthUser = { ...identity, roles: [...identity.roles] };
    storeSession(user);
    this.state.set(user);
  }

  async logout(): Promise<void> {
    storeSession(null);
    this.state.set(null);
  }

  async token(): Promise<string | null> {
    const user = this.state();
    // The mock API reads the identity from this opaque token; it is never sent to a real backend.
    return user ? `mock.${btoa(JSON.stringify({ sub: user.subject, roles: user.roles }))}` : null;
  }
}
