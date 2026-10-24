import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronLeft, lucideChevronRight } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';

/**
 * Previous/next pager for server-paged lists (Spartan pagination is router-link based).
 * Rendered as the bottom strip of a list panel: summary on the left, outline buttons on the right.
 */
@Component({
  selector: 'pf-pager',
  imports: [HlmButtonImports, NgIcon, TranslocoPipe],
  providers: [provideIcons({ lucideChevronLeft, lucideChevronRight })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav
      class="flex flex-wrap items-center justify-between gap-3 border-t px-5 py-3"
      [attr.aria-label]="'pager.label' | transloco"
    >
      <p class="text-sm text-muted-foreground" aria-live="polite">
        @if (totalKnown()) {
          {{ 'pager.summary' | transloco: { page: page(), pages: pageCount(), total: total() } }}
        } @else {
          {{ 'pager.page' | transloco: { page: page() } }}
        }
      </p>
      <div class="flex gap-2">
        <button
          hlmBtn
          type="button"
          variant="outline"
          size="sm"
          class="max-sm:h-10 max-sm:min-w-10"
          [disabled]="disabled() || !hasPrevious()"
          (click)="previous.emit()"
        >
          <ng-icon name="lucideChevronLeft" />
          <span class="hidden sm:inline">{{ 'common.previous' | transloco }}</span>
          <span class="sr-only sm:hidden">{{ 'common.previous' | transloco }}</span>
        </button>
        <button
          hlmBtn
          type="button"
          variant="outline"
          size="sm"
          class="max-sm:h-10 max-sm:min-w-10"
          [disabled]="disabled() || !hasNext()"
          (click)="next.emit()"
        >
          <span class="hidden sm:inline">{{ 'common.next' | transloco }}</span>
          <span class="sr-only sm:hidden">{{ 'common.next' | transloco }}</span>
          <ng-icon name="lucideChevronRight" />
        </button>
      </div>
    </nav>
  `,
})
export class PagerComponent {
  readonly page = input.required<number>();
  readonly pageCount = input(1);
  readonly total = input(0);
  readonly totalKnown = input(true);
  readonly hasPrevious = input.required<boolean>();
  readonly hasNext = input.required<boolean>();
  readonly disabled = input(false);
  readonly previous = output<void>();
  readonly next = output<void>();
}
