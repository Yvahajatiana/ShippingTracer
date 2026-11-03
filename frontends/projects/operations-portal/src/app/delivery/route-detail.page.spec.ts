import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { DRIVERS, page, route, settle } from '../testing/fixtures';
import { RouteDetailPage } from './route-detail.page';
import { AVAILABLE_PARCELS_SIZE } from './route-detail.store';

const AVAILABLE_URL = '/api/v1/delivery/parcels/available';

describe('RouteDetailPage', () => {
  let backend: HttpTestingController;

  afterEach(() => backend.verify());

  it('paginates the available-parcel picker beyond the first page', async () => {
    TestBed.configureTestingModule({
      imports: [RouteDetailPage],
      providers: [provideParcelFlowTesting({ subject: 'operator-chloe' }), provideRouter([])],
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(RouteDetailPage);
    fixture.componentRef.setInput('id', 'route-1');
    const element = fixture.nativeElement as HTMLElement;
    const total = AVAILABLE_PARCELS_SIZE + 1;
    const pending = (url: string) => vi.waitFor(() => backend.expectOne((req) => req.url === url));

    fixture.detectChanges();
    (await pending('/api/v1/delivery/routes/route-1')).flush(route());
    (await pending('/api/v1/delivery/drivers')).flush(page(DRIVERS));
    (await pending(AVAILABLE_URL)).flush(
      page([{ shipmentId: 'sh-1', trackingNumber: 'PF1FR' }], 1, AVAILABLE_PARCELS_SIZE, total),
    );
    await settle();
    await fixture.whenStable();

    const pager = element.querySelector('[data-testid="parcel-pager"] nav')!;
    expect(pager.getAttribute('aria-label')).toBe('routes.detail.parcelPager');
    const [previous, nextButton] = [...pager.querySelectorAll<HTMLButtonElement>('button')];
    expect(previous.disabled).toBe(true);
    expect(nextButton.disabled).toBe(false);

    nextButton.click();
    const second = await pending(AVAILABLE_URL);
    expect(second.request.params.get('page')).toBe('2');
    second.flush(
      page([{ shipmentId: 'sh-101', trackingNumber: 'PF101FR' }], 2, AVAILABLE_PARCELS_SIZE, total),
    );
    await settle();
    await fixture.whenStable();

    const options = [...element.querySelectorAll<HTMLOptionElement>('#route-parcel option')];
    expect(options.map((option) => option.textContent?.trim())).toContain('PF101FR');
    expect(options.map((option) => option.textContent?.trim())).not.toContain('PF1FR');
    expect(nextButton.disabled).toBe(true);
    expect(previous.disabled).toBe(false);
  });
});
