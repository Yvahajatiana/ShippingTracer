import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { KpiReport, addDays } from '@parcelflow/shared';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { COMPANIES, driver, invoice, page, settle, warehouse } from '../testing/fixtures';
import { OverviewPage } from './overview.page';
import { OverviewStore, dailySeries, lastDays, reportLastDay } from './overview.store';

const REPORT: KpiReport = {
  from: '2026-10-01',
  // Exclusive bound, as echoed by Reporting: the report covers 1-3 October.
  to: '2026-10-04',
  generatedAtUtc: '2026-10-04T12:00:00Z',
  totalShipments: 5,
  deliveredShipments: 2,
  deliveryRatePercent: 40,
  averageDeliveryHours: 52.5,
  deliveryDurationSamples: 2,
  averageTransitHours: null,
  transitDurationSamples: 0,
  dailyVolumes: [
    { date: '2026-10-01', shipments: 3 },
    { date: '2026-10-03', shipments: 2 },
  ],
  statusVolumes: [{ status: 'Delivered', shipments: 2 }],
};

const isKpis = (url: string) => url === '/api/v1/reports/kpis';

describe('overview', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideParcelFlowTesting({ subject: 'admin-david' }),
        provideRouter([]),
        OverviewStore,
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  function answerCounters(failWarehouses = false) {
    backend.expectOne('/api/v1/companies').flush(COMPANIES);
    const warehouses = backend.expectOne('/api/v1/warehouses?page=1&pageSize=1');
    if (failWarehouses) {
      warehouses.flush({}, { status: 503, statusText: 'Unavailable' });
    } else {
      warehouses.flush(page([warehouse('PAR-01')], 3));
    }
    backend.expectOne('/api/v1/delivery/drivers?page=1&pageSize=1').flush(page([driver('d')], 4));
    backend
      .expectOne('/api/v1/billing/invoices?page=1&pageSize=100')
      .flush(
        page([
          invoice('a', { status: 'PendingPayment' }),
          invoice('b'),
          invoice('c', { status: 'PendingPayment' }),
        ]),
      );
  }

  it('computes the 30-day window and a gap-free daily series over [from, to)', () => {
    expect(lastDays(30, new Date(2026, 9, 30))).toEqual({ from: '2026-10-01', to: '2026-10-30' });
    expect(dailySeries(REPORT)).toEqual({
      dates: ['2026-10-01', '2026-10-02', '2026-10-03'],
      values: [3, 0, 2],
    });
    expect(reportLastDay(REPORT)).toBe('2026-10-03');
  });

  it('loads the KPI report for the last 30 days and exposes errors', async () => {
    const store = TestBed.inject(OverviewStore);
    store.loadReport();
    await settle();
    const request = backend.expectOne((req) => isKpis(req.url));
    const { from, to } = lastDays(30);
    expect(request.request.params.get('from')).toBe(from);
    // Today is the inclusive end: Reporting receives the exclusive next day.
    expect(request.request.params.get('to')).toBe(addDays(to, 1));
    request.flush({}, { status: 503, statusText: 'Unavailable' });
    expect(store.error()).toMatchObject({ status: 503 });

    store.loadReport();
    await settle();
    backend.expectOne((req) => isKpis(req.url)).flush(REPORT);
    expect(store.error()).toBeNull();
    expect(store.daily().values).toEqual([3, 0, 2]);
    expect(store.lastDay()).toBe('2026-10-03');
  });

  it('counts sections and tolerates a failing counter', async () => {
    const store = TestBed.inject(OverviewStore);
    store.loadCounters();
    expect(store.countersLoading()).toBe(true);
    await settle();
    answerCounters(true);
    expect(store.countersLoading()).toBe(false);
    expect(store.counters()).toEqual({
      companies: 2,
      warehouses: null,
      drivers: 4,
      pendingInvoices: 2,
    });
  });

  it('counts pending invoices across every page', async () => {
    const store = TestBed.inject(OverviewStore);
    store.loadCounters();
    await settle();
    backend.expectOne('/api/v1/companies').flush(COMPANIES);
    backend.expectOne('/api/v1/warehouses?page=1&pageSize=1').flush(page([], 3));
    backend.expectOne('/api/v1/delivery/drivers?page=1&pageSize=1').flush(page([], 4));
    expect(store.countersLoading()).toBe(false);
    expect(store.pendingInvoicesLoading()).toBe(true);

    const first = Array.from({ length: 100 }, (_, index) =>
      invoice(`a${index}`, { status: index < 10 ? 'PendingPayment' : 'Paid' }),
    );
    backend
      .expectOne('/api/v1/billing/invoices?page=1&pageSize=100')
      .flush(page(first, 102, 1, 100));
    await settle();
    backend
      .expectOne('/api/v1/billing/invoices?page=2&pageSize=100')
      .flush(page([invoice('b1', { status: 'PendingPayment' }), invoice('b2')], 102, 2, 100));

    expect(store.pendingInvoicesLoading()).toBe(false);
    expect(store.pendingInvoicesComplete()).toBe(true);
    expect(store.counters().pendingInvoices).toBe(11);
  });

  it('renders KPI cards, the chart and section links', async () => {
    const fixture = TestBed.createComponent(OverviewPage);
    await fixture.whenStable();
    await settle();
    backend.expectOne((req) => isKpis(req.url)).flush(REPORT);
    answerCounters();
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('pf-chart canvas')).not.toBeNull();
    expect(element.textContent).toContain('overview.kpis.totalShipments');
    expect(element.querySelectorAll('a[href="/companies"]').length).toBe(1);
    expect(element.querySelector('a[href="/billing/invoices"]')?.textContent).toContain('2');
  });
});
