import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { DRIVERS, page, route, settle } from '../testing/fixtures';
import { RouteListStore } from './delivery-lists.store';

describe('RouteListStore', () => {
  let backend: HttpTestingController;
  let store: InstanceType<typeof RouteListStore>;

  async function init() {
    store = TestBed.inject(RouteListStore);
    await settle();
    backend
      .expectOne((req) => req.url === '/api/v1/delivery/routes')
      .flush(page([route({ driverSubject: 'driver-emma' })], 1, 20, 1));
    backend.expectOne((req) => req.url === '/api/v1/delivery/drivers').flush(page(DRIVERS));
    await settle();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideParcelFlowTesting({ subject: 'operator-chloe' }),
        provideRouter([]),
        RouteListStore,
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('loads the first page and resolves driver names', async () => {
    await init();
    expect(store.items()).toHaveLength(1);
    expect(store.pageCount()).toBe(1);
    expect(store.hasMore()).toBe(false);
    expect(store.driverNames().get('driver-emma')).toBe('Emma Roux');
  });

  it('creates a route and reloads the first page', async () => {
    await init();
    const created = store.create('  Lyon Est ', '2030-01-15');
    expect(store.creating()).toBe(true);
    await settle();
    const request = backend.expectOne('/api/v1/delivery/routes');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ name: 'Lyon Est', serviceDate: '2030-01-15' });
    request.flush(route({ id: 'route-2', name: 'Lyon Est', serviceDate: '2030-01-15' }));

    await expect(created).resolves.toMatchObject({ id: 'route-2' });
    expect(store.creating()).toBe(false);
    await settle();
    backend
      .expectOne((req) => req.url === '/api/v1/delivery/routes' && req.method === 'GET')
      .flush(page([route({ id: 'route-2' }), route()], 1, 20, 2));
    expect(store.items()).toHaveLength(2);
  });

  it('rethrows validation errors so the form can show them', async () => {
    await init();
    const created = store.create('Past', '2020-01-01');
    await settle();
    backend
      .expectOne('/api/v1/delivery/routes')
      .flush(
        { status: 400, errors: { ServiceDate: ['Service date cannot be in the past.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
    await expect(created).rejects.toMatchObject({
      status: 400,
      fieldErrors: { serviceDate: ['Service date cannot be in the past.'] },
    });
    expect(store.creating()).toBe(false);
  });
});
