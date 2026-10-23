// In-memory data set behind the mock API. Subjects match MOCK_IDENTITIES.
import {
  Company,
  CreditNote,
  DeliveryAttempt,
  DeliveryRoute,
  Driver,
  Invoice,
  NotificationRecord,
  ParcelStatus,
  Payment,
  Profile,
  Shipment,
  Tariff,
  Warehouse,
} from '../api/models';

export interface TrackingEvent {
  eventId: string;
  status: ParcelStatus;
  occurredAtUtc: string;
  facilityCode: string | null;
}

export interface StockEntry {
  warehouseId: string;
  shipmentId: string;
  trackingNumber: string;
  lastScanAtUtc: string;
}

export interface ProofImages {
  signature?: Blob;
  photo?: Blob;
}

export interface MockDb {
  profiles: Profile[];
  companies: Company[];
  tariffs: Tariff[];
  shipments: Shipment[];
  tracking: Map<string, TrackingEvent[]>;
  warehouses: Warehouse[];
  stock: StockEntry[];
  drivers: Driver[];
  routes: DeliveryRoute[];
  attempts: DeliveryAttempt[];
  proofImages: Map<string, ProofImages>;
  invoices: Invoice[];
  payments: Payment[];
  creditNotes: CreditNote[];
  notifications: NotificationRecord[];
}

export const id = () => crypto.randomUUID();

function daysAgo(days: number, hours = 0): string {
  return new Date(Date.now() - days * 86_400_000 - hours * 3_600_000).toISOString();
}

export function today(offsetDays = 0): string {
  return new Date(Date.now() + offsetDays * 86_400_000).toISOString().slice(0, 10);
}

const PARIS = {
  line1: '12 rue de Rivoli',
  line2: null as string | null,
  postalCode: '75001',
  city: 'Paris',
  countryCode: 'FR',
};
const LYON = {
  line1: '5 place Bellecour',
  line2: null,
  postalCode: '69002',
  city: 'Lyon',
  countryCode: 'FR',
};
const BERLIN = {
  line1: 'Unter den Linden 10',
  line2: null,
  postalCode: '10117',
  city: 'Berlin',
  countryCode: 'DE',
};
const MONTREAL = {
  line1: '1000 rue Sherbrooke',
  line2: 'Bureau 200',
  postalCode: 'H3A 1G1',
  city: 'Montréal',
  countryCode: 'CA',
};

const STATUS_FLOW: ParcelStatus[] = [
  'Created',
  'PickedUp',
  'InTransit',
  'AtSortingCenter',
  'OutForDelivery',
  'Delivered',
];

function history(final: ParcelStatus, startDaysAgo: number): TrackingEvent[] {
  const steps =
    final === 'Failed'
      ? [...STATUS_FLOW.slice(0, 5), 'Failed' as ParcelStatus]
      : STATUS_FLOW.slice(0, STATUS_FLOW.indexOf(final) + 1);
  return steps.map((status, index) => ({
    eventId: id(),
    status,
    occurredAtUtc: daysAgo(startDaysAgo - index * 0.5),
    facilityCode:
      status === 'AtSortingCenter' ? 'LYS-SC' : status === 'InTransit' ? 'PAR-01' : null,
  }));
}

export function createDb(): MockDb {
  const companies: Company[] = [
    {
      id: id(),
      name: 'Atelier Durand',
      registrationNumber: '81234567800011',
      email: 'contact@atelier-durand.test',
    },
    {
      id: id(),
      name: 'Nordic Supplies',
      registrationNumber: '90876543200025',
      email: 'hello@nordic-supplies.test',
    },
  ];
  const profiles: Profile[] = [
    {
      id: id(),
      keycloakSubject: 'customer-alice',
      firstName: 'Alice',
      lastName: 'Martin',
      email: 'alice@example.test',
      phone: '+33612345678',
      companyId: null,
      createdAtUtc: daysAgo(60),
      addresses: [
        { id: id(), label: 'Domicile', ...PARIS },
        { id: id(), label: 'Bureau', ...LYON },
      ],
    },
    {
      id: id(),
      keycloakSubject: 'customer-bob',
      firstName: 'Bob',
      lastName: 'Durand',
      email: 'bob@example.test',
      phone: null,
      companyId: companies[0].id,
      createdAtUtc: daysAgo(30),
      addresses: [{ id: id(), label: 'Atelier', ...LYON }],
    },
  ];
  const tariffs: Tariff[] = [
    { zone: 'Domestic', basePrice: 6.9, pricePerKg: 1.2, currency: 'EUR' },
    { zone: 'Europe', basePrice: 12.5, pricePerKg: 2.4, currency: 'EUR' },
    { zone: 'International', basePrice: 24, pricePerKg: 5.5, currency: 'EUR' },
  ];

  const alice = {
    name: 'Alice Martin',
    email: 'alice@example.test',
    phone: '+33612345678',
    ...PARIS,
  };
  const bob = { name: 'Bob Durand', email: 'bob@example.test', phone: '+33698765432', ...LYON };
  const recipientBerlin = {
    name: 'Jonas Weber',
    email: 'jonas@example.test',
    phone: '+4930123456',
    ...BERLIN,
  };
  const recipientMontreal = {
    name: 'Léa Tremblay',
    email: 'lea@example.test',
    phone: '+15145550199',
    ...MONTREAL,
  };

  const seeds: {
    owner: string;
    sender: typeof alice;
    recipient: typeof alice;
    zone: Shipment['zone'];
    status: Shipment['status'];
    parcel: ParcelStatus;
    days: number;
    weight: number;
  }[] = [
    {
      owner: 'customer-alice',
      sender: alice,
      recipient: bob,
      zone: 'Domestic',
      status: 'PickedUp',
      parcel: 'Delivered',
      days: 12,
      weight: 2.4,
    },
    {
      owner: 'customer-alice',
      sender: alice,
      recipient: recipientBerlin,
      zone: 'Europe',
      status: 'PickedUp',
      parcel: 'OutForDelivery',
      days: 4,
      weight: 1.1,
    },
    {
      owner: 'customer-alice',
      sender: alice,
      recipient: recipientMontreal,
      zone: 'International',
      status: 'AwaitingPayment',
      parcel: 'Created',
      days: 1,
      weight: 3.5,
    },
    {
      owner: 'customer-alice',
      sender: alice,
      recipient: bob,
      zone: 'Domestic',
      status: 'ReadyForPickup',
      parcel: 'Created',
      days: 2,
      weight: 0.8,
    },
    {
      owner: 'customer-bob',
      sender: bob,
      recipient: alice,
      zone: 'Domestic',
      status: 'PickedUp',
      parcel: 'AtSortingCenter',
      days: 3,
      weight: 5,
    },
    {
      owner: 'customer-bob',
      sender: bob,
      recipient: recipientBerlin,
      zone: 'Europe',
      status: 'PickedUp',
      parcel: 'Failed',
      days: 8,
      weight: 2,
    },
    {
      owner: 'customer-bob',
      sender: bob,
      recipient: alice,
      zone: 'Domestic',
      status: 'PaymentFailed',
      parcel: 'Created',
      days: 5,
      weight: 1.5,
    },
  ];
  const shipments: Shipment[] = [];
  const tracking = new Map<string, TrackingEvent[]>();
  const invoices: Invoice[] = [];
  const payments: Payment[] = [];
  const notifications: NotificationRecord[] = [];
  seeds.forEach((seed, index) => {
    const tariff = tariffs.find((item) => item.zone === seed.zone)!;
    const price = Math.round((tariff.basePrice + tariff.pricePerKg * seed.weight) * 100) / 100;
    const shipment: Shipment = {
      id: id(),
      trackingNumber: `PF${(240000 + index * 137).toString().padStart(8, '0')}FR`,
      ownerSubject: seed.owner,
      sender: { ...seed.sender },
      recipient: { ...seed.recipient },
      parcel: { weightKg: seed.weight, lengthCm: 30, widthCm: 20, heightCm: 15 },
      price,
      currency: 'EUR',
      zone: seed.zone,
      status: seed.status,
      createdAtUtc: daysAgo(seed.days),
    };
    shipments.push(shipment);
    tracking.set(shipment.id, history(seed.parcel, seed.days));
    const invoice: Invoice = {
      id: id(),
      number: `INV-2026-${(1001 + index).toString()}`,
      shipmentId: shipment.id,
      customerSubject: seed.owner,
      amount: price,
      currency: 'EUR',
      issuedAtUtc: shipment.createdAtUtc,
      status:
        seed.status === 'AwaitingPayment'
          ? 'PendingPayment'
          : seed.status === 'PaymentFailed'
            ? 'PaymentFailed'
            : 'Paid',
      creditTotal: 0,
      remainingCredit: price,
    };
    invoices.push(invoice);
    if (invoice.status !== 'PendingPayment') {
      payments.push({
        operationId: id(),
        invoiceId: invoice.id,
        outcome: invoice.status === 'Paid' ? 'Approved' : 'Declined',
        amount: price,
        currency: 'EUR',
        occurredAtUtc: shipment.createdAtUtc,
      });
    }
    const profile = profiles.find((item) => item.keycloakSubject === seed.owner)!;
    notifications.push({
      id: id(),
      sourceEventId: id(),
      shipmentId: shipment.id,
      customerSubject: seed.owner,
      trackingNumber: shipment.trackingNumber,
      channel: index % 2 === 0 ? 'Email' : 'Sms',
      destination: index % 2 === 0 ? profile.email : (profile.phone ?? '+33600000000'),
      subject: `Colis ${shipment.trackingNumber}`,
      body: `Votre colis ${shipment.trackingNumber} a été enregistré.`,
      occurredAtUtc: shipment.createdAtUtc,
      recordedAtUtc: shipment.createdAtUtc,
      deliveryStatus: 'Sent',
    });
  });

  const warehouses: Warehouse[] = [
    {
      id: id(),
      code: 'PAR-01',
      name: 'Paris Nord',
      kind: 'Warehouse',
      street: '8 avenue de la Gare',
      city: 'Gonesse',
      postalCode: '95500',
      countryCode: 'FR',
      isActive: true,
    },
    {
      id: id(),
      code: 'LYS-SC',
      name: 'Lyon Saint-Exupéry',
      kind: 'SortingCenter',
      street: 'Zone de fret',
      city: 'Colombier-Saugnieu',
      postalCode: '69124',
      countryCode: 'FR',
      isActive: true,
    },
    {
      id: id(),
      code: 'MRS-01',
      name: 'Marseille Port',
      kind: 'Warehouse',
      street: '1 quai du Lazaret',
      city: 'Marseille',
      postalCode: '13002',
      countryCode: 'FR',
      isActive: false,
    },
  ];
  const stock: StockEntry[] = [
    {
      warehouseId: warehouses[1].id,
      shipmentId: shipments[4].id,
      trackingNumber: shipments[4].trackingNumber,
      lastScanAtUtc: daysAgo(1),
    },
    {
      warehouseId: warehouses[0].id,
      shipmentId: shipments[3].id,
      trackingNumber: shipments[3].trackingNumber,
      lastScanAtUtc: daysAgo(0, 5),
    },
  ];

  const drivers: Driver[] = [
    { subject: 'driver-emma', displayName: 'Emma Roux', isActive: true },
    { subject: 'driver-farid', displayName: 'Farid Benali', isActive: true },
    { subject: 'driver-gael', displayName: 'Gaël Morel', isActive: false },
  ];
  const routes: DeliveryRoute[] = [
    {
      id: id(),
      name: 'Paris Centre — matin',
      serviceDate: today(),
      driverSubject: 'driver-emma',
      status: 'InProgress',
      startedAtUtc: daysAgo(0, 3),
      completedAtUtc: null,
      stops: [
        {
          shipmentId: shipments[1].id,
          sequence: 1,
          status: 'Pending',
          failureReason: null,
          completedAtUtc: null,
        },
      ],
    },
    {
      id: id(),
      name: 'Lyon Est — après-midi',
      serviceDate: today(1),
      driverSubject: null,
      status: 'Planned',
      startedAtUtc: null,
      completedAtUtc: null,
      stops: [],
    },
    {
      id: id(),
      name: 'Berlin — export',
      serviceDate: today(-7),
      driverSubject: 'driver-farid',
      status: 'Completed',
      startedAtUtc: daysAgo(7, 4),
      completedAtUtc: daysAgo(7),
      stops: [
        {
          shipmentId: shipments[0].id,
          sequence: 1,
          status: 'Delivered',
          failureReason: null,
          completedAtUtc: daysAgo(7, 1),
        },
        {
          shipmentId: shipments[5].id,
          sequence: 2,
          status: 'Failed',
          failureReason: 'RecipientAbsent',
          completedAtUtc: daysAgo(7),
        },
      ],
    },
  ];
  const attempts: DeliveryAttempt[] = [
    {
      operationId: id(),
      routeId: routes[2].id,
      shipmentId: shipments[0].id,
      outcome: 'Delivered',
      occurredAtUtc: daysAgo(7, 1),
      actorSubject: 'driver-farid',
      recipientName: 'Bob Durand',
      failureReason: null,
      hasSignature: false,
      hasPhoto: false,
    },
    {
      operationId: id(),
      routeId: routes[2].id,
      shipmentId: shipments[5].id,
      outcome: 'Failed',
      occurredAtUtc: daysAgo(7),
      actorSubject: 'driver-farid',
      recipientName: null,
      failureReason: 'RecipientAbsent',
      hasSignature: false,
      hasPhoto: false,
    },
  ];

  return {
    profiles,
    companies,
    tariffs,
    shipments,
    tracking,
    warehouses,
    stock,
    drivers,
    routes,
    attempts,
    proofImages: new Map(),
    invoices,
    payments,
    creditNotes: [],
    notifications,
  };
}
