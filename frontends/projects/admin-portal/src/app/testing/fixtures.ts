// Test-only data builders and helpers (excluded from the application build by tsconfig.app.json).
import {
  Company,
  CreditNote,
  Driver,
  Invoice,
  NotificationRecord,
  Profile,
  Warehouse,
} from '@parcelflow/shared';

/** The bearer token is resolved asynchronously before a request reaches HttpTestingController. */
export const settle = () => new Promise<void>((resolve) => setTimeout(resolve));

export function page<T>(items: T[], total = items.length, pageNumber = 1, pageSize = 20) {
  return { page: pageNumber, pageSize, total, items };
}

export function profile(index: number, overrides: Partial<Profile> = {}): Profile {
  return {
    id: `p${index}`,
    keycloakSubject: `customer-${index}`,
    firstName: 'First',
    lastName: `Last${index}`,
    email: `c${index}@test.dev`,
    phone: null,
    companyId: null,
    createdAtUtc: '2026-01-01T10:00:00Z',
    addresses: [],
    ...overrides,
  };
}

export const COMPANIES: Company[] = [
  { id: 'c2', name: 'Zeta', registrationNumber: 'R2', email: 'z@z.test' },
  { id: 'c1', name: 'Acme', registrationNumber: 'R1', email: 'a@a.test' },
];

export function warehouse(code: string, overrides: Partial<Warehouse> = {}): Warehouse {
  return {
    id: `w-${code}`,
    code,
    name: `Warehouse ${code}`,
    kind: 'Warehouse',
    street: '1 rue du Dépôt',
    city: 'Paris',
    postalCode: '75010',
    countryCode: 'FR',
    isActive: true,
    ...overrides,
  };
}

export function driver(subject: string, overrides: Partial<Driver> = {}): Driver {
  return { subject, displayName: `Driver ${subject}`, isActive: true, ...overrides };
}

export function invoice(id: string, overrides: Partial<Invoice> = {}): Invoice {
  return {
    id,
    number: `INV-${id}`,
    shipmentId: `s-${id}`,
    customerSubject: 'customer-alice',
    amount: 20,
    currency: 'EUR',
    issuedAtUtc: '2026-10-01T10:00:00Z',
    status: 'Paid',
    creditTotal: 0,
    remainingCredit: 20,
    ...overrides,
  };
}

export function creditNote(id: string, overrides: Partial<CreditNote> = {}): CreditNote {
  return {
    id,
    number: `CN-${id}`,
    invoiceId: 'i1',
    amount: 5,
    currency: 'EUR',
    reason: 'ServiceIssue',
    occurredAtUtc: '2026-10-02T10:00:00Z',
    ...overrides,
  };
}

export function notification(id: string, overrides: Partial<NotificationRecord> = {}) {
  const record: NotificationRecord = {
    id,
    sourceEventId: `e-${id}`,
    shipmentId: '11111111-2222-3333-4444-555555555555',
    customerSubject: 'customer-alice',
    trackingNumber: 'PF000000001FR',
    channel: 'Email',
    destination: 'alice@test.dev',
    subject: `Subject ${id}`,
    body: 'Your parcel is on its way.',
    occurredAtUtc: '2026-10-01T10:00:00Z',
    recordedAtUtc: '2026-10-01T10:00:01Z',
    deliveryStatus: 'Sent',
    ...overrides,
  };
  return record;
}

/** Sets an input value inside the document (dialogs render in the CDK overlay container). */
export function typeInto(id: string, value: string): void {
  const element = document.getElementById(id) as HTMLInputElement | null;
  if (!element) {
    throw new Error(`#${id} is not rendered`);
  }
  element.value = value;
  element.dispatchEvent(new Event('input'));
}

export function selectOption(id: string, value: string): void {
  const element = document.getElementById(id) as HTMLSelectElement | null;
  if (!element) {
    throw new Error(`#${id} is not rendered`);
  }
  element.value = value;
  element.dispatchEvent(new Event('change'));
}

/** Clicks the submit button of the last rendered dialog form. */
export function submitDialog(): void {
  const buttons = document.querySelectorAll<HTMLButtonElement>('button[type="submit"]');
  const button = buttons.item(buttons.length - 1);
  if (!button) {
    throw new Error('No submit button rendered');
  }
  button.click();
}
