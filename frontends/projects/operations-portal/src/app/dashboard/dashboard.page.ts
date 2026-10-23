import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import {
  AbstractControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCalendarRange,
  lucideClock,
  lucidePackage,
  lucidePackageCheck,
  lucidePercent,
  lucideTruck,
} from '@ng-icons/lucide';
import {
  ChartComponent,
  ChartSeries,
  ErrorBannerComponent,
  FieldErrorComponent,
  LanguageService,
  PageHeaderComponent,
  PfDatePipe,
  PfNumberPipe,
  enumKey,
  formatDate,
} from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { EmptyStateComponent } from '../core/states';
import { KpiStore } from './kpi.store';

function rangeOrder(group: AbstractControl): ValidationErrors | null {
  const { from, to } = group.value as { from: string; to: string };
  return from && to && from > to ? { rangeOrder: true } : null;
}

@Component({
  selector: 'pf-dashboard-page',
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    NgIcon,
    HlmButtonImports,
    HlmCardImports,
    HlmInputImports,
    HlmLabelImports,
    HlmSkeletonImports,
    ChartComponent,
    ErrorBannerComponent,
    FieldErrorComponent,
    PageHeaderComponent,
    PfDatePipe,
    PfNumberPipe,
    EmptyStateComponent,
  ],
  providers: [
    KpiStore,
    provideIcons({
      lucideCalendarRange,
      lucideClock,
      lucidePackage,
      lucidePackageCheck,
      lucidePercent,
      lucideTruck,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard.page.html',
})
export class DashboardPage {
  protected readonly store = inject(KpiStore);
  private readonly transloco = inject(TranslocoService);
  private readonly language = inject(LanguageService);

  protected readonly form = inject(NonNullableFormBuilder).group(
    {
      from: [this.store.from(), Validators.required],
      to: [this.store.to(), Validators.required],
    },
    { validators: rangeOrder },
  );

  /** Daily volume labels follow the UI locale. */
  protected readonly dailyLabels = computed(() => {
    const locale = this.language.locale();
    return this.store.dailyLabels().map((date) => formatDate(date, locale, 'date'));
  });

  protected readonly dailySeries = computed<ChartSeries[]>(() => {
    this.language.language();
    return [
      {
        label: this.transloco.translate('dashboard.charts.shipments'),
        data: this.store.dailyCounts(),
      },
    ];
  });

  protected readonly statusLabels = computed(() => {
    this.language.language();
    return this.store
      .statusVolumes()
      .map((item) => this.transloco.translate(enumKey('ParcelStatus', item.status)));
  });

  protected readonly statusSeries = computed<ChartSeries[]>(() => {
    this.language.language();
    return [
      {
        label: this.transloco.translate('dashboard.charts.shipments'),
        data: this.store.statusVolumes().map((item) => item.shipments),
      },
    ];
  });

  constructor() {
    // Keeps the inputs aligned when the store range changes (e.g. reset).
    effect(() => {
      this.form.setValue({ from: this.store.from(), to: this.store.to() }, { emitEvent: false });
    });
  }

  protected apply(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      return;
    }
    const { from, to } = this.form.getRawValue();
    this.store.setRange(from, to);
  }

  protected reset(): void {
    this.store.resetRange();
  }
}
