import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEye, lucideFilterX, lucideSearch } from '@ng-icons/lucide';
import {
  ErrorBannerComponent,
  FieldErrorComponent,
  NotificationRecord,
  PageHeaderComponent,
  PfDatePipe,
  StatusTagComponent,
} from '@parcelflow/shared';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSheetImports } from '@spartan-ng/helm/sheet';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { GUID_PATTERN } from '../core/form-errors';
import { EmptyStateComponent, ListSkeletonComponent } from '../core/list-states.component';
import { PagerComponent } from '../core/pager.component';
import { NotificationsStore } from './notifications.store';

@Component({
  selector: 'pf-notifications-page',
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    NgIcon,
    PfDatePipe,
    PageHeaderComponent,
    ErrorBannerComponent,
    FieldErrorComponent,
    StatusTagComponent,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmFieldImports,
    HlmInputImports,
    HlmSheetImports,
    HlmTableImports,
    EmptyStateComponent,
    ListSkeletonComponent,
    PagerComponent,
  ],
  providers: [NotificationsStore, provideIcons({ lucideEye, lucideFilterX, lucideSearch })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <pf-page-header
      [eyebrow]="'nav.groups.finance' | transloco"
      [title]="'notifications.title' | transloco"
      [subtitle]="'notifications.subtitle' | transloco"
    />

    <section hlmCard class="gap-0 py-0">
      <!-- Toolbar strip: shipment filter and total count -->
      <form
        class="flex flex-wrap items-start gap-3 border-b px-5 py-3.5"
        role="search"
        [formGroup]="filter"
        (ngSubmit)="search()"
        novalidate
      >
        <label for="notification-shipment" class="py-2 text-sm font-medium">
          {{ 'notifications.shipmentFilter' | transloco }}
        </label>
        <div hlmField class="w-full sm:w-80">
          <input
            hlmInput
            id="notification-shipment"
            class="font-mono text-[13px]"
            formControlName="shipmentId"
            autocomplete="off"
            [placeholder]="'notifications.shipmentPlaceholder' | transloco"
          />
          <pf-field-error [control]="filter.controls.shipmentId" />
        </div>
        <div class="flex gap-2">
          <button hlmBtn type="submit" variant="secondary" [disabled]="store.loading()">
            <ng-icon name="lucideSearch" />
            {{ 'common.search' | transloco }}
          </button>
          @if (store.query()) {
            <button hlmBtn type="button" variant="ghost" (click)="clear()">
              <ng-icon name="lucideFilterX" />
              {{ 'notifications.clearFilter' | transloco }}
            </button>
          }
        </div>
        @if (store.items().length > 0) {
          <p class="ml-auto py-2 text-sm text-muted-foreground tabular-nums">
            {{ 'notifications.totalCount' | transloco: { total: store.total() } }}
          </p>
        }
      </form>

      @if (store.error()) {
        <div class="px-5 py-4">
          <pf-error-banner [error]="store.error()" (retry)="store.reload()" />
        </div>
      } @else if (store.loading() && store.items().length === 0) {
        <div class="px-5 py-3">
          <pf-list-skeleton />
        </div>
      } @else if (store.items().length === 0) {
        <div class="p-5">
          <pf-empty-state titleKey="notifications.empty" />
        </div>
      } @else {
        <div hlmTableContainer [attr.aria-busy]="store.loading()">
          <table hlmTable>
            <caption class="sr-only">
              {{
                'notifications.title' | transloco
              }}
            </caption>
            <thead hlmTHead>
              <tr hlmTr>
                <th hlmTh scope="col" class="hidden md:table-cell">
                  {{ 'notifications.fields.recordedAt' | transloco }}
                </th>
                <th hlmTh scope="col">{{ 'notifications.fields.subject' | transloco }}</th>
                <th hlmTh scope="col" class="hidden sm:table-cell">
                  {{ 'notifications.fields.channel' | transloco }}
                </th>
                <th hlmTh scope="col" class="hidden lg:table-cell">
                  {{ 'notifications.fields.destination' | transloco }}
                </th>
                <th hlmTh scope="col" class="hidden lg:table-cell">
                  {{ 'common.status' | transloco }}
                </th>
                <th hlmTh scope="col" class="text-right">
                  <span class="sr-only">{{ 'common.details' | transloco }}</span>
                </th>
              </tr>
            </thead>
            <tbody hlmTBody>
              @for (notification of store.items(); track notification.id) {
                <tr hlmTr>
                  <td
                    hlmTd
                    class="hidden whitespace-nowrap text-muted-foreground tabular-nums md:table-cell"
                  >
                    {{ notification.recordedAtUtc | pfDate }}
                  </td>
                  <td hlmTd>
                    <div class="font-medium">{{ notification.subject }}</div>
                    <div class="font-mono text-xs text-muted-foreground">
                      {{ notification.trackingNumber }}
                    </div>
                  </td>
                  <td hlmTd class="hidden sm:table-cell">
                    <pf-status-tag type="NotificationChannel" [value]="notification.channel" />
                  </td>
                  <td hlmTd class="hidden break-all lg:table-cell">
                    {{ notification.destination }}
                  </td>
                  <td hlmTd class="hidden lg:table-cell">
                    <span hlmBadge variant="outline">{{ notification.deliveryStatus }}</span>
                  </td>
                  <td hlmTd class="text-right">
                    <button
                      hlmBtn
                      type="button"
                      variant="ghost"
                      size="icon-sm"
                      [attr.aria-label]="
                        'notifications.openNamed' | transloco: { subject: notification.subject }
                      "
                      (click)="store.select(notification)"
                    >
                      <ng-icon name="lucideEye" />
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }

      <!-- The pager renders its own bottom strip. -->
      <pf-pager
        [page]="store.page()"
        [pageCount]="store.pageCount()"
        [total]="store.total()"
        [hasPrevious]="store.hasPrevious()"
        [hasNext]="store.hasNext()"
        [disabled]="store.loading()"
        (previous)="store.previousPage()"
        (next)="store.nextPage()"
      />
    </section>

    <hlm-sheet side="right" [state]="sheetState()" (closed)="store.select(null)">
      <hlm-sheet-content *hlmSheetPortal class="overflow-y-auto p-6">
        @if (store.selected(); as notification) {
          <div hlmSheetHeader class="p-0 pr-8">
            <h2 hlmSheetTitle>{{ notification.subject }}</h2>
            <p hlmSheetDescription>{{ 'notifications.detailDescription' | transloco }}</p>
          </div>
          <dl>
            <div class="flex items-center justify-between gap-3 border-b py-2.5">
              <dt class="text-[13px] text-muted-foreground">
                {{ 'notifications.fields.channel' | transloco }}
              </dt>
              <dd class="text-right">
                <pf-status-tag type="NotificationChannel" [value]="notification.channel" />
              </dd>
            </div>
            <div class="flex justify-between gap-3 border-b py-2.5">
              <dt class="shrink-0 text-[13px] text-muted-foreground">
                {{ 'notifications.fields.destination' | transloco }}
              </dt>
              <dd class="min-w-0 text-right text-sm break-all">{{ notification.destination }}</dd>
            </div>
            <div class="flex items-center justify-between gap-3 border-b py-2.5">
              <dt class="text-[13px] text-muted-foreground">{{ 'common.status' | transloco }}</dt>
              <dd class="text-right">
                <span hlmBadge variant="outline">{{ notification.deliveryStatus }}</span>
              </dd>
            </div>
            <div class="flex justify-between gap-3 border-b py-2.5">
              <dt class="shrink-0 text-[13px] text-muted-foreground">
                {{ 'notifications.fields.trackingNumber' | transloco }}
              </dt>
              <dd class="min-w-0 text-right font-mono text-[13px] break-all">
                {{ notification.trackingNumber }}
              </dd>
            </div>
            <div class="flex justify-between gap-3 border-b py-2.5">
              <dt class="shrink-0 text-[13px] text-muted-foreground">
                {{ 'notifications.fields.customer' | transloco }}
              </dt>
              <dd class="min-w-0 text-right font-mono text-[13px] break-all">
                {{ notification.customerSubject }}
              </dd>
            </div>
            <div class="flex justify-between gap-3 border-b py-2.5">
              <dt class="shrink-0 text-[13px] text-muted-foreground">
                {{ 'notifications.fields.occurredAt' | transloco }}
              </dt>
              <dd class="text-right text-sm tabular-nums">
                {{ notification.occurredAtUtc | pfDate }}
              </dd>
            </div>
            <div class="flex justify-between gap-3 border-b py-2.5">
              <dt class="shrink-0 text-[13px] text-muted-foreground">
                {{ 'notifications.fields.recordedAt' | transloco }}
              </dt>
              <dd class="text-right text-sm tabular-nums">
                {{ notification.recordedAtUtc | pfDate }}
              </dd>
            </div>
            <div class="flex justify-between gap-3 border-b py-2.5">
              <dt class="shrink-0 text-[13px] text-muted-foreground">
                {{ 'notifications.fields.shipment' | transloco }}
              </dt>
              <dd class="min-w-0 text-right">
                <button
                  hlmBtn
                  type="button"
                  variant="link"
                  class="h-auto p-0 text-right font-mono text-[13px] break-all whitespace-normal"
                  (click)="filterByShipment(notification)"
                >
                  {{ notification.shipmentId }}
                </button>
              </dd>
            </div>
            <div class="pt-4">
              <dt class="mb-2 text-[13px] text-muted-foreground">
                {{ 'notifications.fields.body' | transloco }}
              </dt>
              <dd class="rounded-md border bg-muted/40 p-3 text-sm whitespace-pre-line">
                {{ notification.body }}
              </dd>
            </div>
          </dl>
        }
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
export class NotificationsPage {
  protected readonly store = inject(NotificationsStore);

  protected readonly filter = inject(NonNullableFormBuilder).group({
    shipmentId: ['', Validators.pattern(GUID_PATTERN)],
  });

  protected readonly sheetState = computed(() => (this.store.selected() ? 'open' : 'closed'));

  constructor() {
    this.store.loadPage(1);
  }

  search(): void {
    if (this.filter.invalid) {
      this.filter.markAllAsTouched();
      return;
    }
    const shipmentId = this.filter.controls.shipmentId.value.trim();
    this.store.search(shipmentId || null);
  }

  clear(): void {
    this.filter.reset({ shipmentId: '' });
    this.store.search(null);
  }

  /** Narrows the audit list to the shipment of the opened notification. */
  filterByShipment(notification: NotificationRecord): void {
    this.store.select(null);
    this.filter.setValue({ shipmentId: notification.shipmentId });
    this.store.search(notification.shipmentId);
  }
}
