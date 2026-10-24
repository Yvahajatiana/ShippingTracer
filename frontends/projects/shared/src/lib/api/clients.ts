import { HttpClient, HttpContext, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ANONYMOUS } from '../http/api.interceptor';
import { ApiUrl, queryParams } from './api-url';
import {
  Address,
  AddressBody,
  AvailableParcel,
  Company,
  CompanyBody,
  ConfirmDeliveryBody,
  CreateWarehouseBody,
  CreditNote,
  CreditReason,
  DELIVERY_ZONES,
  DeliveryAttempt,
  DeliveryRoute,
  DeliveryZone,
  Driver,
  FailDeliveryBody,
  Guid,
  InternalTracking,
  Invoice,
  IsoDate,
  KpiReport,
  MockPaymentOutcome,
  NotificationRecord,
  Page,
  Payment,
  ProfileBody,
  Profile,
  ProofImageKind,
  PublicTracking,
  Quote,
  SHIPMENT_STATUSES,
  Scan,
  ScanParcelBody,
  Shipment,
  ShipmentBody,
  ShipmentStatus,
  Tariff,
  TariffBody,
  UpdateWarehouseBody,
  Warehouse,
  WarehouseStock,
} from './models';

/** A downloaded file with the server-proposed name. */
export interface DownloadedFile {
  blob: Blob;
  fileName: string;
}

function toDownloadedFile(response: HttpResponse<Blob>, fallbackName: string): DownloadedFile {
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
  let proposed = fallbackName;
  try {
    proposed = match ? decodeURIComponent(match[1]) : fallbackName;
  } catch {
    // Malformed percent-encoding: keep the fallback name.
  }
  // Never trust a server file name for path or control characters.
  const fileName = [...proposed]
    .map((char) => (char.charCodeAt(0) < 32 || '\\/<>:"|?*'.includes(char) ? '_' : char))
    .join('');
  return { blob: response.body ?? new Blob(), fileName };
}

@Injectable({ providedIn: 'root' })
export class CustomerApi {
  private readonly http = inject(HttpClient);
  private readonly url = inject(ApiUrl);

  me(): Observable<Profile> {
    return this.http.get<Profile>(this.url.to('customers', 'me'));
  }
  createProfile(body: ProfileBody): Observable<Profile> {
    return this.http.post<Profile>(this.url.to('customers'), body);
  }
  listProfiles(offset = 0, limit = 20): Observable<Profile[]> {
    return this.http.get<Profile[]>(this.url.to('customers'), {
      params: queryParams({ offset, limit }),
    });
  }
  getProfile(id: Guid): Observable<Profile> {
    return this.http.get<Profile>(this.url.to('customers', id));
  }
  updateProfile(id: Guid, body: ProfileBody): Observable<Profile> {
    return this.http.put<Profile>(this.url.to('customers', id), body);
  }
  assignCompany(id: Guid, companyId: Guid | null): Observable<Profile> {
    return this.http.put<Profile>(this.url.to('customers', id, 'company'), { companyId });
  }
  addAddress(id: Guid, body: AddressBody): Observable<Address> {
    return this.http.post<Address>(this.url.to('customers', id, 'addresses'), body);
  }
  updateAddress(id: Guid, addressId: Guid, body: AddressBody): Observable<Address> {
    return this.http.put<Address>(this.url.to('customers', id, 'addresses', addressId), body);
  }
  deleteAddress(id: Guid, addressId: Guid): Observable<void> {
    return this.http.delete<void>(this.url.to('customers', id, 'addresses', addressId));
  }
  listCompanies(): Observable<Company[]> {
    return this.http.get<Company[]>(this.url.to('companies'));
  }
  getCompany(id: Guid): Observable<Company> {
    return this.http.get<Company>(this.url.to('companies', id));
  }
  createCompany(body: CompanyBody): Observable<Company> {
    return this.http.post<Company>(this.url.to('companies'), body);
  }
  updateCompany(id: Guid, body: CompanyBody): Observable<Company> {
    return this.http.put<Company>(this.url.to('companies', id), body);
  }
}

/**
 * Shipment serializes DeliveryZone and ShipmentStatus as integers (issue #23).
 * Both forms are accepted so the client keeps working once names are emitted.
 */
export function normalizeZone(value: unknown): DeliveryZone {
  return typeof value === 'number' ? DELIVERY_ZONES[value] : (value as DeliveryZone);
}
export function normalizeShipmentStatus(value: unknown): ShipmentStatus {
  return typeof value === 'number' ? SHIPMENT_STATUSES[value] : (value as ShipmentStatus);
}
function normalizeShipment(shipment: Shipment): Shipment {
  return {
    ...shipment,
    zone: normalizeZone(shipment.zone),
    status: normalizeShipmentStatus(shipment.status),
  };
}

@Injectable({ providedIn: 'root' })
export class ShipmentApi {
  private readonly http = inject(HttpClient);
  private readonly url = inject(ApiUrl);

  quote(body: ShipmentBody): Observable<Quote> {
    return this.http
      .post<Quote>(this.url.to('shipments', 'quote'), body)
      .pipe(map((quote) => ({ ...quote, zone: normalizeZone(quote.zone) })));
  }
  create(body: ShipmentBody): Observable<Shipment> {
    return this.http.post<Shipment>(this.url.to('shipments'), body).pipe(map(normalizeShipment));
  }
  list(offset = 0, limit = 20): Observable<Shipment[]> {
    return this.http
      .get<Shipment[]>(this.url.to('shipments'), { params: queryParams({ offset, limit }) })
      .pipe(map((items) => items.map(normalizeShipment)));
  }
  get(id: Guid): Observable<Shipment> {
    return this.http.get<Shipment>(this.url.to('shipments', id)).pipe(map(normalizeShipment));
  }
  label(id: Guid): Observable<DownloadedFile> {
    return this.http
      .get(this.url.to('shipments', id, 'label'), { observe: 'response', responseType: 'blob' })
      .pipe(map((response) => toDownloadedFile(response, 'label.pdf')));
  }
  tariffs(): Observable<Tariff[]> {
    return this.http
      .get<Tariff[]>(this.url.to('tariffs'))
      .pipe(
        map((items) => items.map((tariff) => ({ ...tariff, zone: normalizeZone(tariff.zone) }))),
      );
  }
  updateTariff(zone: DeliveryZone, body: TariffBody): Observable<Tariff> {
    return this.http
      .put<Tariff>(this.url.to('tariffs', zone), body)
      .pipe(map((tariff) => ({ ...tariff, zone: normalizeZone(tariff.zone) })));
  }
}

@Injectable({ providedIn: 'root' })
export class TrackingApi {
  private readonly http = inject(HttpClient);
  private readonly url = inject(ApiUrl);

  /** Anonymous public tracking by tracking number. */
  publicTracking(trackingNumber: string): Observable<PublicTracking> {
    return this.http.get<PublicTracking>(
      this.url.to('tracking', trackingNumber.trim().toUpperCase()),
      {
        context: new HttpContext().set(ANONYMOUS, true),
      },
    );
  }
  /** Staff-only full history by shipment id. */
  shipmentTracking(shipmentId: Guid): Observable<InternalTracking> {
    return this.http.get<InternalTracking>(this.url.to('tracking', 'shipments', shipmentId));
  }
}

@Injectable({ providedIn: 'root' })
export class WarehouseApi {
  private readonly http = inject(HttpClient);
  private readonly url = inject(ApiUrl);

  list(page = 1, pageSize = 50): Observable<Page<Warehouse>> {
    return this.http.get<Page<Warehouse>>(this.url.to('warehouses'), {
      params: queryParams({ page, pageSize }),
    });
  }
  get(id: Guid): Observable<Warehouse> {
    return this.http.get<Warehouse>(this.url.to('warehouses', id));
  }
  create(body: CreateWarehouseBody): Observable<Warehouse> {
    return this.http.post<Warehouse>(this.url.to('warehouses'), body);
  }
  update(id: Guid, body: UpdateWarehouseBody): Observable<Warehouse> {
    return this.http.put<Warehouse>(this.url.to('warehouses', id), body);
  }
  stock(id: Guid, page = 1, pageSize = 50): Observable<WarehouseStock> {
    return this.http.get<WarehouseStock>(this.url.to('warehouses', id, 'stock'), {
      params: queryParams({ page, pageSize }),
    });
  }
  scan(body: ScanParcelBody): Observable<Scan> {
    return this.http.post<Scan>(this.url.to('warehouses', 'scans'), body);
  }
}

@Injectable({ providedIn: 'root' })
export class DeliveryApi {
  private readonly http = inject(HttpClient);
  private readonly url = inject(ApiUrl);

  drivers(page = 1, pageSize = 50): Observable<Page<Driver>> {
    return this.http.get<Page<Driver>>(this.url.to('delivery', 'drivers'), {
      params: queryParams({ page, pageSize }),
    });
  }
  createDriver(subject: string, displayName: string): Observable<Driver> {
    return this.http.post<Driver>(this.url.to('delivery', 'drivers'), { subject, displayName });
  }
  updateDriver(subject: string, displayName: string, isActive: boolean): Observable<Driver> {
    return this.http.put<Driver>(this.url.to('delivery', 'drivers', subject), {
      displayName,
      isActive,
    });
  }
  routes(page = 1, pageSize = 50): Observable<Page<DeliveryRoute>> {
    return this.http.get<Page<DeliveryRoute>>(this.url.to('delivery', 'routes'), {
      params: queryParams({ page, pageSize }),
    });
  }
  route(id: Guid): Observable<DeliveryRoute> {
    return this.http.get<DeliveryRoute>(this.url.to('delivery', 'routes', id));
  }
  createRoute(name: string, serviceDate: IsoDate): Observable<DeliveryRoute> {
    return this.http.post<DeliveryRoute>(this.url.to('delivery', 'routes'), { name, serviceDate });
  }
  assignDriver(routeId: Guid, driverSubject: string): Observable<DeliveryRoute> {
    return this.http.put<DeliveryRoute>(this.url.to('delivery', 'routes', routeId, 'driver'), {
      driverSubject,
    });
  }
  addStop(routeId: Guid, shipmentId: Guid): Observable<DeliveryRoute> {
    return this.http.post<DeliveryRoute>(this.url.to('delivery', 'routes', routeId, 'stops'), {
      shipmentId,
    });
  }
  removeStop(routeId: Guid, shipmentId: Guid): Observable<void> {
    return this.http.delete<void>(this.url.to('delivery', 'routes', routeId, 'stops', shipmentId));
  }
  startRoute(routeId: Guid): Observable<DeliveryRoute> {
    return this.http.post<DeliveryRoute>(this.url.to('delivery', 'routes', routeId, 'start'), {});
  }
  confirmDelivery(
    routeId: Guid,
    shipmentId: Guid,
    body: ConfirmDeliveryBody,
  ): Observable<DeliveryAttempt> {
    return this.http.post<DeliveryAttempt>(
      this.url.to('delivery', 'routes', routeId, 'stops', shipmentId, 'delivery'),
      body,
    );
  }
  failDelivery(
    routeId: Guid,
    shipmentId: Guid,
    body: FailDeliveryBody,
  ): Observable<DeliveryAttempt> {
    return this.http.post<DeliveryAttempt>(
      this.url.to('delivery', 'routes', routeId, 'stops', shipmentId, 'failure'),
      body,
    );
  }
  proof(routeId: Guid, shipmentId: Guid): Observable<DeliveryAttempt> {
    return this.http.get<DeliveryAttempt>(
      this.url.to('delivery', 'routes', routeId, 'stops', shipmentId, 'proof'),
    );
  }
  proofImage(routeId: Guid, shipmentId: Guid, kind: ProofImageKind): Observable<Blob> {
    return this.http.get(
      this.url.to('delivery', 'routes', routeId, 'stops', shipmentId, 'proof', kind.toLowerCase()),
      {
        responseType: 'blob',
      },
    );
  }
  availableParcels(page = 1, pageSize = 50): Observable<Page<AvailableParcel>> {
    return this.http.get<Page<AvailableParcel>>(this.url.to('delivery', 'parcels', 'available'), {
      params: queryParams({ page, pageSize }),
    });
  }
}

@Injectable({ providedIn: 'root' })
export class BillingApi {
  private readonly http = inject(HttpClient);
  private readonly url = inject(ApiUrl);

  invoices(page = 1, pageSize = 50): Observable<Page<Invoice>> {
    return this.http.get<Page<Invoice>>(this.url.to('billing', 'invoices'), {
      params: queryParams({ page, pageSize }),
    });
  }
  invoice(id: Guid): Observable<Invoice> {
    return this.http.get<Invoice>(this.url.to('billing', 'invoices', id));
  }
  payment(invoiceId: Guid): Observable<Payment> {
    return this.http.get<Payment>(this.url.to('billing', 'invoices', invoiceId, 'payment'));
  }
  pay(invoiceId: Guid, operationId: Guid, outcome: MockPaymentOutcome): Observable<Payment> {
    return this.http.post<Payment>(this.url.to('billing', 'invoices', invoiceId, 'payments'), {
      operationId,
      outcome,
    });
  }
  creditNotes(invoiceId: Guid, page = 1, pageSize = 50): Observable<Page<CreditNote>> {
    return this.http.get<Page<CreditNote>>(
      this.url.to('billing', 'invoices', invoiceId, 'credit-notes'),
      {
        params: queryParams({ page, pageSize }),
      },
    );
  }
  issueCreditNote(
    invoiceId: Guid,
    operationId: Guid,
    amount: number,
    reason: CreditReason,
  ): Observable<CreditNote> {
    return this.http.post<CreditNote>(
      this.url.to('billing', 'invoices', invoiceId, 'credit-notes'),
      {
        operationId,
        amount,
        reason,
      },
    );
  }
}

@Injectable({ providedIn: 'root' })
export class NotificationApi {
  private readonly http = inject(HttpClient);
  private readonly url = inject(ApiUrl);

  list(
    shipmentId: Guid | null = null,
    page = 1,
    pageSize = 50,
  ): Observable<Page<NotificationRecord>> {
    return this.http.get<Page<NotificationRecord>>(this.url.to('notifications'), {
      params: queryParams({ shipmentId, page, pageSize }),
    });
  }
  get(id: Guid): Observable<NotificationRecord> {
    return this.http.get<NotificationRecord>(this.url.to('notifications', id));
  }
}

/** Longest KPI cohort accepted by Reporting (`GetKpisValidator`), in days. */
export const KPI_MAX_RANGE_DAYS = 366;

@Injectable({ providedIn: 'root' })
export class ReportingApi {
  private readonly http = inject(HttpClient);
  private readonly url = inject(ApiUrl);

  /**
   * KPIs for the UTC cohort `[from, to)`: `to` is EXCLUSIVE and the span must be 1 to
   * `KPI_MAX_RANGE_DAYS` days. Callers holding an inclusive end date pass `addDays(end, 1)`.
   */
  kpis(from: IsoDate, to: IsoDate): Observable<KpiReport> {
    return this.http.get<KpiReport>(this.url.to('reports', 'kpis'), {
      params: queryParams({ from, to }),
    });
  }
}
