import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translateSignal } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowRight,
  lucideBuilding2,
  lucideReceipt,
  lucideTrendingUp,
  lucideTruck,
  lucideWarehouse,
} from '@ng-icons/lucide';
import {
  ChartComponent,
  ChartSeries,
  ErrorBannerComponent,
  LanguageService,
  PageHeaderComponent,
  PfDatePipe,
  PfNumberPipe,
  formatDate,
} from '@parcelflow/shared';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { AdminCounters, KPI_WINDOW_DAYS, OverviewStore } from './overview.store';

interface SectionLink {
  key: keyof AdminCounters;
  labelKey: string;
  route: string;
  icon: string;
}

const SECTIONS: SectionLink[] = [
  {
    key: 'companies',
    labelKey: 'overview.sections.companies',
    route: '/companies',
    icon: 'lucideBuilding2',
  },
  {
    key: 'warehouses',
    labelKey: 'overview.sections.warehouses',
    route: '/warehouses',
    icon: 'lucideWarehouse',
  },
  { key: 'drivers', labelKey: 'overview.sections.drivers', route: '/drivers', icon: 'lucideTruck' },
  {
    key: 'pendingInvoices',
    labelKey: 'overview.sections.pendingInvoices',
    route: '/billing/invoices',
    icon: 'lucideReceipt',
  },
];

@Component({
  selector: 'pf-overview-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    NgIcon,
    PfDatePipe,
    PfNumberPipe,
    ChartComponent,
    PageHeaderComponent,
    ErrorBannerComponent,
    HlmCardImports,
    HlmSkeletonImports,
  ],
  providers: [
    OverviewStore,
    provideIcons({
      lucideArrowRight,
      lucideBuilding2,
      lucideReceipt,
      lucideTrendingUp,
      lucideTruck,
      lucideWarehouse,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <pf-page-header
      [eyebrow]="'nav.groups.monitoring' | transloco"
      [title]="'overview.title' | transloco"
      [subtitle]="'overview.subtitle' | transloco: { days: windowDays }"
    />

    <section aria-labelledby="overview-kpi-title" class="mb-8">
      <h2 id="overview-kpi-title" class="sr-only">{{ 'overview.kpiTitle' | transloco }}</h2>
      @if (store.error()) {
        <pf-error-banner [error]="store.error()" (retry)="store.loadReport()" />
      } @else if (!store.report()) {
        <div class="grid gap-4" role="status" [attr.aria-label]="'common.loading' | transloco">
          <hlm-skeleton class="h-32 w-full rounded-md" />
          <hlm-skeleton class="h-80 w-full rounded-md" />
        </div>
      } @else {
        @let report = store.report()!;
        <!-- A single card holding the four KPIs; 1px dividers come from the gap over a border-coloured background. -->
        <ul hlmCard class="grid gap-px bg-border py-0 sm:grid-cols-2 xl:grid-cols-4">
          <li class="flex flex-col gap-1.5 bg-card px-6 py-5">
            <p class="text-xs font-semibold tracking-[0.06em] text-muted-foreground uppercase">
              {{ 'overview.kpis.totalShipments' | transloco }}
            </p>
            <p class="text-3xl font-semibold tracking-tight tabular-nums">
              {{ report.totalShipments | pfNumber }}
            </p>
          </li>
          <li class="flex flex-col gap-1.5 bg-card px-6 py-5">
            <p class="text-xs font-semibold tracking-[0.06em] text-muted-foreground uppercase">
              {{ 'overview.kpis.delivered' | transloco }}
            </p>
            <p class="text-3xl font-semibold tracking-tight tabular-nums">
              {{ report.deliveredShipments | pfNumber }}
            </p>
            @if (report.deliveryRatePercent !== null) {
              <div class="flex items-center gap-3">
                <!-- Decorative: the rate is stated in the adjacent text. -->
                <div class="h-1.5 flex-1 overflow-hidden rounded-full bg-muted" aria-hidden="true">
                  <div
                    class="h-full rounded-full bg-success"
                    [style.width.%]="clampPercent(report.deliveryRatePercent)"
                  ></div>
                </div>
                <p class="text-sm text-muted-foreground tabular-nums">
                  {{
                    'overview.kpis.rate'
                      | transloco: { rate: (report.deliveryRatePercent | pfNumber: 1) }
                  }}
                </p>
              </div>
            } @else {
              <p class="text-sm text-muted-foreground">
                {{ 'overview.kpis.noData' | transloco }}
              </p>
            }
          </li>
          <li class="flex flex-col gap-1.5 bg-card px-6 py-5">
            <p class="text-xs font-semibold tracking-[0.06em] text-muted-foreground uppercase">
              {{ 'overview.kpis.averageDelivery' | transloco }}
            </p>
            <p class="text-3xl font-semibold tracking-tight tabular-nums">
              @if (report.averageDeliveryHours !== null) {
                {{
                  'overview.kpis.hours'
                    | transloco: { hours: (report.averageDeliveryHours | pfNumber: 1) }
                }}
              } @else {
                —
              }
            </p>
            <p class="text-sm text-muted-foreground">
              {{ 'overview.kpis.samples' | transloco: { count: report.deliveryDurationSamples } }}
            </p>
          </li>
          <li class="flex flex-col gap-1.5 bg-card px-6 py-5">
            <p class="text-xs font-semibold tracking-[0.06em] text-muted-foreground uppercase">
              {{ 'overview.kpis.averageTransit' | transloco }}
            </p>
            <p class="text-3xl font-semibold tracking-tight tabular-nums">
              @if (report.averageTransitHours !== null) {
                {{
                  'overview.kpis.hours'
                    | transloco: { hours: (report.averageTransitHours | pfNumber: 1) }
                }}
              } @else {
                —
              }
            </p>
            <p class="text-sm text-muted-foreground">
              {{ 'overview.kpis.samples' | transloco: { count: report.transitDurationSamples } }}
            </p>
          </li>
        </ul>

        <section hlmCard class="mt-4" aria-labelledby="overview-chart-title">
          <div hlmCardHeader>
            <h3 hlmCardTitle id="overview-chart-title" class="flex items-center gap-2">
              <ng-icon name="lucideTrendingUp" aria-hidden="true" />
              {{ 'overview.chart.title' | transloco }}
            </h3>
            <p hlmCardDescription>
              {{
                'overview.chart.range'
                  | transloco
                    : { from: (report.from | pfDate: 'date'), to: (report.to | pfDate: 'date') }
              }}
            </p>
          </div>
          <div hlmCardContent>
            <pf-chart
              type="bar"
              class="h-64"
              [labels]="chartLabels()"
              [series]="chartSeries()"
              [ariaLabel]="'overview.chart.ariaLabel' | transloco: { total: report.totalShipments }"
            />
          </div>
        </section>
      }
    </section>

    <section aria-labelledby="overview-sections-title">
      <h2 id="overview-sections-title" class="mb-4 text-lg font-semibold">
        {{ 'overview.sectionsTitle' | transloco }}
      </h2>
      <ul class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        @for (section of sections; track section.key) {
          <li>
            <a
              hlmCard
              size="sm"
              class="h-full transition-colors hover:bg-muted/50 focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
              [routerLink]="section.route"
            >
              <div hlmCardHeader>
                <p hlmCardDescription class="flex items-center gap-2">
                  <ng-icon [name]="section.icon" aria-hidden="true" />
                  {{ section.labelKey | transloco }}
                </p>
                <p hlmCardTitle class="flex items-center justify-between text-2xl tabular-nums">
                  @if (store.countersLoading()) {
                    <hlm-skeleton class="h-7 w-12" />
                  } @else {
                    <span>{{ store.counters()[section.key] ?? '—' }}</span>
                  }
                  <ng-icon
                    name="lucideArrowRight"
                    class="text-muted-foreground"
                    aria-hidden="true"
                  />
                </p>
              </div>
            </a>
          </li>
        }
      </ul>
    </section>
  `,
})
export class OverviewPage {
  protected readonly store = inject(OverviewStore);
  private readonly language = inject(LanguageService);
  private readonly seriesLabel = translateSignal('overview.chart.series');

  protected readonly windowDays = KPI_WINDOW_DAYS;
  protected readonly sections = SECTIONS;

  protected readonly chartLabels = computed(() =>
    this.store.daily().dates.map((date) => formatDate(date, this.language.locale(), 'date')),
  );
  protected readonly chartSeries = computed<ChartSeries[]>(() => [
    { label: this.seriesLabel(), data: this.store.daily().values },
  ]);

  /** Keeps the progress bar fill within its track. */
  protected clampPercent(value: number): number {
    return Math.min(100, Math.max(0, value));
  }

  constructor() {
    this.store.loadReport();
    this.store.loadCounters();
  }
}
