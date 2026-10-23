import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { settle, shipment } from '../testing/fixtures';
import { SHIPMENT_PAGE_SIZE } from './shipments.store';
import { ShipmentListPage } from './shipment-list.page';

describe('ShipmentListPage', () => {
  let backend: HttpTestingController;

  afterEach(() => backend.verify());

  it('pages with offset/limit and filters the loaded page by tracking number', async () => {
    TestBed.configureTestingModule({
      imports: [ShipmentListPage],
      providers: [provideParcelFlowTesting({ subject: 'operator-chloe' }), provideRouter([])],
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ShipmentListPage);
    const element = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
    await settle();

    const first = backend.expectOne((req) => req.url === '/api/v1/shipments');
    expect(first.request.params.get('offset')).toBe('0');
    // One extra item reveals whether a next page exists.
    expect(first.request.params.get('limit')).toBe(String(SHIPMENT_PAGE_SIZE + 1));
    const items = Array.from({ length: SHIPMENT_PAGE_SIZE + 1 }, (_, index) => ({
      ...shipment(`sh-${index}`, `PF${String(index).padStart(8, '0')}FR`),
      // The API still serializes these enums as integers.
      zone: 0 as unknown as 'Domestic',
      status: 1 as unknown as 'ReadyForPickup',
    }));
    first.flush(items);
    await fixture.whenStable();

    const rows = () => element.querySelectorAll('table tbody tr');
    expect(rows()).toHaveLength(SHIPMENT_PAGE_SIZE);
    expect(element.textContent).toContain('enum.ShipmentStatus.ReadyForPickup');

    const search = element.querySelector<HTMLInputElement>('#shipment-search')!;
    search.value = '00000007';
    search.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(rows()).toHaveLength(1);
    expect(rows()[0].textContent).toContain('PF00000007FR');

    const next = [...element.querySelectorAll<HTMLButtonElement>('pf-pager button')].at(-1)!;
    expect(next.disabled).toBe(false);
    next.click();
    await settle();
    const second = backend.expectOne((req) => req.url === '/api/v1/shipments');
    expect(second.request.params.get('offset')).toBe(String(SHIPMENT_PAGE_SIZE));
    second.flush([]);
  });
});
