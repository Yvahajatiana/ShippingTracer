import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  afterRenderEffect,
  input,
  viewChild,
} from '@angular/core';
import type { Chart, ChartType } from 'chart.js';

export interface ChartSeries {
  label: string;
  data: number[];
}

export type PfChartType = Extract<ChartType, 'bar' | 'line' | 'doughnut'>;

/** Reads a theme token (e.g. `--chart-1`) so charts follow the Spartan palette and dark mode. */
function token(element: Element, name: string): string {
  return getComputedStyle(element).getPropertyValue(name).trim();
}

/**
 * Lightweight chart.js wrapper. chart.js is loaded on first render so it stays out of the initial bundle.
 */
@Component({
  selector: 'pf-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'relative block' },
  template: `<canvas #canvas role="img" [attr.aria-label]="ariaLabel()"></canvas>`,
})
export class ChartComponent implements OnDestroy {
  readonly type = input<PfChartType>('bar');
  readonly labels = input.required<string[]>();
  readonly series = input.required<ChartSeries[]>();
  readonly ariaLabel = input.required<string>();

  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private chart: Chart | null = null;

  constructor() {
    afterRenderEffect({
      write: (onCleanup) => {
        const type = this.type();
        const labels = this.labels();
        const series = this.series();
        const canvas = this.canvas().nativeElement;
        let cancelled = false;
        void import('chart.js/auto').then(({ default: ChartJs }) => {
          if (cancelled) {
            return;
          }
          const palette = [1, 2, 3, 4, 5].map((index) => token(canvas, `--chart-${index}`));
          const foreground = token(canvas, '--muted-foreground');
          const grid = token(canvas, '--border');
          const circular = type === 'doughnut';
          // Render axis and legend text with the portal's typeface rather than chart.js' default.
          ChartJs.defaults.font.family = getComputedStyle(canvas).fontFamily;
          this.chart?.destroy();
          this.chart = new ChartJs(canvas, {
            type,
            data: {
              labels,
              datasets: series.map((item, index) => ({
                label: item.label,
                data: item.data,
                backgroundColor: circular ? palette : palette[index % palette.length],
                borderColor: circular
                  ? token(canvas, '--background')
                  : palette[index % palette.length],
                borderRadius: type === 'bar' ? 4 : undefined,
                tension: 0.3,
              })),
            },
            options: {
              responsive: true,
              maintainAspectRatio: false,
              plugins: {
                legend: { display: circular || series.length > 1, labels: { color: foreground } },
              },
              scales: circular
                ? {}
                : {
                    // Keep axis labels horizontal; chart.js skips labels instead of tilting them.
                    x: {
                      ticks: { color: foreground, maxRotation: 0, autoSkipPadding: 12 },
                      grid: { display: false },
                    },
                    y: {
                      beginAtZero: true,
                      ticks: { color: foreground, precision: 0 },
                      grid: { color: grid },
                    },
                  },
            },
          });
        });
        onCleanup(() => {
          cancelled = true;
        });
      },
    });
  }

  ngOnDestroy(): void {
    this.chart?.destroy();
  }
}
