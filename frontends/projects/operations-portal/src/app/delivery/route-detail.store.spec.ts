import { HttpRequest } from '@angular/common/http';
import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { DeliveryRoute } from '@parcelflow/shared';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { DRIVERS, page, route, settle, shipment, stop } from '../testing/fixtures';
import { AVAILABLE_PARCELS_SIZE, RouteDetailStore } from './route-detail.store';

const ROUTE_URL = '/api/v1/delivery/routes/route-1';
const AVAILABLE_URL = '/api/v1/delivery/parcels/available';

describe('RouteDetailStore', () => {
  let backend: HttpTestingController;
  let store: InstanceType<typeof RouteDetailStore>;

  /** Waits until a matching request reaches the backend (token and file reads are async). */
  const next = (match: string | ((request: HttpRequest<unknown>) => boolean)) =>
    vi.waitFor(() => backend.expectOne(match) as TestRequest);

  /** Answers every pending shipment lookup used to label stops. */
  function flushShipments(): void {
    for (const request of backend.match((req) => req.url.startsWith('/api/v1/shipments/'))) {
      const id = request.request.url.split('/').pop()!;
      request.flush(shipment(id, `PF${id.toUpperCase().replace(/-/g, '')}FR`));
    }
  }

  async function load(
    current: DeliveryRoute,
    parcels = [{ shipmentId: 'sh-9', trackingNumber: 'PF9FR' }],
  ) {
    const done = store.load('route-1');
    (await next(ROUTE_URL)).flush(current);
    (await next((req) => req.url === '/api/v1/delivery/drivers')).flush(page(DRIVERS));
    if (current.status === 'Planned') {
      (await next((req) => req.url === '/api/v1/delivery/parcels/available')).flush(page(parcels));
    }
    await done;
    await settle();
    flushShipments();
    await settle();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideParcelFlowTesting({ subject: 'operator-chloe' }),
        provideRouter([]),
        RouteDetailStore,
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    store = TestBed.inject(RouteDetailStore);
  });

  afterEach(() => backend.verify());

  it('loads the route, active drivers, available parcels and stop labels', async () => {
    await load(route({ stops: [stop({ shipmentId: 'sh-1' })] }));

    expect(store.isPlanned()).toBe(true);
    expect(store.activeDrivers().map((driver) => driver.subject)).toEqual(['driver-emma']);
    expect(store.parcels()).toEqual([{ shipmentId: 'sh-9', trackingNumber: 'PF9FR' }]);
    expect(store.stops()[0]).toMatchObject({
      trackingNumber: 'PFSH1FR',
      recipientName: 'Bob Durand',
    });
    expect(store.canStart()).toBe(false);
  });

  it('offers active drivers from every page of the driver list', async () => {
    const done = store.load('route-1');
    (await next(ROUTE_URL)).flush(route({ status: 'InProgress' }));
    // Page 1 is full of inactive drivers; the only active one is on page 2.
    const inactive = Array.from({ length: 100 }, (_, index) => ({
      subject: `driver-a${String(index).padStart(3, '0')}`,
      displayName: `Inactive ${index}`,
      isActive: false,
    }));
    const first = await next(
      (req) =>
        req.url === '/api/v1/delivery/drivers' &&
        req.params.get('page') === '1' &&
        req.params.get('pageSize') === '100',
    );
    first.flush(page(inactive, 1, 100, 101));
    (
      await next((req) => req.url === '/api/v1/delivery/drivers' && req.params.get('page') === '2')
    ).flush(page([DRIVERS[0]], 2, 100, 101));
    await done;
    await settle();
    flushShipments();

    expect(store.drivers()).toHaveLength(101);
    expect(store.activeDrivers().map((driver) => driver.subject)).toEqual(['driver-emma']);
  });

  it('exposes the load error', async () => {
    const done = store.load('route-1');
    (await next(ROUTE_URL)).flush({}, { status: 404, statusText: 'Not Found' });
    (await next((req) => req.url === '/api/v1/delivery/drivers')).flush(page(DRIVERS));
    await done;
    expect(store.error()).toMatchObject({ status: 404 });
    expect(store.route()).toBeNull();
  });

  it('assigns a driver', async () => {
    await load(route());
    const result = store.assignDriver('driver-emma');
    expect(store.busy()).toBe('driver');
    const request = await next(`${ROUTE_URL}/driver`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ driverSubject: 'driver-emma' });
    request.flush(route({ driverSubject: 'driver-emma' }));

    await expect(result).resolves.toBe(true);
    expect(store.busy()).toBeNull();
    expect(store.driverName()).toBe('Emma Roux');
  });

  it('adds and removes a stop, refreshing available parcels', async () => {
    await load(route({ driverSubject: 'driver-emma' }));

    const added = store.addStop('sh-9');
    const add = await next(`${ROUTE_URL}/stops`);
    expect(add.request.method).toBe('POST');
    expect(add.request.body).toEqual({ shipmentId: 'sh-9' });
    add.flush(route({ driverSubject: 'driver-emma', stops: [stop({ shipmentId: 'sh-9' })] }));
    (await next((req) => req.url === '/api/v1/delivery/parcels/available')).flush(page([]));
    await expect(added).resolves.toBe(true);
    await settle();
    flushShipments();
    expect(store.canStart()).toBe(true);

    const removed = store.removeStop('sh-9');
    const remove = await next(`${ROUTE_URL}/stops/sh-9`);
    expect(remove.request.method).toBe('DELETE');
    remove.flush(null, { status: 204, statusText: 'No Content' });
    // 204 has no body: the route is reloaded.
    (await next(ROUTE_URL)).flush(route({ driverSubject: 'driver-emma' }));
    (await next((req) => req.url === '/api/v1/delivery/parcels/available')).flush(
      page([{ shipmentId: 'sh-9', trackingNumber: 'PF9FR' }]),
    );
    await expect(removed).resolves.toBe(true);
    expect(store.stops()).toEqual([]);
    expect(store.parcels()).toHaveLength(1);
  });

  it('pages through every available parcel using the reported total', async () => {
    const parcel = (index: number) => ({
      shipmentId: `sh-${index}`,
      trackingNumber: `PF${index}FR`,
    });
    const done = store.load('route-1');
    (await next(ROUTE_URL)).flush(route());
    (await next((req) => req.url === '/api/v1/delivery/drivers')).flush(page(DRIVERS));
    const first = await next((req) => req.url === AVAILABLE_URL);
    expect(first.request.params.get('page')).toBe('1');
    expect(first.request.params.get('pageSize')).toBe(String(AVAILABLE_PARCELS_SIZE));
    first.flush(page([parcel(1)], 1, AVAILABLE_PARCELS_SIZE, AVAILABLE_PARCELS_SIZE + 1));
    await done;

    expect(store.parcelsTotal()).toBe(AVAILABLE_PARCELS_SIZE + 1);
    expect(store.parcelsPageCount()).toBe(2);
    expect(store.parcelsHasPrevious()).toBe(false);
    expect(store.parcelsHasNext()).toBe(true);

    const forward = store.nextParcels();
    const second = await next((req) => req.url === AVAILABLE_URL);
    expect(second.request.params.get('page')).toBe('2');
    second.flush(page([parcel(101)], 2, AVAILABLE_PARCELS_SIZE, AVAILABLE_PARCELS_SIZE + 1));
    await forward;
    expect(store.parcelsPage()).toBe(2);
    expect(store.parcels()).toEqual([parcel(101)]);
    expect(store.parcelsHasNext()).toBe(false);

    // Adding the only parcel of the last page refreshes it, then steps back a page.
    const added = store.addStop('sh-101');
    (await next(`${ROUTE_URL}/stops`)).flush(route({ stops: [stop({ shipmentId: 'sh-101' })] }));
    const refresh = await next((req) => req.url === AVAILABLE_URL);
    expect(refresh.request.params.get('page')).toBe('2');
    refresh.flush(page([], 2, AVAILABLE_PARCELS_SIZE, AVAILABLE_PARCELS_SIZE));
    const back = await next((req) => req.url === AVAILABLE_URL);
    expect(back.request.params.get('page')).toBe('1');
    back.flush(page([parcel(1)], 1, AVAILABLE_PARCELS_SIZE, AVAILABLE_PARCELS_SIZE));
    await expect(added).resolves.toBe(true);
    await settle();
    flushShipments();

    expect(store.parcelsPage()).toBe(1);
    expect(store.parcels()).toEqual([parcel(1)]);
    expect(store.parcelsPageCount()).toBe(1);
    expect(store.parcelsHasNext()).toBe(false);

    const previous = store.previousParcels();
    await previous;
    // Already on the first page: no request is sent (verified by afterEach).
  });

  it('reloads the route when an action conflicts', async () => {
    await load(route({ driverSubject: 'driver-emma', stops: [stop()] }));
    const started = store.start();
    (await next(`${ROUTE_URL}/start`)).flush({}, { status: 409, statusText: 'Conflict' });
    (await next(ROUTE_URL)).flush(
      route({ driverSubject: 'driver-emma', status: 'InProgress', stops: [stop()] }),
    );
    await expect(started).resolves.toBe(false);
    expect(store.isInProgress()).toBe(true);
  });

  it('starts the route', async () => {
    await load(route({ driverSubject: 'driver-emma', stops: [stop()] }));
    const started = store.start();
    const request = await next(`${ROUTE_URL}/start`);
    expect(request.request.method).toBe('POST');
    request.flush(route({ driverSubject: 'driver-emma', status: 'InProgress', stops: [stop()] }));
    await expect(started).resolves.toBe(true);
    expect(store.isInProgress()).toBe(true);
    expect(store.parcels()).toEqual([]);
  });

  it('confirms a delivery with base64 proofs and reuses the operationId on retry', async () => {
    await load(route({ driverSubject: 'driver-emma', status: 'InProgress', stops: [stop()] }));
    const photo = new Blob([new Uint8Array([1, 2, 3])], { type: 'image/png' });
    const proof = {
      operationId: 'op-1',
      recipientName: '  Jeanne Martin ',
      signaturePng: 'c2lnbmF0dXJl',
      photo,
    };

    // First attempt: the network drops the request.
    const first = store.confirmDelivery('sh-1', proof);
    const attempt = await next(`${ROUTE_URL}/stops/sh-1/delivery`);
    expect(attempt.request.body).toEqual({
      operationId: 'op-1',
      recipientName: 'Jeanne Martin',
      signaturePng: 'c2lnbmF0dXJl',
      photo: 'AQID',
    });
    attempt.error(new ProgressEvent('error'));
    const failure = await first;
    expect(failure.ok).toBe(false);

    // Retry with the same idempotency key.
    const second = store.confirmDelivery('sh-1', proof);
    const retry = await next(`${ROUTE_URL}/stops/sh-1/delivery`);
    expect(retry.request.body).toMatchObject({ operationId: 'op-1' });
    retry.flush({});
    (await next(ROUTE_URL)).flush(
      route({
        driverSubject: 'driver-emma',
        status: 'Completed',
        stops: [stop({ status: 'Delivered', completedAtUtc: '2026-10-23T10:00:00Z' })],
      }),
    );
    await expect(second).resolves.toEqual({ ok: true });
    expect(store.progress()).toMatchObject({ delivered: 1, pending: 0, percent: 100 });
  });

  it('sends a signature-only delivery without photo', async () => {
    await load(route({ driverSubject: 'driver-emma', status: 'InProgress', stops: [stop()] }));
    const result = store.confirmDelivery('sh-1', {
      operationId: 'op-2',
      recipientName: 'Jeanne',
      signaturePng: 'AAAA',
      photo: null,
    });
    const request = await next(`${ROUTE_URL}/stops/sh-1/delivery`);
    expect(request.request.body).toMatchObject({ signaturePng: 'AAAA', photo: null });
    request.flush({}, { status: 400, statusText: 'Bad Request' });
    const outcome = await result;
    expect(outcome.ok).toBe(false);
  });

  it('reports a failed delivery with its reason', async () => {
    await load(route({ driverSubject: 'driver-emma', status: 'InProgress', stops: [stop()] }));
    const result = store.failDelivery('sh-1', 'op-3', 'RecipientAbsent');
    expect(store.busy()).toBe('fail');
    const request = await next(`${ROUTE_URL}/stops/sh-1/failure`);
    expect(request.request.body).toEqual({ operationId: 'op-3', reason: 'RecipientAbsent' });
    request.flush({});
    (await next(ROUTE_URL)).flush(
      route({
        driverSubject: 'driver-emma',
        status: 'Completed',
        stops: [stop({ status: 'Failed', failureReason: 'RecipientAbsent' })],
      }),
    );
    await expect(result).resolves.toEqual({ ok: true });
    expect(store.stops()[0].failureReason).toBe('RecipientAbsent');
  });
});
