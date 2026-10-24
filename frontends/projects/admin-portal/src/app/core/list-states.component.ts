import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideInbox } from '@ng-icons/lucide';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';

/** Placeholder rows shown while a list loads; the host page provides the surrounding padding. */
@Component({
  selector: 'pf-list-skeleton',
  imports: [HlmSkeletonImports, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="grid gap-3" role="status" [attr.aria-label]="'common.loading' | transloco">
      @for (row of rowIndexes(); track row) {
        <hlm-skeleton class="h-10 w-full" />
      }
    </div>
  `,
})
export class ListSkeletonComponent {
  readonly rows = input(5);
  protected readonly rowIndexes = computed(() => Array.from({ length: this.rows() }, (_, i) => i));
}

/** Empty state for lists without results; the host page provides the surrounding padding. */
@Component({
  selector: 'pf-empty-state',
  imports: [HlmEmptyImports, NgIcon, TranslocoPipe],
  providers: [provideIcons({ lucideInbox })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <hlm-empty class="border border-dashed">
      <hlm-empty-header>
        <hlm-empty-media variant="icon">
          <ng-icon name="lucideInbox" />
        </hlm-empty-media>
        <p hlmEmptyTitle>{{ titleKey() | transloco }}</p>
        @if (descriptionKey(); as description) {
          <p hlmEmptyDescription>{{ description | transloco }}</p>
        }
      </hlm-empty-header>
      <ng-content />
    </hlm-empty>
  `,
})
export class EmptyStateComponent {
  readonly titleKey = input('common.empty');
  readonly descriptionKey = input<string>();
}
