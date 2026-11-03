import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePackage, lucidePackagePlus } from '@ng-icons/lucide';
import {
  ErrorBannerComponent,
  PageHeaderComponent,
  PfDatePipe,
  PfMoneyPipe,
  StatusTagComponent,
} from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { PagerComponent } from '../core/pager.component';
import { ShipmentsStore } from './shipments.store';

/** Paged list of the customer's shipments: table on desktop, cards on small screens. */
@Component({
  selector: 'pf-shipments-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    NgIcon,
    PageHeaderComponent,
    ErrorBannerComponent,
    StatusTagComponent,
    PfDatePipe,
    PfMoneyPipe,
    HlmButtonImports,
    HlmCardImports,
    HlmEmptyImports,
    HlmSkeletonImports,
    HlmTableImports,
    PagerComponent,
  ],
  providers: [ShipmentsStore, provideIcons({ lucidePackage, lucidePackagePlus })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <pf-page-header
      [title]="'shipments.title' | transloco"
      [subtitle]="'shipments.subtitle' | transloco"
    >
      <a hlmBtn routerLink="/shipments/new">
        <ng-icon name="lucidePackagePlus" />
        {{ 'shipments.new' | transloco }}
      </a>
    </pf-page-header>

    @if (store.error(); as error) {
      <pf-error-banner [error]="error" (retry)="store.reload()" />
    } @else if (store.empty()) {
      <div hlmEmpty class="border bg-card">
        <div hlmEmptyHeader>
          <div hlmEmptyMedia variant="icon"><ng-icon name="lucidePackage" /></div>
          <h2 hlmEmptyTitle>{{ 'shipments.empty.title' | transloco }}</h2>
          <p hlmEmptyDescription>{{ 'shipments.empty.message' | transloco }}</p>
        </div>
        <div hlmEmptyContent>
          <a hlmBtn routerLink="/shipments/new">
            <ng-icon name="lucidePackagePlus" />
            {{ 'shipments.new' | transloco }}
          </a>
        </div>
      </div>
    } @else {
      <div hlmCard class="py-0" [attr.aria-busy]="store.loading()">
        @if (store.loading() && store.items().length === 0) {
          <div class="grid gap-3 p-4">
            @for (row of [1, 2, 3, 4, 5]; track row) {
              <hlm-skeleton class="h-10 w-full" />
            }
          </div>
        } @else {
          <!-- Desktop: table -->
          <div hlmTableContainer class="hidden md:block">
            <table hlmTable>
              <caption class="sr-only">
                {{
                  'shipments.title' | transloco
                }}
              </caption>
              <thead hlmTHead>
                <tr hlmTr>
                  <th hlmTh scope="col" class="px-4">
                    {{ 'shipments.columns.trackingNumber' | transloco }}
                  </th>
                  <th hlmTh scope="col">{{ 'shipments.columns.recipient' | transloco }}</th>
                  <th hlmTh scope="col">{{ 'shipments.columns.zone' | transloco }}</th>
                  <th hlmTh scope="col">{{ 'common.status' | transloco }}</th>
                  <th hlmTh scope="col" class="text-end">
                    {{ 'shipments.columns.price' | transloco }}
                  </th>
                  <th hlmTh scope="col" class="px-4">{{ 'common.createdAt' | transloco }}</th>
                </tr>
              </thead>
              <tbody hlmTBody>
                @for (shipment of store.items(); track shipment.id) {
                  <tr hlmTr>
                    <td hlmTd class="px-4">
                      <a
                        class="font-mono font-medium text-primary hover:underline"
                        [routerLink]="['/shipments', shipment.id]"
                        >{{ shipment.trackingNumber }}</a
                      >
                    </td>
                    <td hlmTd>
                      {{ shipment.recipient.city }}
                      <span class="text-muted-foreground"
                        >· {{ shipment.recipient.countryCode }}</span
                      >
                    </td>
                    <td hlmTd>{{ 'enum.DeliveryZone.' + shipment.zone | transloco }}</td>
                    <td hlmTd><pf-status-tag type="ShipmentStatus" [value]="shipment.status" /></td>
                    <td hlmTd class="text-end tabular-nums">
                      {{ shipment.price | pfMoney: shipment.currency }}
                    </td>
                    <td hlmTd class="px-4">{{ shipment.createdAtUtc | pfDate: 'date' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>

          <!-- Mobile: cards -->
          <ul class="divide-y md:hidden">
            @for (shipment of store.items(); track shipment.id) {
              <li>
                <a
                  class="flex flex-col gap-2 p-4 hover:bg-muted/50"
                  [routerLink]="['/shipments', shipment.id]"
                >
                  <div class="flex items-center justify-between gap-2">
                    <span class="font-mono font-medium text-primary">{{
                      shipment.trackingNumber
                    }}</span>
                    <pf-status-tag type="ShipmentStatus" [value]="shipment.status" />
                  </div>
                  <div
                    class="flex items-center justify-between gap-2 text-sm text-muted-foreground"
                  >
                    <span>
                      {{ shipment.recipient.city }} · {{ shipment.recipient.countryCode }} ·
                      {{ 'enum.DeliveryZone.' + shipment.zone | transloco }}
                    </span>
                    <span class="font-medium text-foreground tabular-nums">
                      {{ shipment.price | pfMoney: shipment.currency }}
                    </span>
                  </div>
                  <span class="text-xs text-muted-foreground">
                    {{ shipment.createdAtUtc | pfDate: 'date' }}
                  </span>
                </a>
              </li>
            }
          </ul>
        }
      </div>
      <pf-pager
        [page]="store.page()"
        [hasPrevious]="store.hasPrevious()"
        [hasNext]="store.hasNext()"
        [loading]="store.loading()"
        (previous)="store.previous()"
        (next)="store.next()"
      />
    }
  `,
})
export class ShipmentsPage {
  protected readonly store = inject(ShipmentsStore);

  constructor() {
    this.store.loadPage(1);
  }
}
