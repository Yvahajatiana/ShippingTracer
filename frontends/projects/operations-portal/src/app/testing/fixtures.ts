/*
 * Test fixtures for the operations portal specs. Never import from application code.
 */
import {
  DeliveryRoute,
  DeliveryStop,
  Driver,
  KpiReport,
  Page,
  Shipment,
  Warehouse,
} from '@parcelflow/shared';

/** Waits for the bearer token, which is attached asynchronously by the interceptor. */
export const settle = () => new Promise<void>((resolve) => setTimeout(resolve));

export function page<T>(items: T[], pageNumber = 1, pageSize = 50, total = items.length): Page<T> {
  return { page: pageNumber, pageSize, total, items };
}

export function kpiReport(overrides: Partial<KpiReport> = {}): KpiReport {
  return {
    from: '2026-09-01',
    to: '2026-09-30',
    generatedAtUtc: '2026-09-30T10:00:00Z',
    totalShipments: 4,
    deliveredShipments: 2,
    deliveryRatePercent: 50,
    averageDeliveryHours: 30.5,
    deliveryDurationSamples: 2,
    averageTransitHours: 12,
    transitDurationSamples: 4,
    dailyVolumes: [
      { date: '2026-09-02', shipments: 3 },
      { date: '2026-09-03', shipments: 1 },
    ],
    statusVolumes: [
      { status: 'Delivered', shipments: 2 },
      { status: 'InTransit', shipments: 2 },
    ],
    ...overrides,
  };
}

export function warehouse(overrides: Partial<Warehouse> = {}): Warehouse {
  return {
    id: 'wh-1',
    code: 'PAR-01',
    name: 'Paris Nord',
    kind: 'Warehouse',
    street: '8 avenue de la Gare',
    city: 'Gonesse',
    postalCode: '95500',
    countryCode: 'FR',
    isActive: true,
    ...overrides,
  };
}

export function stop(overrides: Partial<DeliveryStop> = {}): DeliveryStop {
  return {
    shipmentId: 'sh-1',
    sequence: 1,
    status: 'Pending',
    failureReason: null,
    completedAtUtc: null,
    ...overrides,
  };
}

export function route(overrides: Partial<DeliveryRoute> = {}): DeliveryRoute {
  return {
    id: 'route-1',
    name: 'Paris Centre',
    serviceDate: '2026-10-23',
    driverSubject: null,
    status: 'Planned',
    startedAtUtc: null,
    completedAtUtc: null,
    stops: [],
    ...overrides,
  };
}

export const DRIVERS: Driver[] = [
  { subject: 'driver-emma', displayName: 'Emma Roux', isActive: true },
  { subject: 'driver-gael', displayName: 'Gaël Morel', isActive: false },
];

export function shipment(id: string, trackingNumber: string): Shipment {
  const address = {
    name: 'Bob Durand',
    email: 'bob@example.test',
    phone: '+33600000000',
    line1: '5 place Bellecour',
    line2: null,
    postalCode: '69002',
    city: 'Lyon',
    countryCode: 'FR',
  };
  return {
    id,
    trackingNumber,
    ownerSubject: 'customer-bob',
    sender: { ...address, name: 'Alice Martin' },
    recipient: address,
    parcel: { weightKg: 1, lengthCm: 10, widthCm: 10, heightCm: 10 },
    price: 9.5,
    currency: 'EUR',
    zone: 'Domestic',
    status: 'ReadyForPickup',
    createdAtUtc: '2026-10-20T10:00:00Z',
  };
}
