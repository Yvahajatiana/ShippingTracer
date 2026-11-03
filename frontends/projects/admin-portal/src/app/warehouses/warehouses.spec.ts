import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Notifier } from '@parcelflow/shared';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { page, selectOption, settle, submitDialog, typeInto, warehouse } from '../testing/fixtures';
import { WarehouseDialogComponent } from './warehouse-dialog.component';
import { WarehousesPage } from './warehouses.page';
import { WarehousesStore } from './warehouses.store';

const FIRST_PAGE = '/api/v1/warehouses?page=1&pageSize=20';

describe('warehouses', () => {
  let backend: HttpTestingController;
  let notifier: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    notifier = { success: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideParcelFlowTesting({
          subject: 'admin-david',
          translations: { warehouses: { codeTaken: 'Code already used' } },
        }),
        provideRouter([]),
        { provide: Notifier, useValue: notifier },
        WarehousesStore,
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    backend.verify();
    document.body.innerHTML = '';
  });

  describe('WarehousesStore', () => {
    let store: InstanceType<typeof WarehousesStore>;

    beforeEach(() => {
      store = TestBed.inject(WarehousesStore);
    });

    it('loads pages with totals', async () => {
      store.loadPage(1);
      await settle();
      backend.expectOne(FIRST_PAGE).flush(page([warehouse('PAR-01'), warehouse('LYS-SC')], 25));
      expect(store.items().length).toBe(2);
      expect(store.pageCount()).toBe(2);
      expect(store.hasNext()).toBe(true);

      store.nextPage();
      await settle();
      backend
        .expectOne('/api/v1/warehouses?page=2&pageSize=20')
        .flush(page([warehouse('MRS-01')], 25, 2));
      expect(store.page()).toBe(2);
      expect(store.hasNext()).toBe(false);
    });

    it('creates a warehouse then refreshes the current page', async () => {
      const created = store.create({
        code: 'NTE-01',
        name: 'Nantes',
        kind: 'SortingCenter',
        street: '1 quai',
        city: 'Nantes',
        postalCode: '44000',
        countryCode: 'FR',
      });
      expect(store.saving()).toBe(true);
      await settle();
      const request = backend.expectOne('/api/v1/warehouses');
      expect(request.request.method).toBe('POST');
      request.flush(warehouse('NTE-01', { kind: 'SortingCenter' }));
      await created;
      expect(store.saving()).toBe(false);
      await settle();
      backend.expectOne(FIRST_PAGE).flush(page([warehouse('NTE-01')]));
      expect(store.items()[0].code).toBe('NTE-01');
    });

    it('replaces an updated warehouse in place', async () => {
      store.loadPage(1);
      await settle();
      backend.expectOne(FIRST_PAGE).flush(page([warehouse('PAR-01')]));
      const updated = store.update('w-PAR-01', {
        name: 'Paris Nord',
        street: '2 rue',
        city: 'Paris',
        postalCode: '75018',
        countryCode: 'FR',
        isActive: false,
      });
      await settle();
      const request = backend.expectOne('/api/v1/warehouses/w-PAR-01');
      expect(request.request.method).toBe('PUT');
      request.flush(warehouse('PAR-01', { name: 'Paris Nord', isActive: false }));
      await updated;
      expect(store.items()[0]).toMatchObject({ name: 'Paris Nord', isActive: false });
    });
  });

  describe('WarehouseDialogComponent', () => {
    async function open(existing: ReturnType<typeof warehouse> | null = null) {
      const fixture = TestBed.createComponent(WarehouseDialogComponent);
      fixture.componentRef.setInput('warehouse', existing);
      fixture.componentRef.setInput('open', true);
      await fixture.whenStable();
      return fixture;
    }

    function fillCreateForm(code: string) {
      typeInto('warehouse-code', code);
      selectOption('warehouse-kind', 'SortingCenter');
      typeInto('warehouse-name', 'Lille');
      typeInto('warehouse-street', '3 rue du Tri');
      typeInto('warehouse-postal', '59000');
      typeInto('warehouse-city', 'Lille');
      typeInto('warehouse-country', 'fr');
    }

    it('shows a duplicate code (409) on the code field and keeps the dialog open', async () => {
      const fixture = await open();
      fillCreateForm('par-01');
      expect((document.getElementById('warehouse-code') as HTMLInputElement).value).toBe('PAR-01');
      submitDialog();
      await settle();
      const request = backend.expectOne('/api/v1/warehouses');
      expect(request.request.body).toEqual({
        code: 'PAR-01',
        kind: 'SortingCenter',
        name: 'Lille',
        street: '3 rue du Tri',
        postalCode: '59000',
        city: 'Lille',
        countryCode: 'FR',
      });
      request.flush(
        { title: 'Warehouse code already used' },
        { status: 409, statusText: 'Conflict' },
      );
      await settle();
      await fixture.whenStable();
      expect(notifier.error).toHaveBeenCalledWith(expect.objectContaining({ status: 409 }));
      expect(document.body.textContent).toContain('Code already used');
      expect(fixture.componentInstance.open()).toBe(true);
    });

    it('rejects an invalid country code before calling the API', async () => {
      await open();
      fillCreateForm('LIL-01');
      typeInto('warehouse-country', 'FRA');
      submitDialog();
      await settle();
      backend.expectNone('/api/v1/warehouses');
      expect(document.body.textContent).toContain('validation.countryCode');
    });

    it('edits name and activity with code and kind read-only', async () => {
      const fixture = await open(warehouse('PAR-01'));
      expect((document.getElementById('warehouse-code') as HTMLInputElement).disabled).toBe(true);
      expect((document.getElementById('warehouse-kind') as HTMLSelectElement).disabled).toBe(true);
      typeInto('warehouse-name', 'Paris Est');
      (document.getElementById('warehouse-active') as HTMLElement).click();
      await fixture.whenStable();
      submitDialog();
      await settle();
      const request = backend.expectOne('/api/v1/warehouses/w-PAR-01');
      expect(request.request.body).toEqual({
        name: 'Paris Est',
        street: '1 rue du Dépôt',
        postalCode: '75010',
        city: 'Paris',
        countryCode: 'FR',
        isActive: false,
      });
      request.flush(warehouse('PAR-01', { name: 'Paris Est', isActive: false }));
      await settle();
      expect(notifier.success).toHaveBeenCalledWith('common.saved');
      expect(fixture.componentInstance.open()).toBe(false);
    });
  });

  describe('WarehousesPage', () => {
    it('renders the list with kind and status', async () => {
      const fixture = TestBed.createComponent(WarehousesPage);
      await fixture.whenStable();
      await settle();
      backend
        .expectOne(FIRST_PAGE)
        .flush(page([warehouse('PAR-01'), warehouse('LYS-SC', { isActive: false })]));
      await fixture.whenStable();
      const rows = fixture.nativeElement.querySelectorAll('tbody tr');
      expect(rows.length).toBe(2);
      expect(rows[0].textContent).toContain('PAR-01');
      expect(rows[1].textContent).toContain('common.inactive');
      expect(fixture.nativeElement.textContent).toContain('pager.summary');
    });
  });
});
