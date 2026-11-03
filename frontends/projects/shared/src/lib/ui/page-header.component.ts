import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Page title with an optional eyebrow (section label), subtitle and a projected actions area. */
@Component({
  selector: 'pf-page-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="mb-6 flex flex-wrap items-start justify-between gap-4">
      <div class="min-w-0">
        @if (eyebrow()) {
          <p class="mb-1.5 text-xs font-semibold tracking-[0.08em] text-primary uppercase">
            {{ eyebrow() }}
          </p>
        }
        <h1 class="text-2xl font-semibold tracking-tight">{{ title() }}</h1>
        @if (subtitle()) {
          <p class="mt-1 text-muted-foreground">{{ subtitle() }}</p>
        }
      </div>
      <div class="flex flex-wrap gap-2">
        <ng-content />
      </div>
    </header>
  `,
})
export class PageHeaderComponent {
  readonly title = input.required<string>();
  readonly subtitle = input<string>();
  readonly eyebrow = input<string>();
}
