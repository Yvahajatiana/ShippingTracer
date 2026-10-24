import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { aWireShipment, expectRequest } from '../../testing/fixtures';
import { ShipmentsPage } from './shipments.page';

describe('ShipmentsPage', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideParcelFlowTesting({ subject: 'customer-alice' }), provideRouter([])],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('renders shipments as table rows and mobile cards linking to the details', async () => {
    const fixture = TestBed.createComponent(ShipmentsPage);
    await fixture.whenStable();
    (await expectRequest(backend, (req) => req.url === '/api/v1/shipments')).flush([
      aWireShipment(0),
      aWireShipment(1, { status: 3, zone: 0 }),
    ]);
    await fixture.whenStable();

    const element: HTMLElement = fixture.nativeElement;
    const rows = element.querySelectorAll('tbody tr');
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('PF00240000FR');
    expect(rows[0].textContent).toContain('enum.DeliveryZone.Europe');
    expect(rows[1].textContent).toContain('enum.ShipmentStatus.PickedUp');
    expect(rows[1].textContent).toContain('enum.DeliveryZone.Domestic');
    expect(element.querySelector('a[href="/shipments/shipment-0"]')).not.toBeNull();
    expect(element.querySelectorAll('ul.md\\:hidden > li')).toHaveLength(2);
    // Single page: both pager buttons are disabled.
    const pager = [...element.querySelectorAll<HTMLButtonElement>('pf-pager button')];
    expect(pager.every((button) => button.disabled)).toBe(true);
  });

  it('shows an empty state with a call to action', async () => {
    const fixture = TestBed.createComponent(ShipmentsPage);
    await fixture.whenStable();
    (await expectRequest(backend, (req) => req.url === '/api/v1/shipments')).flush([]);
    await fixture.whenStable();

    const element: HTMLElement = fixture.nativeElement;
    expect(element.textContent).toContain('shipments.empty.title');
    expect(element.querySelectorAll('a[href="/shipments/new"]').length).toBeGreaterThan(0);
    expect(element.querySelector('table')).toBeNull();
  });
});
