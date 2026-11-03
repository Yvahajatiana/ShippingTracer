import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Company } from '@parcelflow/shared';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { settle } from '../testing/fixtures';
import { CompaniesStore } from './companies.store';

const acme: Company = { id: 'c1', name: 'Acme', registrationNumber: 'RCS-1', email: 'a@acme.test' };
const zeta: Company = { id: 'c2', name: 'zeta', registrationNumber: 'RCS-2', email: 'z@zeta.test' };

describe('CompaniesStore', () => {
  let store: InstanceType<typeof CompaniesStore>;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideParcelFlowTesting({ subject: 'admin-david' }),
        provideRouter([]),
        CompaniesStore,
      ],
    });
    store = TestBed.inject(CompaniesStore);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('loads companies sorted by name', async () => {
    store.load();
    expect(store.loading()).toBe(true);
    await settle();
    backend.expectOne('/api/v1/companies').flush([zeta, acme]);
    expect(store.loading()).toBe(false);
    expect(store.sorted().map((company) => company.id)).toEqual(['c1', 'c2']);
  });

  it('exposes load errors', async () => {
    store.load();
    await settle();
    backend
      .expectOne('/api/v1/companies')
      .flush({ title: 'Boom' }, { status: 503, statusText: 'Unavailable' });
    expect(store.loading()).toBe(false);
    expect(store.error()).toMatchObject({ status: 503, messageKey: 'errors.http.5xx' });
  });

  it('appends a created company and toggles the saving flag', async () => {
    const created = store.create({ name: 'New', registrationNumber: 'R', email: 'n@n.test' });
    expect(store.saving()).toBe(true);
    await settle();
    const request = backend.expectOne('/api/v1/companies');
    expect(request.request.method).toBe('POST');
    request.flush({ id: 'c3', name: 'New', registrationNumber: 'R', email: 'n@n.test' });
    await expect(created).resolves.toMatchObject({ id: 'c3' });
    expect(store.saving()).toBe(false);
    expect(store.companies().map((company) => company.id)).toEqual(['c3']);
  });

  it('replaces an updated company and rethrows failures', async () => {
    store.load();
    await settle();
    backend.expectOne('/api/v1/companies').flush([acme]);

    const updated = store.update('c1', {
      name: 'Acme SA',
      registrationNumber: 'RCS-1',
      email: 'a@acme.test',
    });
    await settle();
    const request = backend.expectOne('/api/v1/companies/c1');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      name: 'Acme SA',
      registrationNumber: 'RCS-1',
      email: 'a@acme.test',
    });
    request.flush({ ...acme, name: 'Acme SA' });
    await updated;
    expect(store.companies()[0].name).toBe('Acme SA');

    const failing = store.update('c1', { name: 'X', registrationNumber: 'R', email: 'x@x.test' });
    await settle();
    backend
      .expectOne('/api/v1/companies/c1')
      .flush({ title: 'Missing' }, { status: 404, statusText: 'Not Found' });
    await expect(failing).rejects.toMatchObject({ status: 404 });
    expect(store.saving()).toBe(false);
  });
});
