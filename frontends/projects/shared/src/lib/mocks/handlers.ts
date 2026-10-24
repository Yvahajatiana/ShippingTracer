// MSW handlers reproducing the gateway contract and the main authorization rules of each service.
// They are a development aid only: business rules are simplified and data resets on reload.
import { HttpResponse, delay, http } from 'msw';
import {
  AddressBody,
  CompanyBody,
  ConfirmDeliveryBody,
  CreateWarehouseBody,
  DELIVERY_ZONES,
  DeliveryZone,
  FailDeliveryBody,
  KpiReport,
  PARCEL_STATUSES,
  ProfileBody,
  SHIPMENT_STATUSES,
  ScanParcelBody,
  Shipment,
  ShipmentBody,
  TariffBody,
  UpdateWarehouseBody,
} from '../api/models';
import { Role } from '../auth/auth.service';
import { MockDb, createDb, id, today } from './db';

const API = '*/api/v1';

interface Identity {
  sub: string;
  roles: Role[];
}

const STAFF: Role[] = ['operator', 'admin'];
const EVERYONE: Role[] = ['customer', 'operator', 'admin'];
const EU_COUNTRIES = new Set([
  'AT',
  'BE',
  'BG',
  'CY',
  'CZ',
  'DE',
  'DK',
  'EE',
  'ES',
  'FI',
  'GR',
  'HR',
  'HU',
  'IE',
  'IT',
  'LT',
  'LU',
  'LV',
  'MT',
  'NL',
  'PL',
  'PT',
  'RO',
  'SE',
  'SI',
  'SK',
]);

function problem(status: number, title: string, errors?: Record<string, string[]>) {
  return HttpResponse.json(
    {
      type: `https://httpstatuses.io/${status}`,
      title,
      status,
      traceId: `00-mock-${id().slice(0, 8)}-01`,
      ...(errors ? { errors } : {}),
    },
    { status, headers: { 'Content-Type': 'application/problem+json' } },
  );
}

function identityOf(request: Request): Identity | null {
  const header = request.headers.get('Authorization') ?? '';
  const match = /^Bearer mock\.(.+)$/.exec(header);
  if (!match) {
    return null;
  }
  try {
    const parsed = JSON.parse(atob(match[1])) as Identity;
    return typeof parsed.sub === 'string' && Array.isArray(parsed.roles) ? parsed : null;
  } catch {
    return null;
  }
}

/** Returns the caller identity, or the 401/403 response to send. */
function authorize(request: Request, roles: Role[]): Identity | Response {
  const identity = identityOf(request);
  if (!identity) {
    return problem(401, 'Unauthorized');
  }
  return identity.roles.some((role) => roles.includes(role)) ? identity : problem(403, 'Forbidden');
}

const isStaff = (identity: Identity) => identity.roles.some((role) => STAFF.includes(role));
const isAdmin = (identity: Identity) => identity.roles.includes('admin');

function pageOf<T>(items: T[], request: Request) {
  const url = new URL(request.url);
  const page = Math.max(1, Number(url.searchParams.get('page') ?? 1));
  const pageSize = Math.min(100, Math.max(1, Number(url.searchParams.get('pageSize') ?? 50)));
  return {
    page,
    pageSize,
    total: items.length,
    items: items.slice((page - 1) * pageSize, page * pageSize),
  };
}

function sliceOf<T>(items: T[], request: Request): T[] {
  const url = new URL(request.url);
  const offset = Math.max(0, Number(url.searchParams.get('offset') ?? 0));
  const limit = Math.min(100, Math.max(1, Number(url.searchParams.get('limit') ?? 20)));
  return items.slice(offset, offset + limit);
}

function requireFields(
  body: Record<string, unknown>,
  fields: string[],
): Record<string, string[]> | null {
  const errors: Record<string, string[]> = {};
  for (const field of fields) {
    const value = field
      .split('.')
      .reduce<unknown>(
        (current, key) => (current as Record<string, unknown> | undefined)?.[key],
        body,
      );
    if (
      value === undefined ||
      value === null ||
      (typeof value === 'string' && value.trim() === '')
    ) {
      const pascal = field
        .split('.')
        .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
        .join('.');
      errors[pascal] = ["'" + pascal + "' must not be empty."];
    }
  }
  return Object.keys(errors).length ? errors : null;
}

/** Shipment still emits enums as integers (issue #23); the mock mirrors it. */
function wireShipment(shipment: Shipment) {
  return {
    ...shipment,
    zone: DELIVERY_ZONES.indexOf(shipment.zone),
    status: SHIPMENT_STATUSES.indexOf(shipment.status),
  };
}

function zoneFor(body: ShipmentBody): DeliveryZone {
  const from = body.sender.countryCode;
  const to = body.recipient.countryCode;
  if (from === to) {
    return 'Domestic';
  }
  return EU_COUNTRIES.has(to) || to === 'FR' ? 'Europe' : 'International';
}

function quoteFor(db: MockDb, body: ShipmentBody) {
  const zone = zoneFor(body);
  const tariff = db.tariffs.find((item) => item.zone === zone)!;
  const volumetric = (body.parcel.lengthCm * body.parcel.widthCm * body.parcel.heightCm) / 5000;
  const billableWeightKg = Math.round(Math.max(body.parcel.weightKg, volumetric) * 100) / 100;
  const price = Math.round((tariff.basePrice + tariff.pricePerKg * billableWeightKg) * 100) / 100;
  return { price, currency: tariff.currency, zone, billableWeightKg };
}

const SHIPMENT_FIELDS = [
  'sender.name',
  'sender.email',
  'sender.phone',
  'sender.line1',
  'sender.postalCode',
  'sender.city',
  'sender.countryCode',
  'recipient.name',
  'recipient.email',
  'recipient.phone',
  'recipient.line1',
  'recipient.postalCode',
  'recipient.city',
  'recipient.countryCode',
];

function validateShipment(body: ShipmentBody) {
  const errors = requireFields(body as unknown as Record<string, unknown>, SHIPMENT_FIELDS) ?? {};
  const parcel = body.parcel ?? ({} as ShipmentBody['parcel']);
  if (!(parcel.weightKg > 0 && parcel.weightKg <= 30))
    errors['Parcel.WeightKg'] = ['Weight must be between 0 and 30 kg.'];
  for (const key of ['lengthCm', 'widthCm', 'heightCm'] as const) {
    if (!(parcel[key] > 0 && parcel[key] <= 150)) {
      errors[`Parcel.${key.charAt(0).toUpperCase()}${key.slice(1)}`] = [
        'Dimension must be between 0 and 150 cm.',
      ];
    }
  }
  return Object.keys(errors).length ? errors : null;
}

function statusLabel(status: string) {
  return status.replace(/([a-z])([A-Z])/g, '$1 $2');
}

function trackingView(db: MockDb, shipment: Shipment) {
  const events = db.tracking.get(shipment.id) ?? [];
  const last = events[events.length - 1];
  return {
    shipmentId: shipment.id,
    trackingNumber: shipment.trackingNumber,
    currentStatus: last?.status ?? 'Created',
    currentStatusLabel: statusLabel(last?.status ?? 'Created'),
    updatedAtUtc: last?.occurredAtUtc ?? shipment.createdAtUtc,
    history: events.map((event) => ({ ...event, statusLabel: statusLabel(event.status) })),
  };
}

function addTracking(
  db: MockDb,
  shipmentId: string,
  status: (typeof PARCEL_STATUSES)[number],
  facilityCode: string | null = null,
) {
  const events = db.tracking.get(shipmentId) ?? [];
  events.push({ eventId: id(), status, occurredAtUtc: new Date().toISOString(), facilityCode });
  db.tracking.set(shipmentId, events);
}

function base64ToBlob(value: string, type: string): Blob {
  const bytes = Uint8Array.from(atob(value), (char) => char.charCodeAt(0));
  return new Blob([bytes], { type });
}

function labelPdf(trackingNumber: string): Blob {
  const content = `BT /F1 24 Tf 72 720 Td (ParcelFlow ${trackingNumber}) Tj ET`;
  const pdf = `%PDF-1.4
1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj
2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj
3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Contents 4 0 R/Resources<</Font<</F1 5 0 R>>>>>>endobj
4 0 obj<</Length ${content.length}>>stream
${content}
endstream endobj
5 0 obj<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>endobj
trailer<</Root 1 0 R>>
%%EOF`;
  return new Blob([pdf], { type: 'application/pdf' });
}

export function createHandlers(db: MockDb = createDb()) {
  const latency = () => delay(150 + Math.round(Math.random() * 250));

  return [
    // ---------- Customer ----------
    http.get(`${API}/customers/me`, async ({ request }) => {
      await latency();
      const who = authorize(request, EVERYONE);
      if (who instanceof Response) return who;
      const profile = db.profiles.find((item) => item.keycloakSubject === who.sub);
      return profile ? HttpResponse.json(profile) : problem(404, 'Profile not found');
    }),
    http.post(`${API}/customers`, async ({ request }) => {
      await latency();
      const who = authorize(request, EVERYONE);
      if (who instanceof Response) return who;
      const body = (await request.json()) as ProfileBody;
      const errors = requireFields(body as unknown as Record<string, unknown>, [
        'firstName',
        'lastName',
        'email',
      ]);
      if (errors) return problem(400, 'Validation failed', errors);
      if (db.profiles.some((item) => item.keycloakSubject === who.sub))
        return problem(409, 'Profile already exists');
      const profile = {
        id: id(),
        keycloakSubject: who.sub,
        ...body,
        phone: body.phone || null,
        companyId: null,
        createdAtUtc: new Date().toISOString(),
        addresses: [],
      };
      db.profiles.push(profile);
      return HttpResponse.json(profile, { status: 201 });
    }),
    http.get(`${API}/customers`, async ({ request }) => {
      await latency();
      const who = authorize(request, STAFF);
      if (who instanceof Response) return who;
      return HttpResponse.json(sliceOf(db.profiles, request));
    }),
    http.get(`${API}/customers/:id`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, EVERYONE);
      if (who instanceof Response) return who;
      const profile = db.profiles.find((item) => item.id === params['id']);
      if (!profile) return problem(404, 'Profile not found');
      if (profile.keycloakSubject !== who.sub && !isStaff(who)) return problem(403, 'Forbidden');
      return HttpResponse.json(profile);
    }),
    http.put(`${API}/customers/:id`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, EVERYONE);
      if (who instanceof Response) return who;
      const profile = db.profiles.find((item) => item.id === params['id']);
      if (!profile) return problem(404, 'Profile not found');
      if (profile.keycloakSubject !== who.sub && !isAdmin(who)) return problem(403, 'Forbidden');
      const body = (await request.json()) as ProfileBody;
      const errors = requireFields(body as unknown as Record<string, unknown>, [
        'firstName',
        'lastName',
        'email',
      ]);
      if (errors) return problem(400, 'Validation failed', errors);
      Object.assign(profile, body, { phone: body.phone || null });
      return HttpResponse.json(profile);
    }),
    http.put(`${API}/customers/:id/company`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, ['admin']);
      if (who instanceof Response) return who;
      const profile = db.profiles.find((item) => item.id === params['id']);
      if (!profile) return problem(404, 'Profile not found');
      const { companyId } = (await request.json()) as { companyId: string | null };
      if (companyId && !db.companies.some((item) => item.id === companyId))
        return problem(404, 'Company not found');
      profile.companyId = companyId;
      return HttpResponse.json(profile);
    }),
    http.post(`${API}/customers/:id/addresses`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, EVERYONE);
      if (who instanceof Response) return who;
      const profile = db.profiles.find((item) => item.id === params['id']);
      if (!profile) return problem(404, 'Profile not found');
      if (profile.keycloakSubject !== who.sub && !isAdmin(who)) return problem(403, 'Forbidden');
      const body = (await request.json()) as AddressBody;
      const errors = requireFields(body as unknown as Record<string, unknown>, [
        'label',
        'line1',
        'postalCode',
        'city',
        'countryCode',
      ]);
      if (errors) return problem(400, 'Validation failed', errors);
      const address = { id: id(), ...body, line2: body.line2 || null };
      profile.addresses.push(address);
      return HttpResponse.json(address, { status: 201 });
    }),
    http.put(`${API}/customers/:id/addresses/:addressId`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, EVERYONE);
      if (who instanceof Response) return who;
      const profile = db.profiles.find((item) => item.id === params['id']);
      const address = profile?.addresses.find((item) => item.id === params['addressId']);
      if (!profile || !address) return problem(404, 'Address not found');
      if (profile.keycloakSubject !== who.sub && !isAdmin(who)) return problem(403, 'Forbidden');
      const body = (await request.json()) as AddressBody;
      Object.assign(address, body, { line2: body.line2 || null });
      return HttpResponse.json(address);
    }),
    http.delete(`${API}/customers/:id/addresses/:addressId`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, EVERYONE);
      if (who instanceof Response) return who;
      const profile = db.profiles.find((item) => item.id === params['id']);
      if (!profile) return problem(404, 'Profile not found');
      if (profile.keycloakSubject !== who.sub && !isAdmin(who)) return problem(403, 'Forbidden');
      profile.addresses = profile.addresses.filter((item) => item.id !== params['addressId']);
      return new HttpResponse(null, { status: 204 });
    }),
    http.get(`${API}/companies`, async ({ request }) => {
      await latency();
      const who = authorize(request, STAFF);
      if (who instanceof Response) return who;
      return HttpResponse.json(db.companies);
    }),
    http.get(`${API}/companies/:id`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, STAFF);
      if (who instanceof Response) return who;
      const company = db.companies.find((item) => item.id === params['id']);
      return company ? HttpResponse.json(company) : problem(404, 'Company not found');
    }),
    http.post(`${API}/companies`, async ({ request }) => {
      await latency();
      const who = authorize(request, ['admin']);
      if (who instanceof Response) return who;
      const body = (await request.json()) as CompanyBody;
      const errors = requireFields(body as unknown as Record<string, unknown>, [
        'name',
        'registrationNumber',
        'email',
      ]);
      if (errors) return problem(400, 'Validation failed', errors);
      const company = { id: id(), ...body };
      db.companies.push(company);
      return HttpResponse.json(company, { status: 201 });
    }),
    http.put(`${API}/companies/:id`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, ['admin']);
      if (who instanceof Response) return who;
      const company = db.companies.find((item) => item.id === params['id']);
      if (!company) return problem(404, 'Company not found');
      Object.assign(company, (await request.json()) as CompanyBody);
      return HttpResponse.json(company);
    }),

    // ---------- Shipment ----------
    http.post(`${API}/shipments/quote`, async ({ request }) => {
      await latency();
      const who = authorize(request, EVERYONE);
      if (who instanceof Response) return who;
      const body = (await request.json()) as ShipmentBody;
      const errors = validateShipment(body);
      if (errors) return problem(400, 'Validation failed', errors);
      const quote = quoteFor(db, body);
      return HttpResponse.json({ ...quote, zone: DELIVERY_ZONES.indexOf(quote.zone) });
    }),
    http.post(`${API}/shipments`, async ({ request }) => {
      await latency();
      const who = authorize(request, EVERYONE);
      if (who instanceof Response) return who;
      const body = (await request.json()) as ShipmentBody;
      const errors = validateShipment(body);
      if (errors) return problem(400, 'Validation failed', errors);
      const quote = quoteFor(db, body);
      const shipment: Shipment = {
        id: id(),
        trackingNumber: `PF${Math.floor(10_000_000 + Math.random() * 89_999_999)}FR`,
        ownerSubject: who.sub,
        sender: body.sender,
        recipient: body.recipient,
        parcel: body.parcel,
        price: quote.price,
        currency: quote.currency,
        zone: quote.zone,
        status: 'AwaitingPayment',
        createdAtUtc: new Date().toISOString(),
      };
      db.shipments.unshift(shipment);
      addTracking(db, shipment.id, 'Created');
      // Billing keys an invoice by its shipment: the invoice id is the shipment id.
      db.invoices.unshift({
        id: shipment.id,
        number: `INV-2026-${1000 + db.invoices.length + 1}`,
        shipmentId: shipment.id,
        customerSubject: who.sub,
        amount: quote.price,
        currency: quote.currency,
        issuedAtUtc: shipment.createdAtUtc,
        status: 'PendingPayment',
        creditTotal: 0,
        remainingCredit: quote.price,
      });
      return HttpResponse.json(wireShipment(shipment), { status: 201 });
    }),
    http.get(`${API}/shipments`, async ({ request }) => {
      await latency();
      const who = authorize(request, EVERYONE);
      if (who instanceof Response) return who;
      const visible = isStaff(who)
        ? db.shipments
        : db.shipments.filter((item) => item.ownerSubject === who.sub);
      return HttpResponse.json(sliceOf(visible, request).map(wireShipment));
    }),
    http.get(`${API}/shipments/:id`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, EVERYONE);
      if (who instanceof Response) return who;
      const shipment = db.shipments.find((item) => item.id === params['id']);
      if (!shipment || (!isStaff(who) && shipment.ownerSubject !== who.sub))
        return problem(404, 'Shipment not found');
      return HttpResponse.json(wireShipment(shipment));
    }),
    http.get(`${API}/shipments/:id/label`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, EVERYONE);
      if (who instanceof Response) return who;
      const shipment = db.shipments.find((item) => item.id === params['id']);
      if (!shipment || (!isStaff(who) && shipment.ownerSubject !== who.sub))
        return problem(404, 'Shipment not found');
      return new HttpResponse(labelPdf(shipment.trackingNumber), {
        headers: {
          'Content-Type': 'application/pdf',
          'Content-Disposition': `attachment; filename="${shipment.trackingNumber}.pdf"`,
        },
      });
    }),
    http.get(`${API}/tariffs`, async ({ request }) => {
      await latency();
      const who = authorize(request, EVERYONE);
      if (who instanceof Response) return who;
      return HttpResponse.json(
        db.tariffs.map((tariff) => ({ ...tariff, zone: DELIVERY_ZONES.indexOf(tariff.zone) })),
      );
    }),
    http.put(`${API}/tariffs/:zone`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, ['admin']);
      if (who instanceof Response) return who;
      const tariff = db.tariffs.find((item) => item.zone === params['zone']);
      if (!tariff) return problem(404, 'Tariff not found');
      const body = (await request.json()) as TariffBody;
      if (!(body.basePrice >= 0) || !(body.pricePerKg >= 0)) {
        return problem(400, 'Validation failed', { BasePrice: ['Prices must be positive.'] });
      }
      Object.assign(tariff, body);
      return HttpResponse.json({ ...tariff, zone: DELIVERY_ZONES.indexOf(tariff.zone) });
    }),

    // ---------- Tracking ----------
    http.get(`${API}/tracking/shipments/:shipmentId`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, STAFF);
      if (who instanceof Response) return who;
      const shipment = db.shipments.find((item) => item.id === params['shipmentId']);
      return shipment
        ? HttpResponse.json(trackingView(db, shipment))
        : problem(404, 'Tracking not found');
    }),
    http.get(`${API}/tracking/:trackingNumber`, async ({ params }) => {
      await latency();
      const number = String(params['trackingNumber']).trim().toUpperCase();
      if (!/^[A-Z0-9]{6,32}$/.test(number)) {
        return problem(400, 'Validation failed', { TrackingNumber: ['Invalid tracking number.'] });
      }
      const shipment = db.shipments.find((item) => item.trackingNumber === number);
      if (!shipment) return problem(404, 'Tracking not found');
      // The public view hides internal identifiers.
      const { trackingNumber, currentStatus, currentStatusLabel, updatedAtUtc, history } =
        trackingView(db, shipment);
      return HttpResponse.json({
        trackingNumber,
        currentStatus,
        currentStatusLabel,
        updatedAtUtc,
        history: history.map(({ status, statusLabel, occurredAtUtc, facilityCode }) => ({
          status,
          statusLabel,
          occurredAtUtc,
          facilityCode,
        })),
      });
    }),

    // ---------- Warehouse ----------
    http.get(`${API}/warehouses`, async ({ request }) => {
      await latency();
      const who = authorize(request, STAFF);
      if (who instanceof Response) return who;
      return HttpResponse.json(pageOf(db.warehouses, request));
    }),
    http.post(`${API}/warehouses/scans`, async ({ request }) => {
      await latency();
      const who = authorize(request, STAFF);
      if (who instanceof Response) return who;
      const body = (await request.json()) as ScanParcelBody;
      const warehouse = db.warehouses.find((item) => item.id === body.warehouseId);
      if (!warehouse) return problem(404, 'Warehouse not found');
      if (!warehouse.isActive) return problem(409, 'Warehouse is inactive');
      const shipment = db.shipments.find(
        (item) => item.trackingNumber === body.trackingNumber.trim().toUpperCase(),
      );
      if (!shipment) return problem(404, 'Parcel not found');
      const inStock = db.stock.find((item) => item.shipmentId === shipment.id);
      if (body.direction === 'Entry') {
        if (inStock) return problem(409, 'Parcel already in a warehouse');
        db.stock.push({
          warehouseId: warehouse.id,
          shipmentId: shipment.id,
          trackingNumber: shipment.trackingNumber,
          lastScanAtUtc: new Date().toISOString(),
        });
        addTracking(
          db,
          shipment.id,
          warehouse.kind === 'SortingCenter' ? 'AtSortingCenter' : 'InTransit',
          warehouse.code,
        );
      } else {
        if (!inStock || inStock.warehouseId !== warehouse.id)
          return problem(409, 'Parcel is not in this warehouse');
        db.stock = db.stock.filter((item) => item !== inStock);
      }
      return HttpResponse.json({
        scanId: body.scanId,
        shipmentId: shipment.id,
        warehouseId: warehouse.id,
        trackingNumber: shipment.trackingNumber,
        direction: body.direction,
        occurredAtUtc: new Date().toISOString(),
      });
    }),
    http.post(`${API}/warehouses`, async ({ request }) => {
      await latency();
      const who = authorize(request, ['admin']);
      if (who instanceof Response) return who;
      const body = (await request.json()) as CreateWarehouseBody;
      const errors = requireFields(body as unknown as Record<string, unknown>, [
        'code',
        'name',
        'kind',
        'street',
        'city',
        'postalCode',
        'countryCode',
      ]);
      if (errors) return problem(400, 'Validation failed', errors);
      if (db.warehouses.some((item) => item.code === body.code))
        return problem(409, 'Warehouse code already used');
      const warehouse = { id: id(), ...body, isActive: true };
      db.warehouses.push(warehouse);
      return HttpResponse.json(warehouse, { status: 201 });
    }),
    http.get(`${API}/warehouses/:warehouseId`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, STAFF);
      if (who instanceof Response) return who;
      const warehouse = db.warehouses.find((item) => item.id === params['warehouseId']);
      return warehouse ? HttpResponse.json(warehouse) : problem(404, 'Warehouse not found');
    }),
    http.put(`${API}/warehouses/:warehouseId`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, ['admin']);
      if (who instanceof Response) return who;
      const warehouse = db.warehouses.find((item) => item.id === params['warehouseId']);
      if (!warehouse) return problem(404, 'Warehouse not found');
      Object.assign(warehouse, (await request.json()) as UpdateWarehouseBody);
      return HttpResponse.json(warehouse);
    }),
    http.get(`${API}/warehouses/:warehouseId/stock`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, STAFF);
      if (who instanceof Response) return who;
      if (!db.warehouses.some((item) => item.id === params['warehouseId']))
        return problem(404, 'Warehouse not found');
      const items = db.stock
        .filter((item) => item.warehouseId === params['warehouseId'])
        .map(({ shipmentId, trackingNumber, lastScanAtUtc }) => ({
          shipmentId,
          trackingNumber,
          lastScanAtUtc,
        }));
      return HttpResponse.json({ warehouseId: params['warehouseId'], ...pageOf(items, request) });
    }),

    // ---------- Delivery ----------
    http.get(`${API}/delivery/drivers`, async ({ request }) => {
      await latency();
      const who = authorize(request, STAFF);
      if (who instanceof Response) return who;
      return HttpResponse.json(pageOf(db.drivers, request));
    }),
    http.post(`${API}/delivery/drivers`, async ({ request }) => {
      await latency();
      const who = authorize(request, ['admin']);
      if (who instanceof Response) return who;
      const body = (await request.json()) as { subject: string; displayName: string };
      const errors = requireFields(body, ['subject', 'displayName']);
      if (errors) return problem(400, 'Validation failed', errors);
      if (db.drivers.some((item) => item.subject === body.subject))
        return problem(409, 'Driver already exists');
      const driver = { ...body, isActive: true };
      db.drivers.push(driver);
      return HttpResponse.json(driver, { status: 201 });
    }),
    http.put(`${API}/delivery/drivers/:subject`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, ['admin']);
      if (who instanceof Response) return who;
      const driver = db.drivers.find((item) => item.subject === params['subject']);
      if (!driver) return problem(404, 'Driver not found');
      Object.assign(driver, (await request.json()) as { displayName: string; isActive: boolean });
      return HttpResponse.json(driver);
    }),
    http.get(`${API}/delivery/routes`, async ({ request }) => {
      await latency();
      const who = authorize(request, STAFF);
      if (who instanceof Response) return who;
      return HttpResponse.json(pageOf(db.routes, request));
    }),
    http.post(`${API}/delivery/routes`, async ({ request }) => {
      await latency();
      const who = authorize(request, STAFF);
      if (who instanceof Response) return who;
      const body = (await request.json()) as { name: string; serviceDate: string };
      const errors = requireFields(body, ['name', 'serviceDate']);
      if (errors) return problem(400, 'Validation failed', errors);
      if (body.serviceDate < today())
        return problem(400, 'Validation failed', {
          ServiceDate: ['Service date cannot be in the past.'],
        });
      const route = {
        id: id(),
        name: body.name,
        serviceDate: body.serviceDate,
        driverSubject: null,
        status: 'Planned' as const,
        startedAtUtc: null,
        completedAtUtc: null,
        stops: [],
      };
      db.routes.unshift(route);
      return HttpResponse.json(route, { status: 201 });
    }),
    http.get(`${API}/delivery/routes/:routeId`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, STAFF);
      if (who instanceof Response) return who;
      const route = db.routes.find((item) => item.id === params['routeId']);
      return route ? HttpResponse.json(route) : problem(404, 'Route not found');
    }),
    http.put(`${API}/delivery/routes/:routeId/driver`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, STAFF);
      if (who instanceof Response) return who;
      const route = db.routes.find((item) => item.id === params['routeId']);
      if (!route) return problem(404, 'Route not found');
      if (route.status !== 'Planned') return problem(409, 'Route already started');
      const { driverSubject } = (await request.json()) as { driverSubject: string };
      const driver = db.drivers.find((item) => item.subject === driverSubject);
      if (!driver?.isActive) return problem(409, 'Driver unavailable');
      route.driverSubject = driverSubject;
      return HttpResponse.json(route);
    }),
    http.post(`${API}/delivery/routes/:routeId/stops`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, STAFF);
      if (who instanceof Response) return who;
      const route = db.routes.find((item) => item.id === params['routeId']);
      if (!route) return problem(404, 'Route not found');
      if (route.status !== 'Planned') return problem(409, 'Route already started');
      const { shipmentId } = (await request.json()) as { shipmentId: string };
      if (
        db.routes.some(
          (item) =>
            item.status !== 'Completed' &&
            item.stops.some((stop) => stop.shipmentId === shipmentId),
        )
      ) {
        return problem(409, 'Parcel already planned');
      }
      route.stops.push({
        shipmentId,
        sequence: route.stops.length + 1,
        status: 'Pending',
        failureReason: null,
        completedAtUtc: null,
      });
      return HttpResponse.json(route);
    }),
    http.delete(
      `${API}/delivery/routes/:routeId/stops/:shipmentId`,
      async ({ request, params }) => {
        await latency();
        const who = authorize(request, STAFF);
        if (who instanceof Response) return who;
        const route = db.routes.find((item) => item.id === params['routeId']);
        if (!route) return problem(404, 'Route not found');
        if (route.status !== 'Planned') return problem(409, 'Route already started');
        route.stops = route.stops
          .filter((stop) => stop.shipmentId !== params['shipmentId'])
          .map((stop, index) => ({ ...stop, sequence: index + 1 }));
        return new HttpResponse(null, { status: 204 });
      },
    ),
    http.post(`${API}/delivery/routes/:routeId/start`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, STAFF);
      if (who instanceof Response) return who;
      const route = db.routes.find((item) => item.id === params['routeId']);
      if (!route) return problem(404, 'Route not found');
      if (route.status !== 'Planned' || !route.driverSubject || route.stops.length === 0) {
        return problem(409, 'Route cannot be started');
      }
      route.status = 'InProgress';
      route.startedAtUtc = new Date().toISOString();
      route.stops.forEach((stop) => addTracking(db, stop.shipmentId, 'OutForDelivery'));
      return HttpResponse.json(route);
    }),
    http.post(
      `${API}/delivery/routes/:routeId/stops/:shipmentId/delivery`,
      async ({ request, params }) => {
        await latency();
        const who = authorize(request, STAFF);
        if (who instanceof Response) return who;
        const route = db.routes.find((item) => item.id === params['routeId']);
        const stop = route?.stops.find((item) => item.shipmentId === params['shipmentId']);
        if (!route || !stop) return problem(404, 'Stop not found');
        if (route.status !== 'InProgress' || stop.status !== 'Pending')
          return problem(409, 'Stop already completed');
        const body = (await request.json()) as ConfirmDeliveryBody;
        const errors =
          requireFields(body as unknown as Record<string, unknown>, ['recipientName']) ?? {};
        if (!body.signaturePng && !body.photo)
          errors['Proof'] = ['A signature or a photo is required.'];
        if (Object.keys(errors).length) return problem(400, 'Validation failed', errors);
        const now = new Date().toISOString();
        Object.assign(stop, { status: 'Delivered', completedAtUtc: now });
        db.proofImages.set(`${route.id}:${stop.shipmentId}`, {
          signature: body.signaturePng ? base64ToBlob(body.signaturePng, 'image/png') : undefined,
          photo: body.photo ? base64ToBlob(body.photo, 'image/jpeg') : undefined,
        });
        addTracking(db, stop.shipmentId, 'Delivered');
        if (route.stops.every((item) => item.status !== 'Pending'))
          Object.assign(route, { status: 'Completed', completedAtUtc: now });
        const attempt = {
          operationId: body.operationId,
          routeId: route.id,
          shipmentId: stop.shipmentId,
          outcome: 'Delivered' as const,
          occurredAtUtc: now,
          actorSubject: who.sub,
          recipientName: body.recipientName,
          failureReason: null,
          hasSignature: !!body.signaturePng,
          hasPhoto: !!body.photo,
        };
        db.attempts.push(attempt);
        return HttpResponse.json(attempt);
      },
    ),
    http.post(
      `${API}/delivery/routes/:routeId/stops/:shipmentId/failure`,
      async ({ request, params }) => {
        await latency();
        const who = authorize(request, STAFF);
        if (who instanceof Response) return who;
        const route = db.routes.find((item) => item.id === params['routeId']);
        const stop = route?.stops.find((item) => item.shipmentId === params['shipmentId']);
        if (!route || !stop) return problem(404, 'Stop not found');
        if (route.status !== 'InProgress' || stop.status !== 'Pending')
          return problem(409, 'Stop already completed');
        const body = (await request.json()) as FailDeliveryBody;
        const now = new Date().toISOString();
        Object.assign(stop, { status: 'Failed', failureReason: body.reason, completedAtUtc: now });
        addTracking(db, stop.shipmentId, 'Failed');
        if (route.stops.every((item) => item.status !== 'Pending'))
          Object.assign(route, { status: 'Completed', completedAtUtc: now });
        const attempt = {
          operationId: body.operationId,
          routeId: route.id,
          shipmentId: stop.shipmentId,
          outcome: 'Failed' as const,
          occurredAtUtc: now,
          actorSubject: who.sub,
          recipientName: null,
          failureReason: body.reason,
          hasSignature: false,
          hasPhoto: false,
        };
        db.attempts.push(attempt);
        return HttpResponse.json(attempt);
      },
    ),
    http.get(
      `${API}/delivery/routes/:routeId/stops/:shipmentId/proof`,
      async ({ request, params }) => {
        await latency();
        const who = authorize(request, STAFF);
        if (who instanceof Response) return who;
        const attempt = db.attempts.find(
          (item) => item.routeId === params['routeId'] && item.shipmentId === params['shipmentId'],
        );
        return attempt ? HttpResponse.json(attempt) : problem(404, 'Proof not found');
      },
    ),
    http.get(
      `${API}/delivery/routes/:routeId/stops/:shipmentId/proof/:kind`,
      async ({ request, params }) => {
        await latency();
        const who = authorize(request, STAFF);
        if (who instanceof Response) return who;
        const images = db.proofImages.get(`${params['routeId']}:${params['shipmentId']}`);
        const kind = String(params['kind']).toLowerCase();
        if (kind !== 'signature' && kind !== 'photo')
          return problem(400, 'Unknown proof image kind.');
        const image = kind === 'signature' ? images?.signature : images?.photo;
        return image
          ? new HttpResponse(image, { headers: { 'Content-Type': image.type } })
          : problem(404, 'Proof image not found');
      },
    ),
    http.get(`${API}/delivery/parcels/available`, async ({ request }) => {
      await latency();
      const who = authorize(request, STAFF);
      if (who instanceof Response) return who;
      const planned = new Set(
        db.routes
          .filter((route) => route.status !== 'Completed')
          .flatMap((route) => route.stops.map((stop) => stop.shipmentId)),
      );
      const inStock = new Set(db.stock.map((item) => item.shipmentId));
      const items = db.shipments
        .filter(
          (item) =>
            (item.status === 'ReadyForPickup' || item.status === 'PickedUp') &&
            !planned.has(item.id) &&
            !inStock.has(item.id),
        )
        .filter((item) => {
          const last = db.tracking.get(item.id)?.at(-1)?.status;
          return last !== 'Delivered' && last !== 'Returned';
        })
        .map((item) => ({ shipmentId: item.id, trackingNumber: item.trackingNumber }));
      return HttpResponse.json(pageOf(items, request));
    }),

    // ---------- Billing ----------
    http.get(`${API}/billing/invoices`, async ({ request }) => {
      await latency();
      const who = authorize(request, ['admin', 'customer']);
      if (who instanceof Response) return who;
      const visible = isAdmin(who)
        ? db.invoices
        : db.invoices.filter((item) => item.customerSubject === who.sub);
      return HttpResponse.json(pageOf(visible, request));
    }),
    http.get(`${API}/billing/invoices/:invoiceId`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, ['admin', 'customer']);
      if (who instanceof Response) return who;
      const invoice = db.invoices.find((item) => item.id === params['invoiceId']);
      if (!invoice || (!isAdmin(who) && invoice.customerSubject !== who.sub))
        return problem(404, 'Invoice not found');
      return HttpResponse.json(invoice);
    }),
    http.get(`${API}/billing/invoices/:invoiceId/payment`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, ['admin', 'customer']);
      if (who instanceof Response) return who;
      const invoice = db.invoices.find((item) => item.id === params['invoiceId']);
      if (!invoice || (!isAdmin(who) && invoice.customerSubject !== who.sub))
        return problem(404, 'Invoice not found');
      const payment = db.payments.filter((item) => item.invoiceId === invoice.id).at(-1);
      return payment ? HttpResponse.json(payment) : problem(404, 'Payment not found');
    }),
    http.post(`${API}/billing/invoices/:invoiceId/payments`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, ['admin', 'customer']);
      if (who instanceof Response) return who;
      const invoice = db.invoices.find((item) => item.id === params['invoiceId']);
      if (!invoice || (!isAdmin(who) && invoice.customerSubject !== who.sub))
        return problem(404, 'Invoice not found');
      const body = (await request.json()) as {
        operationId: string;
        outcome: 'Approved' | 'Declined';
      };
      const replay = db.payments.find((item) => item.operationId === body.operationId);
      if (replay) return HttpResponse.json(replay);
      if (invoice.status !== 'PendingPayment')
        return problem(409, 'Invoice is not awaiting payment');
      const payment = {
        operationId: body.operationId,
        invoiceId: invoice.id,
        outcome: body.outcome,
        amount: invoice.amount,
        currency: invoice.currency,
        occurredAtUtc: new Date().toISOString(),
      };
      db.payments.push(payment);
      invoice.status = body.outcome === 'Approved' ? 'Paid' : 'PaymentFailed';
      const shipment = db.shipments.find((item) => item.id === invoice.shipmentId);
      if (shipment)
        shipment.status = body.outcome === 'Approved' ? 'ReadyForPickup' : 'PaymentFailed';
      return HttpResponse.json(payment);
    }),
    http.get(`${API}/billing/invoices/:invoiceId/credit-notes`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, ['admin', 'customer']);
      if (who instanceof Response) return who;
      const invoice = db.invoices.find((item) => item.id === params['invoiceId']);
      if (!invoice || (!isAdmin(who) && invoice.customerSubject !== who.sub))
        return problem(404, 'Invoice not found');
      return HttpResponse.json(
        pageOf(
          db.creditNotes.filter((item) => item.invoiceId === invoice.id),
          request,
        ),
      );
    }),
    http.get(
      `${API}/billing/invoices/:invoiceId/credit-notes/:creditNoteId`,
      async ({ request, params }) => {
        await latency();
        const who = authorize(request, ['admin', 'customer']);
        if (who instanceof Response) return who;
        const invoice = db.invoices.find((item) => item.id === params['invoiceId']);
        if (!invoice || (!isAdmin(who) && invoice.customerSubject !== who.sub))
          return problem(404, 'Invoice not found');
        const note = db.creditNotes.find(
          (item) => item.id === params['creditNoteId'] && item.invoiceId === invoice.id,
        );
        return note ? HttpResponse.json(note) : problem(404, 'Credit note not found');
      },
    ),
    http.post(`${API}/billing/invoices/:invoiceId/credit-notes`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, ['admin']);
      if (who instanceof Response) return who;
      const invoice = db.invoices.find((item) => item.id === params['invoiceId']);
      if (!invoice) return problem(404, 'Invoice not found');
      const body = (await request.json()) as {
        operationId: string;
        amount: number;
        reason: 'BillingCorrection' | 'ServiceIssue';
      };
      if (invoice.status !== 'Paid' && invoice.status !== 'PartiallyCredited')
        return problem(409, 'Invoice cannot be credited');
      if (!(body.amount > 0) || body.amount > invoice.remainingCredit) {
        return problem(400, 'Validation failed', {
          Amount: [`Amount must be between 0 and ${invoice.remainingCredit}.`],
        });
      }
      const note = {
        id: id(),
        number: `CN-2026-${100 + db.creditNotes.length + 1}`,
        invoiceId: invoice.id,
        amount: body.amount,
        currency: invoice.currency,
        reason: body.reason,
        occurredAtUtc: new Date().toISOString(),
      };
      db.creditNotes.push(note);
      invoice.creditTotal = Math.round((invoice.creditTotal + body.amount) * 100) / 100;
      invoice.remainingCredit = Math.round((invoice.amount - invoice.creditTotal) * 100) / 100;
      invoice.status = invoice.remainingCredit === 0 ? 'Credited' : 'PartiallyCredited';
      return HttpResponse.json(note, { status: 201 });
    }),

    // ---------- Notification ----------
    http.get(`${API}/notifications`, async ({ request }) => {
      await latency();
      const who = authorize(request, ['admin', 'customer']);
      if (who instanceof Response) return who;
      const shipmentId = new URL(request.url).searchParams.get('shipmentId');
      const visible = db.notifications
        .filter((item) => isAdmin(who) || item.customerSubject === who.sub)
        .filter((item) => !shipmentId || item.shipmentId === shipmentId);
      return HttpResponse.json(pageOf(visible, request));
    }),
    http.get(`${API}/notifications/:notificationId`, async ({ request, params }) => {
      await latency();
      const who = authorize(request, ['admin', 'customer']);
      if (who instanceof Response) return who;
      const notification = db.notifications.find((item) => item.id === params['notificationId']);
      if (!notification || (!isAdmin(who) && notification.customerSubject !== who.sub))
        return problem(404, 'Notification not found');
      return HttpResponse.json(notification);
    }),

    // ---------- Reporting ----------
    http.get(`${API}/reports/kpis`, async ({ request }) => {
      await latency();
      const who = authorize(request, STAFF);
      if (who instanceof Response) return who;
      const url = new URL(request.url);
      const from = url.searchParams.get('from') ?? today(-30);
      const to = url.searchParams.get('to') ?? today();
      if (from > to)
        return problem(400, 'Validation failed', { From: ["'From' must be before 'To'."] });
      const inRange = db.shipments.filter(
        (item) => item.createdAtUtc.slice(0, 10) >= from && item.createdAtUtc.slice(0, 10) <= to,
      );
      const statusOf = (shipmentId: string) =>
        db.tracking.get(shipmentId)?.at(-1)?.status ?? 'Created';
      const delivered = inRange.filter((item) => statusOf(item.id) === 'Delivered').length;
      const daily = new Map<string, number>();
      inRange.forEach((item) =>
        daily.set(
          item.createdAtUtc.slice(0, 10),
          (daily.get(item.createdAtUtc.slice(0, 10)) ?? 0) + 1,
        ),
      );
      const report: KpiReport = {
        from,
        to,
        generatedAtUtc: new Date().toISOString(),
        totalShipments: inRange.length,
        deliveredShipments: delivered,
        deliveryRatePercent: inRange.length
          ? Math.round((delivered / inRange.length) * 10_000) / 100
          : null,
        averageDeliveryHours: delivered ? 52.5 : null,
        deliveryDurationSamples: delivered,
        averageTransitHours: inRange.length ? 18.25 : null,
        transitDurationSamples: inRange.length,
        dailyVolumes: [...daily.entries()]
          .sort(([a], [b]) => a.localeCompare(b))
          .map(([date, shipments]) => ({ date, shipments })),
        statusVolumes: PARCEL_STATUSES.map((status) => ({
          status,
          shipments: inRange.filter((item) => statusOf(item.id) === status).length,
        })).filter((item) => item.shipments > 0),
      };
      return HttpResponse.json(report);
    }),
  ];
}
