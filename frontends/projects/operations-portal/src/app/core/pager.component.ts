import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronLeft, lucideChevronRight } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';

/** Previous/next pagination for lists, with the total page count when known. */
@Component({
  selector: 'pf-pager',
  imports: [HlmButtonImports, NgIcon, TranslocoPipe],
  providers: [provideIcons({ lucideChevronLeft, lucideChevronRight })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    <nav
      class="flex items-center justify-between gap-2 pt-4"
      [attr.aria-label]="label() ?? ('pager.label' | transloco)"
    >
      <button
        hlmBtn
        variant="outline"
        size="sm"
        type="button"
        [disabled]="disabled() || !hasPrevious()"
        (click)="previous.emit()"
      >
        <ng-icon name="lucideChevronLeft" />
        <span class="hidden sm:inline">{{ 'common.previous' | transloco }}</span>
        <span class="sr-only sm:hidden">{{ 'common.previous' | transloco }}</span>
      </button>
      <span class="text-sm text-muted-foreground" aria-live="polite">
        @if (pageCount(); as count) {
          {{ 'pager.pageOf' | transloco: { page: page(), count } }}
        } @else {
          {{ 'pager.page' | transloco: { page: page() } }}
        }
      </span>
      <button
        hlmBtn
        variant="outline"
        size="sm"
        type="button"
        [disabled]="disabled() || !hasNext()"
        (click)="next.emit()"
      >
        <span class="hidden sm:inline">{{ 'common.next' | transloco }}</span>
        <span class="sr-only sm:hidden">{{ 'common.next' | transloco }}</span>
        <ng-icon name="lucideChevronRight" />
      </button>
    </nav>
  `,
})
export class PagerComponent {
  /** Accessible name of the navigation; defaults to the generic pagination label. */
  readonly label = input<string | null>(null);
  readonly page = input.required<number>();
  readonly pageCount = input<number | null>(null);
  readonly hasPrevious = input.required<boolean>();
  readonly hasNext = input.required<boolean>();
  readonly disabled = input(false);
  readonly previous = output<void>();
  readonly next = output<void>();
}
