import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { StockParcel } from '@parcelflow/shared';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { settle, warehouse } from '../testing/fixtures';
import { STOCK_PAGE_SIZE, WarehouseDetailStore, scanErrorKey } from './warehouses.store';

const SCAN_URL = '/api/v1/warehouses/scans';
const STOCK_URL = '/api/v1/warehouses/wh-1/stock';

function stockPage(pageNumber: number, total: number) {
  const items: StockParcel[] = Array.from(
    { length: Math.min(STOCK_PAGE_SIZE, total - (pageNumber - 1) * STOCK_PAGE_SIZE) },
    (_, index) => ({
      shipmentId: `sh-${pageNumber}-${index}`,
      trackingNumber: `PF${pageNumber}${String(index).padStart(4, '0')}FR`,
      lastScanAtUtc: '2026-10-23T08:00:00Z',
    }),
  );
  return { warehouseId: 'wh-1', page: pageNumber, pageSize: STOCK_PAGE_SIZE, total, items };
}

describe('WarehouseDetailStore', () => {
  let backend: HttpTestingController;
  let store: InstanceType<typeof WarehouseDetailStore>;

  async function load(active = true, total = 3) {
    store.load('wh-1');
    await settle();
    backend.expectOne('/api/v1/warehouses/wh-1').flush(warehouse({ isActive: active }));
    backend.expectOne((req) => req.url === STOCK_URL).flush(stockPage(1, total));
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideParcelFlowTesting({ subject: 'operator-chloe' }),
        provideRouter([]),
        WarehouseDetailStore,
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    store = TestBed.inject(WarehouseDetailStore);
  });

  afterEach(() => backend.verify());

  it('loads the warehouse and paginates its stock', async () => {
    await load(true, 45);
    expect(store.warehouse()?.code).toBe('PAR-01');
    expect(store.items()).toHaveLength(STOCK_PAGE_SIZE);
    expect(store.pageCount()).toBe(3);
    expect(store.hasMore()).toBe(true);
    expect(store.hasPrevious()).toBe(false);

    store.next();
    await settle();
    const second = backend.expectOne((req) => req.url === STOCK_URL);
    expect(second.request.params.get('page')).toBe('2');
    expect(second.request.params.get('pageSize')).toBe(String(STOCK_PAGE_SIZE));
    second.flush(stockPage(2, 45));
    expect(store.page()).toBe(2);

    store.next();
    await settle();
    backend.expectOne((req) => req.url === STOCK_URL).flush(stockPage(3, 45));
    expect(store.items()).toHaveLength(5);
    expect(store.hasMore()).toBe(false);

    // No request beyond the last page.
    store.next();
    store.previous();
    await settle();
    const back = backend.expectOne((req) => req.url === STOCK_URL);
    expect(back.request.params.get('page')).toBe('2');
    back.flush(stockPage(2, 45));
  });

  it('sends each scan with its own scanId and refreshes the stock on success', async () => {
    await load();
    const first = store.scan(' pf00240000fr ');
    const second = store.scan('PF00240001FR');
    await settle();
    const [a, b] = backend.match(SCAN_URL);
    expect(a.request.body).toMatchObject({
      warehouseId: 'wh-1',
      trackingNumber: 'PF00240000FR',
      direction: 'Entry',
    });
    expect(a.request.body.scanId).toMatch(/^[0-9a-f-]{36}$/);
    expect(b.request.body.scanId).not.toBe(a.request.body.scanId);
    expect(store.pendingScans()).toBe(2);

    a.flush({ ...a.request.body, shipmentId: 'sh-1', occurredAtUtc: '2026-10-23T09:00:00Z' });
    await expect(first).resolves.toBe('success');
    await settle();
    backend.expectOne((req) => req.url === STOCK_URL).flush(stockPage(1, 4));

    // An entry for a parcel already stored is a conflict.
    b.flush({ title: 'Parcel already in a warehouse' }, { status: 409, statusText: 'Conflict' });
    await expect(second).resolves.toBe('error');

    const [latest, earliest] = store.scans();
    expect(latest).toMatchObject({
      trackingNumber: 'PF00240001FR',
      outcome: 'error',
      errorKey: 'warehouses.scan.errors.alreadyStored',
      retryable: false,
    });
    expect(earliest).toMatchObject({ outcome: 'success', at: '2026-10-23T09:00:00Z' });
  });

  it('uses the selected direction and explains exit conflicts', async () => {
    await load();
    store.setDirection('Exit');
    const result = store.scan('PF00240000FR');
    await settle();
    const request = backend.expectOne(SCAN_URL);
    expect(request.request.body.direction).toBe('Exit');
    request.flush({}, { status: 409, statusText: 'Conflict' });
    await expect(result).resolves.toBe('error');
    expect(store.scans()[0].errorKey).toBe('warehouses.scan.errors.notInWarehouse');
  });

  it('resends a scan that failed on the network with the same scanId', async () => {
    await load();
    const first = store.scan('PF00240000FR');
    await settle();
    const lost = backend.expectOne(SCAN_URL);
    const scanId = lost.request.body.scanId;
    lost.error(new ProgressEvent('error'));
    await first;
    expect(store.scans()[0]).toMatchObject({ outcome: 'error', retryable: true });

    const retried = store.retryScan(scanId);
    await settle();
    const retry = backend.expectOne(SCAN_URL);
    expect(retry.request.body.scanId).toBe(scanId);
    retry.flush({
      ...retry.request.body,
      shipmentId: 'sh-1',
      occurredAtUtc: '2026-10-23T09:00:00Z',
    });
    await expect(retried).resolves.toBe('success');
    await settle();
    backend.expectOne((req) => req.url === STOCK_URL).flush(stockPage(1, 4));
    expect(store.scans()).toHaveLength(1);
  });

  it('disables scanning for an inactive warehouse', async () => {
    await load(false);
    expect(store.canScan()).toBe(false);
  });

  it('maps scan failures to messages', () => {
    expect(scanErrorKey(404, 'Entry', 'x')).toBe('warehouses.scan.errors.notFound');
    expect(scanErrorKey(400, 'Entry', 'x')).toBe('warehouses.scan.errors.invalid');
    expect(scanErrorKey(503, 'Exit', 'errors.http.5xx')).toBe('errors.http.5xx');
  });
});
