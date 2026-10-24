import { HttpTestingController } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ChartComponent, ChartSeries } from '@parcelflow/shared';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { kpiReport, settle } from '../testing/fixtures';
import { DashboardPage } from './dashboard.page';

/** chart.js needs a real canvas: the chart is replaced by a stub exposing its inputs. */
@Component({
  selector: 'pf-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div data-testid="chart" [attr.data-type]="type()">{{ labels().join('|') }}</div>`,
})
class ChartStubComponent {
  readonly type = input('bar');
  readonly labels = input.required<string[]>();
  readonly series = input.required<ChartSeries[]>();
  readonly ariaLabel = input.required<string>();
}

describe('DashboardPage', () => {
  let backend: HttpTestingController;

  async function render() {
    TestBed.configureTestingModule({
      imports: [DashboardPage],
      providers: [provideParcelFlowTesting({ subject: 'operator-chloe' }), provideRouter([])],
    });
    TestBed.overrideComponent(DashboardPage, {
      remove: { imports: [ChartComponent] },
      add: { imports: [ChartStubComponent] },
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(DashboardPage);
    await fixture.whenStable();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  afterEach(() => backend.verify());

  it('shows skeletons, then KPI cards and charts', async () => {
    const { fixture, element } = await render();
    expect(element.querySelector('[data-testid=kpi-skeleton]')).not.toBeNull();

    await settle();
    backend.expectOne((req) => req.url === '/api/v1/reports/kpis').flush(kpiReport());
    await fixture.whenStable();

    expect(element.querySelector('[data-testid=kpi-total]')!.textContent).toContain('4');
    expect(element.querySelector('[data-testid=kpi-delivered]')!.textContent).toContain('2');
    expect(element.querySelector('[data-testid=kpi-transit-hours]')!.textContent).toContain(
      'dashboard.kpi.samples',
    );
    const charts = element.querySelectorAll('[data-testid=chart]');
    expect(charts).toHaveLength(2);
    expect(charts[1].getAttribute('data-type')).toBe('doughnut');
    // Status labels go through the enum translations.
    expect(charts[1].textContent).toBe('enum.ParcelStatus.Delivered|enum.ParcelStatus.InTransit');
  });

  it('shows the error banner and retries', async () => {
    const { fixture, element } = await render();
    await settle();
    backend
      .expectOne((req) => req.url === '/api/v1/reports/kpis')
      .flush({}, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    const retry = element.querySelector<HTMLButtonElement>('pf-error-banner button')!;
    expect(retry).not.toBeNull();
    retry.click();
    await settle();
    backend.expectOne((req) => req.url === '/api/v1/reports/kpis').flush(kpiReport());
    await fixture.whenStable();
    expect(element.querySelector('pf-error-banner')).toBeNull();
  });

  it('shows the empty state when nothing was shipped', async () => {
    const { fixture, element } = await render();
    await settle();
    backend
      .expectOne((req) => req.url === '/api/v1/reports/kpis')
      .flush(kpiReport({ totalShipments: 0, dailyVolumes: [], statusVolumes: [] }));
    await fixture.whenStable();
    expect(element.textContent).toContain('dashboard.empty.title');
    expect(element.querySelector('[data-testid=chart]')).toBeNull();
  });
});
