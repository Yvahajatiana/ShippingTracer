import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { kpiReport, settle } from '../testing/fixtures';
import { addDays } from '@parcelflow/shared';
import { KpiStore, defaultRange, isValidRange, rangeLength } from './kpi.store';

describe('KpiStore', () => {
  let backend: HttpTestingController;

  function setup() {
    TestBed.configureTestingModule({
      providers: [
        provideParcelFlowTesting({ subject: 'operator-chloe' }),
        provideRouter([]),
        KpiStore,
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    return TestBed.inject(KpiStore);
  }

  afterEach(() => backend?.verify());

  it('computes the default range as the last 30 days, today included', () => {
    const range = defaultRange(new Date(2026, 2, 31));
    expect(range).toEqual({ from: '2026-03-02', to: '2026-03-31' });
    expect(rangeLength(range)).toBe(30);
  });

  it('accepts single-day ranges and rejects inverted or over-long ones', () => {
    expect(isValidRange({ from: '2026-05-01', to: '2026-05-01' })).toBe(true);
    expect(isValidRange({ from: '2026-05-02', to: '2026-05-01' })).toBe(false);
    // 2024 is a leap year: 366 inclusive days is the Reporting maximum.
    expect(isValidRange({ from: '2024-01-01', to: '2024-12-31' })).toBe(true);
    expect(isValidRange({ from: '2024-01-01', to: '2025-01-01' })).toBe(false);
  });

  it('loads the default range on init and exposes chart data', async () => {
    const store = setup();
    const range = defaultRange();
    expect(store.loading()).toBe(true);
    await settle();
    const request = backend.expectOne((req) => req.url === '/api/v1/reports/kpis');
    expect(request.request.params.get('from')).toBe(range.from);
    // The inclusive end (today) is sent as the exclusive next day.
    expect(request.request.params.get('to')).toBe(addDays(range.to, 1));
    request.flush(kpiReport());

    expect(store.loading()).toBe(false);
    expect(store.report()?.totalShipments).toBe(4);
    expect(store.dailyLabels()).toEqual(['2026-09-02', '2026-09-03']);
    expect(store.dailyCounts()).toEqual([3, 1]);
    expect(store.statusVolumes().map((item) => item.status)).toEqual(['Delivered', 'InTransit']);
    expect(store.isEmpty()).toBe(false);
  });

  it('reloads with a new valid range and rejects an inverted one', async () => {
    const store = setup();
    await settle();
    backend.expectOne((req) => req.url === '/api/v1/reports/kpis').flush(kpiReport());

    expect(store.setRange('2026-05-10', '2026-05-01')).toBe(false);
    expect(store.setRange('2026-05-01', '2026-05-10')).toBe(true);
    await settle();
    const request = backend.expectOne((req) => req.url === '/api/v1/reports/kpis');
    expect(request.request.params.get('from')).toBe('2026-05-01');
    expect(request.request.params.get('to')).toBe('2026-05-11');
    request.flush(kpiReport({ totalShipments: 0, dailyVolumes: [], statusVolumes: [] }));

    expect(store.from()).toBe('2026-05-01');
    expect(store.to()).toBe('2026-05-10');
    expect(store.isEmpty()).toBe(true);
  });

  it('keeps the error and recovers on retry', async () => {
    const store = setup();
    await settle();
    backend
      .expectOne((req) => req.url === '/api/v1/reports/kpis')
      .flush({ title: 'boom' }, { status: 503, statusText: 'Unavailable' });

    expect(store.loading()).toBe(false);
    expect(store.error()).toMatchObject({ status: 503, messageKey: 'errors.http.5xx' });
    expect(store.report()).toBeNull();

    store.reload();
    expect(store.error()).toBeNull();
    await settle();
    backend.expectOne((req) => req.url === '/api/v1/reports/kpis').flush(kpiReport());
    expect(store.report()).not.toBeNull();
  });
});
