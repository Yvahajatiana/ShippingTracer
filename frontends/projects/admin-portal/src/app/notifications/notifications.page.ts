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
      [title]="'notifications.title' | transloco"
      [subtitle]="'notifications.subtitle' | transloco"
    />

    <section hlmCard>
      <div hlmCardHeader>
        <form
          class="flex flex-col gap-3 sm:flex-row sm:items-start"
          role="search"
          [formGroup]="filter"
          (ngSubmit)="search()"
          novalidate
        >
          <div hlmField class="sm:max-w-md">
            <label hlmFieldLabel for="notification-shipment">
              {{ 'notifications.shipmentFilter' | transloco }}
            </label>
            <input
              hlmInput
              id="notification-shipment"
              class="font-mono"
              formControlName="shipmentId"
              autocomplete="off"
              [placeholder]="'notifications.shipmentPlaceholder' | transloco"
            />
            <pf-field-error [control]="filter.controls.shipmentId" />
          </div>
          <div class="flex gap-2 sm:mt-6">
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
        </form>
      </div>
      <div hlmCardContent>
        @if (store.error()) {
          <pf-error-banner [error]="store.error()" (retry)="store.reload()" />
        } @else if (store.loading() && store.items().length === 0) {
          <pf-list-skeleton />
        } @else if (store.items().length === 0) {
          <pf-empty-state titleKey="notifications.empty" />
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
                    <td hlmTd class="hidden whitespace-nowrap md:table-cell">
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
      </div>
    </section>

    <hlm-sheet side="right" [state]="sheetState()" (closed)="store.select(null)">
      <hlm-sheet-content *hlmSheetPortal class="overflow-y-auto p-6">
        @if (store.selected(); as notification) {
          <div hlmSheetHeader class="p-0 pr-8">
            <h2 hlmSheetTitle>{{ notification.subject }}</h2>
            <p hlmSheetDescription>{{ 'notifications.detailDescription' | transloco }}</p>
          </div>
          <dl class="grid gap-3">
            <div>
              <dt class="text-muted-foreground">
                {{ 'notifications.fields.channel' | transloco }}
              </dt>
              <dd><pf-status-tag type="NotificationChannel" [value]="notification.channel" /></dd>
            </div>
            <div>
              <dt class="text-muted-foreground">
                {{ 'notifications.fields.destination' | transloco }}
              </dt>
              <dd class="break-all">{{ notification.destination }}</dd>
            </div>
            <div>
              <dt class="text-muted-foreground">{{ 'common.status' | transloco }}</dt>
              <dd>
                <span hlmBadge variant="outline">{{ notification.deliveryStatus }}</span>
              </dd>
            </div>
            <div>
              <dt class="text-muted-foreground">
                {{ 'notifications.fields.trackingNumber' | transloco }}
              </dt>
              <dd class="font-mono">{{ notification.trackingNumber }}</dd>
            </div>
            <div>
              <dt class="text-muted-foreground">
                {{ 'notifications.fields.customer' | transloco }}
              </dt>
              <dd class="font-mono text-xs break-all">{{ notification.customerSubject }}</dd>
            </div>
            <div>
              <dt class="text-muted-foreground">
                {{ 'notifications.fields.occurredAt' | transloco }}
              </dt>
              <dd>{{ notification.occurredAtUtc | pfDate }}</dd>
            </div>
            <div>
              <dt class="text-muted-foreground">
                {{ 'notifications.fields.recordedAt' | transloco }}
              </dt>
              <dd>{{ notification.recordedAtUtc | pfDate }}</dd>
            </div>
            <div>
              <dt class="text-muted-foreground">{{ 'notifications.fields.body' | transloco }}</dt>
              <dd class="rounded-md border bg-muted/40 p-3 whitespace-pre-line">
                {{ notification.body }}
              </dd>
            </div>
            <div>
              <dt class="text-muted-foreground">
                {{ 'notifications.fields.shipment' | transloco }}
              </dt>
              <dd>
                <button
                  hlmBtn
                  type="button"
                  variant="link"
                  class="h-auto p-0 font-mono text-xs break-all whitespace-normal"
                  (click)="filterByShipment(notification)"
                >
                  {{ notification.shipmentId }}
                </button>
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
