import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translateSignal } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowRight,
  lucideBuilding2,
  lucideCircleCheck,
  lucideClock,
  lucidePackage,
  lucideReceipt,
  lucideTimer,
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
      lucideCircleCheck,
      lucideClock,
      lucidePackage,
      lucideReceipt,
      lucideTimer,
      lucideTrendingUp,
      lucideTruck,
      lucideWarehouse,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <pf-page-header
      [title]="'overview.title' | transloco"
      [subtitle]="'overview.subtitle' | transloco: { days: windowDays }"
    />

    <section aria-labelledby="overview-kpi-title" class="mb-8">
      <h2 id="overview-kpi-title" class="sr-only">{{ 'overview.kpiTitle' | transloco }}</h2>
      @if (store.error()) {
        <pf-error-banner [error]="store.error()" (retry)="store.loadReport()" />
      } @else if (!store.report()) {
        <div
          class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4"
          role="status"
          [attr.aria-label]="'common.loading' | transloco"
        >
          @for (placeholder of [1, 2, 3, 4]; track placeholder) {
            <hlm-skeleton class="h-28 w-full rounded-xl" />
          }
          <hlm-skeleton class="h-72 w-full rounded-xl sm:col-span-2 xl:col-span-4" />
        </div>
      } @else {
        @let report = store.report()!;
        <ul class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <li hlmCard size="sm">
            <div hlmCardHeader>
              <p hlmCardDescription class="flex items-center gap-2">
                <ng-icon name="lucidePackage" aria-hidden="true" />
                {{ 'overview.kpis.totalShipments' | transloco }}
              </p>
              <p hlmCardTitle class="text-3xl font-semibold tabular-nums">
                {{ report.totalShipments | pfNumber }}
              </p>
            </div>
          </li>
          <li hlmCard size="sm">
            <div hlmCardHeader>
              <p hlmCardDescription class="flex items-center gap-2">
                <ng-icon name="lucideCircleCheck" aria-hidden="true" />
                {{ 'overview.kpis.delivered' | transloco }}
              </p>
              <p hlmCardTitle class="text-3xl font-semibold tabular-nums">
                {{ report.deliveredShipments | pfNumber }}
              </p>
              <p class="text-xs text-muted-foreground">
                @if (report.deliveryRatePercent !== null) {
                  {{
                    'overview.kpis.rate'
                      | transloco: { rate: (report.deliveryRatePercent | pfNumber: 1) }
                  }}
                } @else {
                  {{ 'overview.kpis.noData' | transloco }}
                }
              </p>
            </div>
          </li>
          <li hlmCard size="sm">
            <div hlmCardHeader>
              <p hlmCardDescription class="flex items-center gap-2">
                <ng-icon name="lucideClock" aria-hidden="true" />
                {{ 'overview.kpis.averageDelivery' | transloco }}
              </p>
              <p hlmCardTitle class="text-3xl font-semibold tabular-nums">
                @if (report.averageDeliveryHours !== null) {
                  {{
                    'overview.kpis.hours'
                      | transloco: { hours: (report.averageDeliveryHours | pfNumber: 1) }
                  }}
                } @else {
                  —
                }
              </p>
              <p class="text-xs text-muted-foreground">
                {{ 'overview.kpis.samples' | transloco: { count: report.deliveryDurationSamples } }}
              </p>
            </div>
          </li>
          <li hlmCard size="sm">
            <div hlmCardHeader>
              <p hlmCardDescription class="flex items-center gap-2">
                <ng-icon name="lucideTimer" aria-hidden="true" />
                {{ 'overview.kpis.averageTransit' | transloco }}
              </p>
              <p hlmCardTitle class="text-3xl font-semibold tabular-nums">
                @if (report.averageTransitHours !== null) {
                  {{
                    'overview.kpis.hours'
                      | transloco: { hours: (report.averageTransitHours | pfNumber: 1) }
                  }}
                } @else {
                  —
                }
              </p>
              <p class="text-xs text-muted-foreground">
                {{ 'overview.kpis.samples' | transloco: { count: report.transitDurationSamples } }}
              </p>
            </div>
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

  constructor() {
    this.store.loadReport();
    this.store.loadCounters();
  }
}
