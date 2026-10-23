import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideRefreshCw } from '@ng-icons/lucide';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { Notifier } from './notifier.service';

/** Inline error for a failed load, with an optional retry button. */
@Component({
  selector: 'pf-error-banner',
  imports: [HlmAlertImports, HlmButtonImports, NgIcon, TranslocoPipe],
  providers: [provideIcons({ lucideCircleAlert, lucideRefreshCw })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div hlmAlert variant="destructive">
      <ng-icon name="lucideCircleAlert" />
      <h4 hlmAlertTitle>{{ text().summary }}</h4>
      @if (text().detail) {
        <p hlmAlertDescription>{{ text().detail }}</p>
      }
      @if (retryable()) {
        <div hlmAlertAction>
          <button hlmBtn variant="outline" size="sm" (click)="retry.emit()">
            <ng-icon name="lucideRefreshCw" />
            {{ 'common.retry' | transloco }}
          </button>
        </div>
      }
    </div>
  `,
})
export class ErrorBannerComponent {
  private readonly notifier = inject(Notifier);
  readonly error = input.required<unknown>();
  readonly retryable = input(true);
  readonly retry = output<void>();

  readonly text = computed(() => this.notifier.describe(this.error()));
}
