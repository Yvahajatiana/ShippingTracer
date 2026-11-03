// Test data builders shared by the customer portal specs. Never import from application code.
import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import {
  Invoice,
  NotificationRecord,
  Page,
  Profile,
  PublicTracking,
  Shipment,
  ShipmentAddress,
} from '@parcelflow/shared';

/** The bearer token is resolved asynchronously before a request reaches the backend. */
export const settle = () => new Promise<void>((resolve) => setTimeout(resolve));

/** Waits for the interceptor, then expects exactly one matching request. */
export async function expectRequest(
  backend: HttpTestingController,
  match: Parameters<HttpTestingController['expectOne']>[0],
): Promise<TestRequest> {
  await settle();
  return backend.expectOne(match);
}

/** Flushes an RFC 9457 problem response. */
export function flushProblem(
  request: TestRequest,
  status: number,
  errors?: Record<string, string[]>,
): void {
  request.flush(
    { title: 'Problem', status, ...(errors ? { errors } : {}) },
    { status, statusText: 'Problem' },
  );
}

export function aProfile(overrides: Partial<Profile> = {}): Profile {
  return {
    id: 'profile-1',
    keycloakSubject: 'customer-alice',
    firstName: 'Alice',
    lastName: 'Martin',
    email: 'alice@example.test',
    phone: '+33612345678',
    companyId: null,
    createdAtUtc: '2026-01-10T10:00:00Z',
    addresses: [
      {
        id: 'address-1',
        label: 'Domicile',
        line1: '12 rue de Rivoli',
        line2: null,
        postalCode: '75001',
        city: 'Paris',
        countryCode: 'FR',
      },
    ],
    ...overrides,
  };
}

export function anAddress(overrides: Partial<ShipmentAddress> = {}): ShipmentAddress {
  return {
    name: 'Alice Martin',
    email: 'alice@example.test',
    phone: '+33612345678',
    line1: '12 rue de Rivoli',
    line2: null,
    postalCode: '75001',
    city: 'Paris',
    countryCode: 'FR',
    ...overrides,
  };
}

/** Shipment as sent on the wire: Shipment emits zone and status as integers. */
export function aWireShipment(index = 0, overrides: Record<string, unknown> = {}) {
  return {
    id: `shipment-${index}`,
    trackingNumber: `PF${String(240000 + index).padStart(8, '0')}FR`,
    ownerSubject: 'customer-alice',
    sender: anAddress(),
    recipient: anAddress({ name: 'Jonas Weber', city: 'Berlin', countryCode: 'DE' }),
    parcel: { weightKg: 1.5, lengthCm: 30, widthCm: 20, heightCm: 15 },
    price: 16.1,
    currency: 'EUR',
    zone: 1,
    status: 0,
    createdAtUtc: '2026-10-20T08:00:00Z',
    ...overrides,
  };
}

export function aShipment(index = 0): Shipment {
  return {
    ...aWireShipment(index),
    zone: 'Europe',
    status: 'AwaitingPayment',
  } as Shipment;
}

export function anInvoice(overrides: Partial<Invoice> = {}): Invoice {
  return {
    id: 'invoice-1',
    number: 'INV-2026-1001',
    shipmentId: 'shipment-0',
    customerSubject: 'customer-alice',
    amount: 16.1,
    currency: 'EUR',
    issuedAtUtc: '2026-10-20T08:00:00Z',
    status: 'PendingPayment',
    creditTotal: 0,
    remainingCredit: 16.1,
    ...overrides,
  };
}

export function aTracking(overrides: Partial<PublicTracking> = {}): PublicTracking {
  return {
    trackingNumber: 'PF00240000FR',
    currentStatus: 'InTransit',
    currentStatusLabel: 'In Transit',
    updatedAtUtc: '2026-10-21T08:00:00Z',
    history: [
      {
        status: 'Created',
        statusLabel: 'Created',
        occurredAtUtc: '2026-10-20T08:00:00Z',
        facilityCode: null,
      },
      {
        status: 'InTransit',
        statusLabel: 'In Transit',
        occurredAtUtc: '2026-10-21T08:00:00Z',
        facilityCode: 'PAR-01',
      },
    ],
    ...overrides,
  };
}

export function aNotification(index = 0): NotificationRecord {
  return {
    id: `notification-${index}`,
    sourceEventId: `event-${index}`,
    shipmentId: 'shipment-0',
    customerSubject: 'customer-alice',
    trackingNumber: 'PF00240000FR',
    channel: index % 2 === 0 ? 'Email' : 'Sms',
    destination: 'alice@example.test',
    subject: `Colis PF00240000FR #${index}`,
    body: 'Body',
    occurredAtUtc: '2026-10-20T08:00:00Z',
    recordedAtUtc: '2026-10-20T08:00:00Z',
    deliveryStatus: 'Sent',
  };
}

export function aPage<T>(items: T[], page = 1, pageSize = 20, total = items.length): Page<T> {
  return { page, pageSize, total, items };
}
