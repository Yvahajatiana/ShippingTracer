import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBell, lucideMail, lucideMessageSquare } from '@ng-icons/lucide';
import { ErrorBannerComponent, PageHeaderComponent, PfDatePipe } from '@parcelflow/shared';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { PagerComponent } from '../core/pager.component';
import { NotificationsStore } from './notifications.store';

/** Paged history of notifications, each linked to its shipment. */
@Component({
  selector: 'pf-notifications-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    NgIcon,
    PageHeaderComponent,
    ErrorBannerComponent,
    PfDatePipe,
    HlmBadgeImports,
    HlmCardImports,
    HlmEmptyImports,
    HlmSkeletonImports,
    PagerComponent,
  ],
  providers: [NotificationsStore, provideIcons({ lucideBell, lucideMail, lucideMessageSquare })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <pf-page-header
      [title]="'notifications.title' | transloco"
      [subtitle]="'notifications.subtitle' | transloco"
    />

    @if (store.error(); as error) {
      <pf-error-banner [error]="error" (retry)="store.reload()" />
    } @else if (store.empty()) {
      <div hlmEmpty class="border bg-card">
        <div hlmEmptyHeader>
          <div hlmEmptyMedia variant="icon"><ng-icon name="lucideBell" /></div>
          <h2 hlmEmptyTitle>{{ 'notifications.empty' | transloco }}</h2>
        </div>
      </div>
    } @else {
      <div hlmCard class="max-w-4xl py-0" [attr.aria-busy]="store.loading()">
        @if (store.loading() && store.items().length === 0) {
          <div class="grid gap-3 p-4">
            @for (row of [1, 2, 3]; track row) {
              <hlm-skeleton class="h-14 w-full" />
            }
          </div>
        } @else {
          <ul class="divide-y">
            @for (notification of store.items(); track notification.id) {
              <li class="flex gap-3 p-4">
                <span
                  class="flex size-9 shrink-0 items-center justify-center rounded-full bg-muted text-muted-foreground"
                  aria-hidden="true"
                >
                  <ng-icon
                    [name]="notification.channel === 'Email' ? 'lucideMail' : 'lucideMessageSquare'"
                  />
                </span>
                <div class="grid min-w-0 flex-1 gap-1">
                  <div class="flex flex-wrap items-center justify-between gap-2">
                    <p class="font-medium">{{ notification.subject }}</p>
                    <time
                      class="text-xs text-muted-foreground"
                      [attr.datetime]="notification.occurredAtUtc"
                    >
                      {{ notification.occurredAtUtc | pfDate }}
                    </time>
                  </div>
                  <p class="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
                    <span hlmBadge variant="outline">
                      {{ 'enum.NotificationChannel.' + notification.channel | transloco }}
                    </span>
                    <span class="break-all">{{ notification.destination }}</span>
                  </p>
                  <a
                    class="w-fit text-sm text-primary hover:underline"
                    [routerLink]="['/shipments', notification.shipmentId]"
                    >{{
                      'notifications.shipmentLink'
                        | transloco: { number: notification.trackingNumber }
                    }}</a
                  >
                </div>
              </li>
            }
          </ul>
        }
      </div>
      <pf-pager
        class="block max-w-4xl"
        [page]="store.page()"
        [total]="store.total()"
        [hasPrevious]="store.hasPrevious()"
        [hasNext]="store.hasNext()"
        [loading]="store.loading()"
        (previous)="store.previous()"
        (next)="store.next()"
      />
    }
  `,
})
export class NotificationsPage {
  protected readonly store = inject(NotificationsStore);

  constructor() {
    this.store.loadPage(1);
  }
}
