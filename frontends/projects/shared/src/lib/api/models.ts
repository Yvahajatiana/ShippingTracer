// TypeScript mirrors of the service contracts exposed through the gateway (API v1).
// Guid and DateTimeOffset are ISO strings, DateOnly is `YYYY-MM-DD`, decimal is number,
// byte[] is a base64 string. Enums are serialized as names, except Shipment (see shipment.api.ts).

export type Guid = string;
export type IsoDateTime = string;
export type IsoDate = string;

export interface Page<T> {
  page: number;
  pageSize: number;
  total: number;
  items: T[];
}

// ---------- Customer ----------
export interface Address {
  id: Guid;
  label: string;
  line1: string;
  line2: string | null;
  postalCode: string;
  city: string;
  countryCode: string;
}
export interface Profile {
  id: Guid;
  keycloakSubject: string;
  firstName: string;
  lastName: string;
  email: string;
  phone: string | null;
  companyId: Guid | null;
  createdAtUtc: IsoDateTime;
  addresses: Address[];
}
export interface Company {
  id: Guid;
  name: string;
  registrationNumber: string;
  email: string;
}
export interface ProfileBody {
  firstName: string;
  lastName: string;
  email: string;
  phone: string | null;
}
export type AddressBody = Omit<Address, 'id'>;
export type CompanyBody = Omit<Company, 'id'>;

// ---------- Shipment ----------
export const DELIVERY_ZONES = ['Domestic', 'Europe', 'International'] as const;
export type DeliveryZone = (typeof DELIVERY_ZONES)[number];
export const SHIPMENT_STATUSES = [
  'AwaitingPayment',
  'ReadyForPickup',
  'PaymentFailed',
  'PickedUp',
] as const;
export type ShipmentStatus = (typeof SHIPMENT_STATUSES)[number];

export interface ShipmentAddress {
  name: string;
  email: string;
  phone: string;
  line1: string;
  line2: string | null;
  postalCode: string;
  city: string;
  countryCode: string;
}
export interface Parcel {
  weightKg: number;
  lengthCm: number;
  widthCm: number;
  heightCm: number;
}
export interface ShipmentBody {
  sender: ShipmentAddress;
  recipient: ShipmentAddress;
  parcel: Parcel;
}
export interface Shipment {
  id: Guid;
  trackingNumber: string;
  ownerSubject: string;
  sender: ShipmentAddress;
  recipient: ShipmentAddress;
  parcel: Parcel;
  price: number;
  currency: string;
  zone: DeliveryZone;
  status: ShipmentStatus;
  createdAtUtc: IsoDateTime;
}
export interface Quote {
  price: number;
  currency: string;
  zone: DeliveryZone;
  billableWeightKg: number;
}
export interface Tariff {
  zone: DeliveryZone;
  basePrice: number;
  pricePerKg: number;
  currency: string;
}
export interface TariffBody {
  basePrice: number;
  pricePerKg: number;
}

// ---------- Tracking ----------
export const PARCEL_STATUSES = [
  'Created',
  'PickedUp',
  'InTransit',
  'AtSortingCenter',
  'OutForDelivery',
  'Delivered',
  'Failed',
  'Returned',
] as const;
export type ParcelStatus = (typeof PARCEL_STATUSES)[number];

export interface PublicTrackingEntry {
  status: ParcelStatus;
  statusLabel: string;
  occurredAtUtc: IsoDateTime;
  facilityCode: string | null;
}
export interface PublicTracking {
  trackingNumber: string;
  currentStatus: ParcelStatus;
  currentStatusLabel: string;
  updatedAtUtc: IsoDateTime;
  history: PublicTrackingEntry[];
}
export interface InternalTrackingEntry extends PublicTrackingEntry {
  eventId: Guid;
}
export interface InternalTracking {
  shipmentId: Guid;
  trackingNumber: string | null;
  currentStatus: ParcelStatus;
  currentStatusLabel: string;
  updatedAtUtc: IsoDateTime;
  history: InternalTrackingEntry[];
}

// ---------- Warehouse ----------
export const WAREHOUSE_KINDS = ['Warehouse', 'SortingCenter'] as const;
export type WarehouseKind = (typeof WAREHOUSE_KINDS)[number];
export const SCAN_DIRECTIONS = ['Entry', 'Exit'] as const;
export type ScanDirection = (typeof SCAN_DIRECTIONS)[number];

export interface Warehouse {
  id: Guid;
  code: string;
  name: string;
  kind: WarehouseKind;
  street: string;
  city: string;
  postalCode: string;
  countryCode: string;
  isActive: boolean;
}
export type CreateWarehouseBody = Omit<Warehouse, 'id' | 'isActive'>;
export type UpdateWarehouseBody = Omit<Warehouse, 'id' | 'code' | 'kind'>;
export interface StockParcel {
  shipmentId: Guid;
  trackingNumber: string;
  lastScanAtUtc: IsoDateTime;
}
export interface WarehouseStock extends Page<StockParcel> {
  warehouseId: Guid;
}
export interface ScanParcelBody {
  scanId: Guid;
  warehouseId: Guid;
  trackingNumber: string;
  direction: ScanDirection;
}
export interface Scan {
  scanId: Guid;
  shipmentId: Guid;
  warehouseId: Guid;
  trackingNumber: string;
  direction: ScanDirection;
  occurredAtUtc: IsoDateTime;
}

// ---------- Delivery ----------
export const DELIVERY_ROUTE_STATUSES = ['Planned', 'InProgress', 'Completed'] as const;
export type DeliveryRouteStatus = (typeof DELIVERY_ROUTE_STATUSES)[number];
export const DELIVERY_STOP_STATUSES = ['Pending', 'Delivered', 'Failed'] as const;
export type DeliveryStopStatus = (typeof DELIVERY_STOP_STATUSES)[number];
export type DeliveryOutcome = 'Delivered' | 'Failed';
export const DELIVERY_FAILURE_REASONS = [
  'RecipientAbsent',
  'AddressNotFound',
  'RecipientRefused',
] as const;
export type DeliveryFailureReason = (typeof DELIVERY_FAILURE_REASONS)[number];
export type ProofImageKind = 'Signature' | 'Photo';

export interface Driver {
  subject: string;
  displayName: string;
  isActive: boolean;
}
export interface DeliveryStop {
  shipmentId: Guid;
  sequence: number;
  status: DeliveryStopStatus;
  failureReason: DeliveryFailureReason | null;
  completedAtUtc: IsoDateTime | null;
}
export interface DeliveryRoute {
  id: Guid;
  name: string;
  serviceDate: IsoDate;
  driverSubject: string | null;
  status: DeliveryRouteStatus;
  startedAtUtc: IsoDateTime | null;
  completedAtUtc: IsoDateTime | null;
  stops: DeliveryStop[];
}
export interface AvailableParcel {
  shipmentId: Guid;
  trackingNumber: string;
}
export interface DeliveryAttempt {
  operationId: Guid;
  routeId: Guid;
  shipmentId: Guid;
  outcome: DeliveryOutcome;
  occurredAtUtc: IsoDateTime;
  actorSubject: string;
  recipientName: string | null;
  failureReason: DeliveryFailureReason | null;
  hasSignature: boolean;
  hasPhoto: boolean;
}
export interface ConfirmDeliveryBody {
  operationId: Guid;
  recipientName: string;
  /** Base64 PNG. */
  signaturePng: string | null;
  /** Base64 JPEG or PNG. */
  photo: string | null;
}
export interface FailDeliveryBody {
  operationId: Guid;
  reason: DeliveryFailureReason;
}

// ---------- Billing ----------
export const INVOICE_STATUSES = [
  'PendingPayment',
  'Paid',
  'PaymentFailed',
  'PartiallyCredited',
  'Credited',
] as const;
export type InvoiceStatus = (typeof INVOICE_STATUSES)[number];
export type MockPaymentOutcome = 'Approved' | 'Declined';
export const CREDIT_REASONS = ['BillingCorrection', 'ServiceIssue'] as const;
export type CreditReason = (typeof CREDIT_REASONS)[number];

export interface Invoice {
  id: Guid;
  number: string;
  shipmentId: Guid;
  customerSubject: string;
  amount: number;
  currency: string;
  issuedAtUtc: IsoDateTime;
  status: InvoiceStatus;
  creditTotal: number;
  remainingCredit: number;
}
export interface Payment {
  operationId: Guid;
  invoiceId: Guid;
  outcome: MockPaymentOutcome;
  amount: number;
  currency: string;
  occurredAtUtc: IsoDateTime;
}
export interface CreditNote {
  id: Guid;
  number: string;
  invoiceId: Guid;
  amount: number;
  currency: string;
  reason: CreditReason;
  occurredAtUtc: IsoDateTime;
}

// ---------- Notification ----------
export type NotificationChannel = 'Email' | 'Sms';
export interface NotificationRecord {
  id: Guid;
  sourceEventId: Guid;
  shipmentId: Guid;
  customerSubject: string;
  trackingNumber: string;
  channel: NotificationChannel;
  destination: string;
  subject: string;
  body: string;
  occurredAtUtc: IsoDateTime;
  recordedAtUtc: IsoDateTime;
  deliveryStatus: string;
}

// ---------- Reporting ----------
export interface DailyVolume {
  date: IsoDate;
  shipments: number;
}
export interface StatusVolume {
  status: ParcelStatus;
  shipments: number;
}
export interface KpiReport {
  from: IsoDate;
  to: IsoDate;
  generatedAtUtc: IsoDateTime;
  totalShipments: number;
  deliveredShipments: number;
  deliveryRatePercent: number | null;
  averageDeliveryHours: number | null;
  deliveryDurationSamples: number;
  averageTransitHours: number | null;
  transitDurationSamples: number;
  dailyVolumes: DailyVolume[];
  statusVolumes: StatusVolume[];
}
