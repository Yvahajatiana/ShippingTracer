import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import {
  aNotification,
  aPage,
  aTracking,
  aWireShipment,
  anInvoice,
  expectRequest,
  flushProblem,
  settle,
} from '../../testing/fixtures';
import { ShipmentDetailStore } from './shipment-detail.store';

describe('ShipmentDetailStore', () => {
  let store: InstanceType<typeof ShipmentDetailStore>;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideParcelFlowTesting({ subject: 'customer-alice' }), ShipmentDetailStore],
    });
    store = TestBed.inject(ShipmentDetailStore);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  async function loadShipment() {
    store.load('shipment-0');
    (await expectRequest(backend, '/api/v1/shipments/shipment-0')).flush(aWireShipment(0));
    await settle();
  }

  it('loads the shipment then its tracking, invoice and notifications', async () => {
    await loadShipment();
    expect(store.shipment()).toMatchObject({ zone: 'Europe', status: 'AwaitingPayment' });
    expect(store.trackingLoading()).toBe(true);

    backend.expectOne('/api/v1/tracking/PF00240000FR').flush(aTracking());
    // The invoice is fetched by id (invoice id == shipment id), never by scanning a page.
    backend.expectOne('/api/v1/billing/invoices/shipment-0').flush(anInvoice({ id: 'shipment-0' }));
    backend.expectNone((req) => req.url === '/api/v1/billing/invoices');
    const notifications = backend.expectOne((req) => req.url === '/api/v1/notifications');
    expect(notifications.request.params.get('shipmentId')).toBe('shipment-0');
    notifications.flush(aPage([aNotification(0)]));

    expect(store.timeline()[0].status).toBe('InTransit');
    expect(store.invoice()?.id).toBe('shipment-0');
    expect(store.notifications()).toHaveLength(1);
    expect(store.trackingLoading() || store.invoiceLoading() || store.notificationsLoading()).toBe(
      false,
    );
  });

  it('keeps the shipment visible when secondary sections fail', async () => {
    await loadShipment();
    flushProblem(backend.expectOne('/api/v1/tracking/PF00240000FR'), 404);
    flushProblem(backend.expectOne('/api/v1/billing/invoices/shipment-0'), 503);
    flushProblem(
      backend.expectOne((req) => req.url === '/api/v1/notifications'),
      503,
    );

    expect(store.shipment()).not.toBeNull();
    expect(store.error()).toBeNull();
    expect(store.trackingError()?.status).toBe(404);
    expect(store.invoice()).toBeNull();
    expect(store.notificationsError()?.status).toBe(503);
  });

  it('treats a missing invoice as no invoice', async () => {
    await loadShipment();
    backend.expectOne('/api/v1/tracking/PF00240000FR').flush(aTracking());
    flushProblem(backend.expectOne('/api/v1/billing/invoices/shipment-0'), 404);
    backend.expectOne((req) => req.url === '/api/v1/notifications').flush(aPage([]));

    expect(store.invoice()).toBeNull();
    expect(store.invoiceLoading()).toBe(false);
    expect(store.error()).toBeNull();
    expect(store.shipment()).not.toBeNull();
  });

  it('reports a missing shipment', async () => {
    store.load('nope');
    flushProblem(await expectRequest(backend, '/api/v1/shipments/nope'), 404);
    expect(store.error()?.status).toBe(404);
    expect(store.shipment()).toBeNull();
  });

  it('downloads the label as a file', async () => {
    await loadShipment();
    backend.expectOne('/api/v1/tracking/PF00240000FR').flush(aTracking());
    flushProblem(backend.expectOne('/api/v1/billing/invoices/shipment-0'), 404);
    backend.expectOne((req) => req.url === '/api/v1/notifications').flush(aPage([]));

    const createObjectURL = vi.fn(() => 'blob:label');
    const revokeObjectURL = vi.fn();
    Object.assign(URL, { createObjectURL, revokeObjectURL });
    const click = vi
      .spyOn(HTMLAnchorElement.prototype, 'click')
      .mockImplementation(() => undefined);

    const pending = store.downloadLabel();
    expect(store.downloading()).toBe(true);
    const request = await expectRequest(backend, '/api/v1/shipments/shipment-0/label');
    request.flush(new Blob(['%PDF']), {
      headers: { 'Content-Disposition': 'attachment; filename="PF00240000FR.pdf"' },
    });
    expect((await pending).ok).toBe(true);
    expect(store.downloading()).toBe(false);
    expect(createObjectURL).toHaveBeenCalled();
    expect(click).toHaveBeenCalled();
    click.mockRestore();
  });
});
