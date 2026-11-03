import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService, MockAuthService } from '@parcelflow/shared';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { aPage, aWireShipment, anInvoice, flushProblem, settle } from '../../testing/fixtures';
import { DashboardStore, INVOICE_SCAN_SIZE, RECENT_SHIPMENTS } from './dashboard.store';
import { HomePage } from './home.page';

describe('DashboardStore', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [provideParcelFlowTesting({ subject: 'customer-alice' }), DashboardStore],
    });
    return {
      store: TestBed.inject(DashboardStore),
      backend: TestBed.inject(HttpTestingController),
    };
  }

  it('loads recent shipments and counts invoices awaiting payment', async () => {
    const { store, backend } = setup();
    store.load(true);
    await settle();
    const shipments = backend.expectOne((req) => req.url === '/api/v1/shipments');
    expect(shipments.request.params.get('limit')).toBe(String(RECENT_SHIPMENTS));
    shipments.flush([aWireShipment(0), aWireShipment(1)]);
    const invoices = backend.expectOne((req) => req.url === '/api/v1/billing/invoices');
    expect(invoices.request.params.get('pageSize')).toBe(String(INVOICE_SCAN_SIZE));
    invoices.flush(
      aPage([
        anInvoice({ id: 'a' }),
        anInvoice({ id: 'b', status: 'Paid' }),
        anInvoice({ id: 'c' }),
      ]),
    );

    expect(store.recentShipments()).toHaveLength(2);
    expect(store.pendingInvoices()).toBe(2);
    expect(store.loading()).toBe(false);
    backend.verify();
  });

  it('does not call the API for visitors and reports failures', async () => {
    const { store, backend } = setup();
    store.load(false);
    await settle();
    backend.expectNone(() => true);
    expect(store.loading()).toBe(false);

    store.load(true);
    await settle();
    flushProblem(
      backend.expectOne((req) => req.url === '/api/v1/shipments'),
      500,
    );
    expect(backend.expectOne((req) => req.url === '/api/v1/billing/invoices').cancelled).toBe(true);
    expect(store.error()?.status).toBe(500);
    backend.verify();
  });
});

describe('HomePage', () => {
  async function render(subject: string | null) {
    TestBed.configureTestingModule({
      providers: [provideParcelFlowTesting({ subject }), provideRouter([])],
    });
    const fixture = TestBed.createComponent(HomePage);
    await fixture.whenStable();
    return { fixture, backend: TestBed.inject(HttpTestingController) };
  }

  it('offers tracking and sign-in to anonymous visitors without calling private APIs', async () => {
    const { fixture, backend } = await render(null);
    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('form[role=search]')).not.toBeNull();
    expect(element.textContent).toContain('home.signIn.title');
    expect(element.textContent).not.toContain('home.dashboard.recent');
    await settle();
    backend.verify();
  });

  it('navigates to the public tracking page with a normalized number', async () => {
    const { fixture } = await render(null);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const element: HTMLElement = fixture.nativeElement;
    const input = element.querySelector<HTMLInputElement>('#pf-tracking-number')!;

    input.value = 'ab';
    input.dispatchEvent(new Event('input'));
    element.querySelector<HTMLFormElement>('form[role=search]')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    expect(navigate).not.toHaveBeenCalled();
    expect(element.textContent).toContain('validation.pattern');

    input.value = ' pf00240000fr ';
    input.dispatchEvent(new Event('input'));
    element.querySelector<HTMLFormElement>('form[role=search]')!.dispatchEvent(new Event('submit'));
    expect(navigate).toHaveBeenCalledWith(['/track', 'PF00240000FR']);
  });

  it('shows the dashboard to a signed-in customer and follows sign-out', async () => {
    const { fixture, backend } = await render('customer-alice');
    await settle();
    backend.expectOne((req) => req.url === '/api/v1/shipments').flush([aWireShipment(0)]);
    backend.expectOne((req) => req.url === '/api/v1/billing/invoices').flush(aPage([anInvoice()]));
    await fixture.whenStable();

    const element: HTMLElement = fixture.nativeElement;
    expect(element.textContent).toContain('home.dashboard.recent');
    expect(element.textContent).toContain('PF00240000FR');
    expect(element.querySelector('[data-testid=pending-invoices]')?.textContent?.trim()).toBe('1');

    await (TestBed.inject(AuthService) as MockAuthService).logout();
    await fixture.whenStable();
    expect(element.textContent).not.toContain('home.dashboard.recent');
    expect(element.textContent).toContain('home.signIn.title');
    backend.verify();
  });
});
