import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Notifier } from '@parcelflow/shared';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { CustomerDetailPage } from './customer-detail.page';
import { CustomerDetailStore } from './customer-detail.store';
import { COMPANIES as companies, profile, settle } from '../testing/fixtures';

describe('customer detail', () => {
  let backend: HttpTestingController;
  let notifier: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    notifier = { success: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideParcelFlowTesting({ subject: 'admin-david' }),
        provideRouter([]),
        { provide: Notifier, useValue: notifier },
        CustomerDetailStore,
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  describe('CustomerDetailStore', () => {
    let store: InstanceType<typeof CustomerDetailStore>;

    beforeEach(() => {
      store = TestBed.inject(CustomerDetailStore);
    });

    it('loads the profile with the company catalogue', async () => {
      store.load('p1');
      await settle();
      backend.expectOne('/api/v1/customers/p1').flush(profile(1, { companyId: 'c1' }));
      backend.expectOne('/api/v1/companies').flush(companies);
      expect(store.profile()?.id).toBe('p1');
      expect(store.company()?.name).toBe('Acme');
      expect(store.sortedCompanies().map((company) => company.name)).toEqual(['Acme', 'Zeta']);
    });

    it('assigns and removes a company', async () => {
      store.load('p1');
      await settle();
      backend.expectOne('/api/v1/customers/p1').flush(profile(1));
      backend.expectOne('/api/v1/companies').flush(companies);

      const assigned = store.assignCompany('c2');
      expect(store.assigning()).toBe(true);
      await settle();
      const request = backend.expectOne('/api/v1/customers/p1/company');
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({ companyId: 'c2' });
      request.flush(profile(1, { companyId: 'c2' }));
      await assigned;
      expect(store.company()?.name).toBe('Zeta');
      expect(store.assigning()).toBe(false);

      const removed = store.assignCompany(null);
      await settle();
      const removal = backend.expectOne('/api/v1/customers/p1/company');
      expect(removal.request.body).toEqual({ companyId: null });
      removal.flush(profile(1));
      await removed;
      expect(store.company()).toBeNull();
    });

    it('exposes a load error', async () => {
      store.load('missing');
      await settle();
      backend
        .expectOne('/api/v1/customers/missing')
        .flush({}, { status: 404, statusText: 'Not Found' });
      // forkJoin cancels the sibling request once one side fails.
      expect(backend.expectOne('/api/v1/companies').cancelled).toBe(true);
      expect(store.error()).toMatchObject({ status: 404 });
    });
  });

  describe('CustomerDetailPage', () => {
    it('renders the profile and assigns the selected company', async () => {
      const fixture = TestBed.createComponent(CustomerDetailPage);
      fixture.componentRef.setInput('id', 'p1');
      await fixture.whenStable();
      await settle();
      backend.expectOne('/api/v1/customers/p1').flush(
        profile(1, {
          addresses: [
            {
              id: 'a1',
              label: 'Home',
              line1: '1 rue de Paris',
              line2: null,
              postalCode: '75001',
              city: 'Paris',
              countryCode: 'FR',
            },
          ],
        }),
      );
      backend.expectOne('/api/v1/companies').flush(companies);
      await fixture.whenStable();

      const element: HTMLElement = fixture.nativeElement;
      expect(element.querySelector('h1')?.textContent).toContain('First Last1');
      expect(element.textContent).toContain('1 rue de Paris');
      expect(element.textContent).toContain('customers.company.noneCurrent');

      const select = element.querySelector<HTMLSelectElement>('#customer-company')!;
      select.value = 'c1';
      select.dispatchEvent(new Event('change'));
      await fixture.whenStable();
      element.querySelector<HTMLButtonElement>('form button[type="submit"]')!.click();
      await settle();
      const request = backend.expectOne('/api/v1/customers/p1/company');
      expect(request.request.body).toEqual({ companyId: 'c1' });
      request.flush(profile(1, { companyId: 'c1' }));
      await settle();
      await fixture.whenStable();
      expect(notifier.success).toHaveBeenCalledWith('customers.company.assigned');
      expect(element.textContent).toContain('customers.company.current');
    });
  });
});
