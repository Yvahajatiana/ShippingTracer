import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';

/** Active / inactive badge for warehouses and drivers. */
@Component({
  selector: 'pf-active-badge',
  imports: [HlmBadgeImports, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      hlmBadge
      variant="secondary"
      [class]="active() ? 'bg-success/15 text-success' : 'bg-secondary text-muted-foreground'"
    >
      {{ (active() ? 'common.active' : 'common.inactive') | transloco }}
    </span>
  `,
})
export class ActiveBadgeComponent {
  readonly active = input.required<boolean>();
}
