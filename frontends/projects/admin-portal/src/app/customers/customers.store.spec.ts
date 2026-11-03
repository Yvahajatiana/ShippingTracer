import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { profile, settle } from '../testing/fixtures';
import { CustomersStore } from './customers.store';

describe('CustomersStore', () => {
  let store: InstanceType<typeof CustomersStore>;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideParcelFlowTesting({ subject: 'admin-david' }),
        provideRouter([]),
        CustomersStore,
      ],
    });
    store = TestBed.inject(CustomersStore);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('pages by offset and detects a next page with one extra item', async () => {
    store.loadPage(1);
    await settle();
    backend
      .expectOne('/api/v1/customers?offset=0&limit=21')
      .flush(Array.from({ length: 21 }, (_, i) => profile(i)));
    expect(store.items().length).toBe(20);
    expect(store.hasNext()).toBe(true);
    expect(store.hasPrevious()).toBe(false);
    expect(store.totalKnown()).toBe(false);

    store.nextPage();
    await settle();
    backend.expectOne('/api/v1/customers?offset=20&limit=21').flush([profile(20), profile(21)]);
    expect(store.page()).toBe(2);
    expect(store.items().map((item) => item.id)).toEqual(['p20', 'p21']);
    expect(store.hasNext()).toBe(false);
    expect(store.hasPrevious()).toBe(true);
  });

  it('keeps the error and clears it on reload', async () => {
    store.loadPage(1);
    await settle();
    backend
      .expectOne('/api/v1/customers?offset=0&limit=21')
      .flush({}, { status: 403, statusText: 'Forbidden' });
    expect(store.error()).toMatchObject({ status: 403 });

    store.reload();
    expect(store.error()).toBeNull();
    expect(store.loading()).toBe(true);
    await settle();
    backend.expectOne('/api/v1/customers?offset=0&limit=21').flush([]);
    expect(store.items()).toEqual([]);
    expect(store.loading()).toBe(false);
  });
});
