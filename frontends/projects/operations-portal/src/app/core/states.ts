import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideInbox } from '@ng-icons/lucide';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';

/** Placeholder rows shown while a list loads. */
@Component({
  selector: 'pf-list-skeleton',
  imports: [HlmSkeletonImports, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    <div class="space-y-3" role="status" [attr.aria-label]="'common.loading' | transloco">
      @for (row of placeholders(); track $index) {
        <hlm-skeleton class="h-10 w-full" />
      }
    </div>
  `,
})
export class ListSkeletonComponent {
  readonly rows = input(5);
  protected placeholders(): number[] {
    return Array.from({ length: this.rows() }, (_, index) => index);
  }
}

/** Empty state with an icon, a title and an optional description. */
@Component({
  selector: 'pf-empty-state',
  imports: [HlmEmptyImports, NgIcon],
  providers: [provideIcons({ lucideInbox })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    <div hlmEmpty class="border p-8">
      <div hlmEmptyHeader>
        <div hlmEmptyMedia variant="icon">
          <ng-icon [name]="icon()" />
        </div>
        <div hlmEmptyTitle>{{ title() }}</div>
        @if (description()) {
          <p hlmEmptyDescription>{{ description() }}</p>
        }
      </div>
      <ng-content />
    </div>
  `,
})
export class EmptyStateComponent {
  readonly title = input.required<string>();
  readonly description = input<string>();
  readonly icon = input('lucideInbox');
}
