import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronLeft, lucideChevronRight } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';

/** Previous/next pager with the current page (and total when the server reports it). */
@Component({
  selector: 'pf-pager',
  imports: [TranslocoPipe, NgIcon, HlmButtonImports],
  providers: [provideIcons({ lucideChevronLeft, lucideChevronRight })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav
      class="flex flex-wrap items-center justify-between gap-2 pt-4"
      [attr.aria-label]="'paging.label' | transloco"
    >
      <p class="text-sm text-muted-foreground" aria-live="polite">
        @if (total() !== null) {
          {{ 'paging.pageWithTotal' | transloco: { page: page(), total: total() } }}
        } @else {
          {{ 'paging.page' | transloco: { page: page() } }}
        }
      </p>
      <div class="flex gap-2">
        <button
          hlmBtn
          variant="outline"
          size="sm"
          [disabled]="!hasPrevious() || loading()"
          (click)="previous.emit()"
        >
          <ng-icon name="lucideChevronLeft" />
          {{ 'common.previous' | transloco }}
        </button>
        <button
          hlmBtn
          variant="outline"
          size="sm"
          [disabled]="!hasNext() || loading()"
          (click)="next.emit()"
        >
          {{ 'common.next' | transloco }}
          <ng-icon name="lucideChevronRight" />
        </button>
      </div>
    </nav>
  `,
})
export class PagerComponent {
  readonly page = input.required<number>();
  readonly total = input<number | null>(null);
  readonly hasPrevious = input.required<boolean>();
  readonly hasNext = input.required<boolean>();
  readonly loading = input(false);
  readonly previous = output<void>();
  readonly next = output<void>();
}
