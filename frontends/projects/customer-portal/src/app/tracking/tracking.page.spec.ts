import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { aTracking, expectRequest, flushProblem } from '../../testing/fixtures';
import { TrackingPage } from './tracking.page';

describe('TrackingPage', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideParcelFlowTesting({ subject: null }), provideRouter([])],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  async function render(trackingNumber: string) {
    const fixture = TestBed.createComponent(TrackingPage);
    fixture.componentRef.setInput('trackingNumber', trackingNumber);
    await fixture.whenStable();
    return fixture;
  }

  it('renders the current status and the timeline for a visitor', async () => {
    const fixture = await render('PF00240000FR');
    (await expectRequest(backend, '/api/v1/tracking/PF00240000FR')).flush(aTracking());
    await fixture.whenStable();

    const element: HTMLElement = fixture.nativeElement;
    const items = element.querySelectorAll('pf-tracking-timeline li');
    expect(items).toHaveLength(2);
    expect(items[0].textContent).toContain('enum.ParcelStatus.InTransit');
    expect(items[0].textContent).toContain('tracking.facility');
    expect(items[1].textContent).toContain('enum.ParcelStatus.Created');
  });

  it('shows a friendly not-found state', async () => {
    const fixture = await render('PF99999999FR');
    flushProblem(await expectRequest(backend, '/api/v1/tracking/PF99999999FR'), 404);
    await fixture.whenStable();

    const element: HTMLElement = fixture.nativeElement;
    expect(element.textContent).toContain('tracking.notFound.title');
    expect(element.querySelector('pf-error-banner')).toBeNull();
  });

  it('reloads when the route parameter changes', async () => {
    const fixture = await render('PF00240000FR');
    (await expectRequest(backend, '/api/v1/tracking/PF00240000FR')).flush(aTracking());
    fixture.componentRef.setInput('trackingNumber', 'PF00240137FR');
    await fixture.whenStable();
    (await expectRequest(backend, '/api/v1/tracking/PF00240137FR')).flush(
      aTracking({ trackingNumber: 'PF00240137FR' }),
    );
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('PF00240137FR');
  });
});
