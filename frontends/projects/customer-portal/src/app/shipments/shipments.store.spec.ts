import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { aWireShipment, expectRequest, flushProblem } from '../../testing/fixtures';
import { SHIPMENTS_PAGE_SIZE, ShipmentsStore } from './shipments.store';

const wirePage = (count: number, start = 0) =>
  Array.from({ length: count }, (_, index) => aWireShipment(start + index));

describe('ShipmentsStore', () => {
  let store: InstanceType<typeof ShipmentsStore>;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideParcelFlowTesting({ subject: 'customer-alice' }), ShipmentsStore],
    });
    store = TestBed.inject(ShipmentsStore);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('requests one extra item to know whether a next page exists', async () => {
    store.loadPage(1);
    expect(store.loading()).toBe(true);

    const request = await expectRequest(backend, (req) => req.url === '/api/v1/shipments');
    expect(request.request.params.get('offset')).toBe('0');
    expect(request.request.params.get('limit')).toBe(String(SHIPMENTS_PAGE_SIZE + 1));
    request.flush(wirePage(SHIPMENTS_PAGE_SIZE + 1));

    expect(store.loading()).toBe(false);
    expect(store.items()).toHaveLength(SHIPMENTS_PAGE_SIZE);
    expect(store.items()[0]).toMatchObject({ zone: 'Europe', status: 'AwaitingPayment' });
    expect(store.hasNext()).toBe(true);
    expect(store.hasPrevious()).toBe(false);
    expect(store.total()).toBeNull();
  });

  it('pages forward and backward with offsets', async () => {
    store.loadPage(1);
    (await expectRequest(backend, (req) => req.url === '/api/v1/shipments')).flush(
      wirePage(SHIPMENTS_PAGE_SIZE + 1),
    );

    store.next();
    const second = await expectRequest(backend, (req) => req.url === '/api/v1/shipments');
    expect(second.request.params.get('offset')).toBe(String(SHIPMENTS_PAGE_SIZE));
    second.flush(wirePage(3, SHIPMENTS_PAGE_SIZE));
    expect(store.page()).toBe(2);
    expect(store.items()).toHaveLength(3);
    expect(store.hasNext()).toBe(false);

    // No further page: next() is a no-op.
    store.next();
    await expectNoRequest(backend);

    store.previous();
    const back = await expectRequest(backend, (req) => req.url === '/api/v1/shipments');
    expect(back.request.params.get('offset')).toBe('0');
    back.flush(wirePage(2));
    expect(store.page()).toBe(1);
  });

  it('reports an empty list once loaded', async () => {
    expect(store.empty()).toBe(false);
    store.loadPage(1);
    (await expectRequest(backend, (req) => req.url === '/api/v1/shipments')).flush([]);
    expect(store.empty()).toBe(true);
  });

  it('keeps the error and clears the items on failure', async () => {
    store.loadPage(1);
    flushProblem(await expectRequest(backend, (req) => req.url === '/api/v1/shipments'), 503);
    expect(store.loading()).toBe(false);
    expect(store.error()).toMatchObject({ status: 503, messageKey: 'errors.http.5xx' });
    expect(store.items()).toEqual([]);
    expect(store.empty()).toBe(false);

    store.reload();
    const retry = await expectRequest(backend, (req) => req.url === '/api/v1/shipments');
    retry.flush(wirePage(1));
    expect(store.error()).toBeNull();
    expect(store.items()).toHaveLength(1);
  });
});

async function expectNoRequest(backend: HttpTestingController) {
  await new Promise<void>((resolve) => setTimeout(resolve));
  backend.expectNone((req) => req.url === '/api/v1/shipments');
}
