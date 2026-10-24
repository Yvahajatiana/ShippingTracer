import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, translateSignal } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideChevronRight,
  lucideBuilding2,
  lucideReceipt,
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
  /** Highlights sections that need attention (e.g. unpaid invoices). */
  tone: 'default' | 'warning';
}

const SECTIONS: SectionLink[] = [
  {
    key: 'companies',
    labelKey: 'overview.sections.companies',
    route: '/companies',
    icon: 'lucideBuilding2',
    tone: 'default',
  },
  {
    key: 'warehouses',
    labelKey: 'overview.sections.warehouses',
    route: '/warehouses',
    icon: 'lucideWarehouse',
    tone: 'default',
  },
  {
    key: 'drivers',
    labelKey: 'overview.sections.drivers',
    route: '/drivers',
    icon: 'lucideTruck',
    tone: 'default',
  },
  {
    key: 'pendingInvoices',
    labelKey: 'overview.sections.pendingInvoices',
    route: '/billing/invoices',
    icon: 'lucideReceipt',
    tone: 'warning',
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
      lucideChevronRight,
      lucideBuilding2,
      lucideReceipt,
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
    >
      @if (store.report(); as report) {
        <p class="rounded-md border bg-card px-3 py-1.5 font-mono text-[13px] tabular-nums">
          <span class="sr-only">
            {{
              'overview.chart.range'
                | transloco
                  : { from: (report.from | pfDate: 'date'), to: (report.to | pfDate: 'date') }
            }}
          </span>
          <span aria-hidden="true">
            {{ report.from | pfDate: 'date' }} → {{ report.to | pfDate: 'date' }}
          </span>
        </p>
      }
    </pf-page-header>

    <section aria-labelledby="overview-kpi-title" class="mb-4">
      <h2 id="overview-kpi-title" class="sr-only">{{ 'overview.kpiTitle' | transloco }}</h2>
      @if (store.error()) {
        <pf-error-banner [error]="store.error()" (retry)="store.loadReport()" />
      } @else if (!store.report()) {
        <hlm-skeleton
          class="block h-32 w-full rounded-md"
          role="status"
          [attr.aria-label]="'common.loading' | transloco"
        />
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
      }
    </section>

    <div class="flex flex-wrap items-start gap-4">
      @if (!store.error()) {
        <section
          hlmCard
          class="min-w-0 flex-[2_1_560px] gap-0 py-0"
          aria-labelledby="overview-chart-title"
        >
          <div class="flex flex-wrap items-center justify-between gap-2 border-b px-6 py-4">
            <h2 id="overview-chart-title" class="text-[15px] font-semibold">
              {{ 'overview.chart.title' | transloco }}
            </h2>
            @if (store.report(); as report) {
              <p class="text-sm text-muted-foreground">
                {{
                  'overview.chart.range'
                    | transloco
                      : { from: (report.from | pfDate: 'date'), to: (report.to | pfDate: 'date') }
                }}
              </p>
            }
          </div>
          <div class="px-6 py-5">
            @if (store.report(); as report) {
              <pf-chart
                type="bar"
                class="h-72"
                [labels]="chartLabels()"
                [series]="chartSeries()"
                [ariaLabel]="
                  'overview.chart.ariaLabel' | transloco: { total: report.totalShipments }
                "
              />
            } @else {
              <hlm-skeleton
                class="block h-72 w-full rounded-md"
                role="status"
                [attr.aria-label]="'common.loading' | transloco"
              />
            }
          </div>
        </section>
      }

      <section
        hlmCard
        class="min-w-0 flex-[1_1_300px] gap-0 py-0"
        aria-labelledby="overview-sections-title"
      >
        <div class="border-b px-6 py-4">
          <h2 id="overview-sections-title" class="text-[15px] font-semibold">
            {{ 'overview.sectionsTitle' | transloco }}
          </h2>
        </div>
        <ul>
          @for (section of sections; track section.key) {
            <li class="border-b last:border-b-0">
              <a
                class="flex items-center gap-3 px-6 py-3.5 transition-colors hover:bg-muted/50 focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none focus-visible:ring-inset"
                [routerLink]="section.route"
              >
                <span
                  class="grid size-9 shrink-0 place-items-center rounded-md"
                  [class]="
                    section.tone === 'warning'
                      ? 'bg-warning/15 text-warning'
                      : 'bg-accent text-primary'
                  "
                  aria-hidden="true"
                >
                  <ng-icon [name]="section.icon" size="1.125rem" />
                </span>
                <span class="min-w-0 flex-1 text-sm font-medium">
                  {{ section.labelKey | transloco }}
                </span>
                @if (store.countersLoading()) {
                  <hlm-skeleton class="h-6 w-10" />
                } @else {
                  <span
                    class="text-lg font-semibold tabular-nums"
                    [class.text-warning]="section.tone === 'warning'"
                  >
                    {{ store.counters()[section.key] ?? '—' }}
                  </span>
                }
                <ng-icon
                  name="lucideChevronRight"
                  class="shrink-0 text-muted-foreground"
                  aria-hidden="true"
                />
              </a>
            </li>
          }
        </ul>
      </section>
    </div>
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
