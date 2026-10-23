import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePackageSearch, lucideTriangleAlert } from '@ng-icons/lucide';
import {
  ErrorBannerComponent,
  PageHeaderComponent,
  PfDatePipe,
  StatusTagComponent,
} from '@parcelflow/shared';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { TrackingSearchComponent } from './tracking-search.component';
import { TrackingTimelineComponent } from './tracking-timeline.component';
import { TrackingStore } from './tracking.store';

/** Public tracking page, reachable without signing in. */
@Component({
  selector: 'pf-tracking-page',
  imports: [
    TranslocoPipe,
    NgIcon,
    PageHeaderComponent,
    ErrorBannerComponent,
    StatusTagComponent,
    PfDatePipe,
    HlmCardImports,
    HlmEmptyImports,
    HlmSkeletonImports,
    TrackingSearchComponent,
    TrackingTimelineComponent,
  ],
  providers: [TrackingStore, provideIcons({ lucidePackageSearch, lucideTriangleAlert })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <pf-page-header
      [title]="'tracking.title' | transloco: { number: trackingNumber() }"
      [subtitle]="'tracking.subtitle' | transloco"
    />

    <div class="grid max-w-3xl gap-6">
      @if (store.loading()) {
        <div hlmCard aria-busy="true">
          <div hlmCardContent class="grid gap-3">
            <hlm-skeleton class="h-6 w-48" />
            <hlm-skeleton class="h-4 w-64" />
            <hlm-skeleton class="h-24 w-full" />
          </div>
        </div>
      } @else if (store.notFound()) {
        <div hlmEmpty class="border bg-card">
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucidePackageSearch" /></div>
            <h2 hlmEmptyTitle>{{ 'tracking.notFound.title' | transloco }}</h2>
            <p hlmEmptyDescription>
              {{ 'tracking.notFound.message' | transloco: { number: trackingNumber() } }}
            </p>
          </div>
        </div>
      } @else if (store.invalidNumber()) {
        <div hlmEmpty class="border bg-card">
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideTriangleAlert" /></div>
            <h2 hlmEmptyTitle>{{ 'tracking.invalid.title' | transloco }}</h2>
            <p hlmEmptyDescription>{{ 'tracking.invalid.message' | transloco }}</p>
          </div>
        </div>
      } @else if (store.error(); as error) {
        <pf-error-banner [error]="error" (retry)="store.load(trackingNumber())" />
      } @else if (store.tracking(); as tracking) {
        <section hlmCard [attr.aria-labelledby]="'pf-tracking-current'">
          <div hlmCardHeader>
            <p class="text-sm text-muted-foreground">{{ 'tracking.number' | transloco }}</p>
            <h2 hlmCardTitle id="pf-tracking-current" class="font-mono text-lg">
              {{ tracking.trackingNumber }}
            </h2>
            <div hlmCardAction>
              <pf-status-tag type="ParcelStatus" [value]="tracking.currentStatus" />
            </div>
          </div>
          <div hlmCardContent class="grid gap-6">
            <p class="text-sm text-muted-foreground">
              {{ 'tracking.updatedAt' | transloco: { date: (tracking.updatedAtUtc | pfDate) } }}
            </p>
            <pf-tracking-timeline [entries]="store.timeline()" />
          </div>
        </section>
      }

      <section hlmCard>
        <div hlmCardHeader>
          <h2 hlmCardTitle>{{ 'tracking.search.another' | transloco }}</h2>
        </div>
        <div hlmCardContent>
          <pf-tracking-search inputId="pf-tracking-number-again" />
        </div>
      </section>
    </div>
  `,
})
export class TrackingPage {
  protected readonly store = inject(TrackingStore);
  /** Route parameter, bound by withComponentInputBinding. */
  readonly trackingNumber = input.required<string>();

  constructor() {
    // rxMethod tracks the signal: a new search from this page reloads the tracking.
    this.store.load(this.trackingNumber);
  }
}
