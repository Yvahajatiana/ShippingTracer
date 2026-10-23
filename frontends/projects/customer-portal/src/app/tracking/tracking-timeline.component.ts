import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleCheck, lucideCircleDot, lucideMapPin } from '@ng-icons/lucide';
import { PfDatePipe, PublicTrackingEntry } from '@parcelflow/shared';

/** Vertical timeline of tracking events, most recent first. */
@Component({
  selector: 'pf-tracking-timeline',
  imports: [NgIcon, PfDatePipe, TranslocoPipe],
  providers: [provideIcons({ lucideCircleCheck, lucideCircleDot, lucideMapPin })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ol
      class="relative ms-3 border-s border-border"
      [attr.aria-label]="'tracking.history' | transloco"
    >
      @for (entry of entries(); track $index; let first = $first) {
        <li class="mb-6 ms-6 last:mb-0">
          <span
            class="absolute -start-3 flex size-6 items-center justify-center rounded-full ring-4 ring-card"
            [class]="
              first ? 'bg-primary text-primary-foreground' : 'bg-muted text-muted-foreground'
            "
            aria-hidden="true"
          >
            <ng-icon [name]="first ? 'lucideCircleCheck' : 'lucideCircleDot'" class="text-sm" />
          </span>
          <p class="font-medium" [class.text-primary]="first">
            {{ 'enum.ParcelStatus.' + entry.status | transloco }}
          </p>
          <time class="block text-sm text-muted-foreground" [attr.datetime]="entry.occurredAtUtc">
            {{ entry.occurredAtUtc | pfDate }}
          </time>
          @if (entry.facilityCode) {
            <p class="mt-1 flex items-center gap-1 text-sm text-muted-foreground">
              <ng-icon name="lucideMapPin" aria-hidden="true" />
              {{ 'tracking.facility' | transloco: { code: entry.facilityCode } }}
            </p>
          }
        </li>
      } @empty {
        <li class="ms-6 text-sm text-muted-foreground">{{ 'tracking.noEvents' | transloco }}</li>
      }
    </ol>
  `,
})
export class TrackingTimelineComponent {
  readonly entries = input.required<readonly PublicTrackingEntry[]>();
}
