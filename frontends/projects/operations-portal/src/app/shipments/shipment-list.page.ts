import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideRefreshCw, lucideSearch } from '@ng-icons/lucide';
import {
  ErrorBannerComponent,
  PageHeaderComponent,
  PfDatePipe,
  PfMoneyPipe,
  StatusTagComponent,
} from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { PagerComponent } from '../core/pager.component';
import { EmptyStateComponent, ListSkeletonComponent } from '../core/states';
import { ShipmentListStore } from './shipments.store';

@Component({
  selector: 'pf-shipment-list-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    NgIcon,
    HlmButtonImports,
    HlmCardImports,
    HlmInputImports,
    HlmLabelImports,
    HlmTableImports,
    ErrorBannerComponent,
    PageHeaderComponent,
    PfDatePipe,
    PfMoneyPipe,
    StatusTagComponent,
    PagerComponent,
    EmptyStateComponent,
    ListSkeletonComponent,
  ],
  providers: [ShipmentListStore, provideIcons({ lucideRefreshCw, lucideSearch })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <pf-page-header
      [title]="'shipments.title' | transloco"
      [subtitle]="'shipments.subtitle' | transloco"
    >
      <button hlmBtn variant="outline" type="button" (click)="store.reload()">
        <ng-icon name="lucideRefreshCw" />
        {{ 'common.refresh' | transloco }}
      </button>
    </pf-page-header>

    <div hlmCard>
      <div hlmCardContent>
        <div class="mb-4 grid max-w-sm gap-2">
          <label hlmLabel for="shipment-search">{{ 'shipments.search' | transloco }}</label>
          <div class="relative">
            <ng-icon
              name="lucideSearch"
              class="pointer-events-none absolute top-1/2 left-2.5 -translate-y-1/2 text-muted-foreground"
              aria-hidden="true"
            />
            <input
              hlmInput
              id="shipment-search"
              type="search"
              class="w-full pl-8"
              autocomplete="off"
              [placeholder]="'shipments.searchPlaceholder' | transloco"
              [value]="store.query()"
              (input)="store.setQuery($any($event.target).value)"
            />
          </div>
          <p class="text-xs text-muted-foreground">{{ 'shipments.searchHint' | transloco }}</p>
        </div>

        @if (store.error(); as error) {
          <pf-error-banner [error]="error" (retry)="store.reload()" />
        } @else if (store.loading() && !store.loaded()) {
          <pf-list-skeleton />
        } @else if (store.isEmpty()) {
          <pf-empty-state [title]="'shipments.empty' | transloco" />
        } @else if (store.filtered().length === 0) {
          <pf-empty-state
            icon="lucideSearch"
            [title]="'shipments.noMatch' | transloco: { query: store.query() }"
          />
        } @else {
          <div hlmTableContainer class="hidden md:block" [attr.aria-busy]="store.loading()">
            <table hlmTable>
              <thead hlmTHead>
                <tr hlmTr>
                  <th hlmTh>{{ 'shipments.trackingNumber' | transloco }}</th>
                  <th hlmTh>{{ 'shipments.recipient' | transloco }}</th>
                  <th hlmTh>{{ 'shipments.zone' | transloco }}</th>
                  <th hlmTh>{{ 'common.status' | transloco }}</th>
                  <th hlmTh class="text-right">{{ 'shipments.price' | transloco }}</th>
                  <th hlmTh>{{ 'common.createdAt' | transloco }}</th>
                </tr>
              </thead>
              <tbody hlmTBody>
                @for (shipment of store.filtered(); track shipment.id) {
                  <tr hlmTr>
                    <td hlmTd>
                      <a
                        class="font-mono font-medium text-primary hover:underline"
                        [routerLink]="['/shipments', shipment.id]"
                        >{{ shipment.trackingNumber }}</a
                      >
                    </td>
                    <td hlmTd>
                      {{ shipment.recipient.name }}
                      <span class="text-muted-foreground"
                        >· {{ shipment.recipient.city }} ({{
                          shipment.recipient.countryCode
                        }})</span
                      >
                    </td>
                    <td hlmTd><pf-status-tag type="DeliveryZone" [value]="shipment.zone" /></td>
                    <td hlmTd>
                      <pf-status-tag type="ShipmentStatus" [value]="shipment.status" />
                    </td>
                    <td hlmTd class="text-right">
                      {{ shipment.price | pfMoney: shipment.currency }}
                    </td>
                    <td hlmTd>{{ shipment.createdAtUtc | pfDate }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>

          <ul class="space-y-3 md:hidden">
            @for (shipment of store.filtered(); track shipment.id) {
              <li>
                <a
                  class="block rounded-lg border bg-background p-3 hover:bg-muted/50"
                  [routerLink]="['/shipments', shipment.id]"
                >
                  <div class="flex items-center justify-between gap-2">
                    <span class="font-mono font-medium text-primary">{{
                      shipment.trackingNumber
                    }}</span>
                    <pf-status-tag type="ShipmentStatus" [value]="shipment.status" />
                  </div>
                  <p class="mt-1 text-sm">
                    {{ shipment.recipient.name }} · {{ shipment.recipient.city }}
                  </p>
                  <div class="mt-2 flex items-center justify-between text-xs text-muted-foreground">
                    <pf-status-tag type="DeliveryZone" [value]="shipment.zone" />
                    <span>{{ shipment.createdAtUtc | pfDate: 'date' }}</span>
                  </div>
                </a>
              </li>
            }
          </ul>
        }

        <pf-pager
          [page]="store.page()"
          [hasPrevious]="store.hasPrevious()"
          [hasNext]="store.hasMore()"
          [disabled]="store.loading()"
          (previous)="store.previous()"
          (next)="store.next()"
        />
      </div>
    </div>
  `,
})
export class ShipmentListPage {
  protected readonly store = inject(ShipmentListStore);
}
