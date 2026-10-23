import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  input,
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowLeft, lucidePackageOpen, lucideRefreshCw } from '@ng-icons/lucide';
import {
  ErrorBannerComponent,
  PageHeaderComponent,
  PfDatePipe,
  StatusTagComponent,
} from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { ActiveBadgeComponent } from '../core/active-badge.component';
import { PagerComponent } from '../core/pager.component';
import { EmptyStateComponent, ListSkeletonComponent } from '../core/states';
import { ScanPanelComponent } from './scan-panel.component';
import { WarehouseDetailStore } from './warehouses.store';

@Component({
  selector: 'pf-warehouse-detail-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    NgIcon,
    HlmButtonImports,
    HlmCardImports,
    HlmSkeletonImports,
    HlmTableImports,
    ErrorBannerComponent,
    PageHeaderComponent,
    PfDatePipe,
    StatusTagComponent,
    ActiveBadgeComponent,
    PagerComponent,
    EmptyStateComponent,
    ListSkeletonComponent,
    ScanPanelComponent,
  ],
  providers: [
    WarehouseDetailStore,
    provideIcons({ lucideArrowLeft, lucidePackageOpen, lucideRefreshCw }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a hlmBtn variant="ghost" size="sm" class="mb-2 -ml-2" routerLink="/warehouses">
      <ng-icon name="lucideArrowLeft" />
      {{ 'warehouses.backToList' | transloco }}
    </a>

    @if (store.warehouseError(); as error) {
      <pf-error-banner [error]="error" (retry)="store.load(id())" />
    } @else if (store.warehouseLoading() && !store.warehouse()) {
      <hlm-skeleton class="mb-6 h-10 w-64" />
      <pf-list-skeleton [rows]="6" />
    } @else if (store.warehouse(); as warehouse) {
      <pf-page-header [title]="warehouse.name" [subtitle]="warehouse.code">
        <pf-status-tag type="WarehouseKind" [value]="warehouse.kind" />
        <pf-active-badge [active]="warehouse.isActive" />
      </pf-page-header>

      <div class="grid gap-6 lg:grid-cols-5">
        <div class="space-y-6 lg:col-span-2">
          <pf-scan-panel />
          <section hlmCard>
            <div hlmCardHeader>
              <h2 hlmCardTitle>{{ 'warehouses.address' | transloco }}</h2>
            </div>
            <address hlmCardContent class="not-italic">
              <p>{{ warehouse.street }}</p>
              <p>{{ warehouse.postalCode }} {{ warehouse.city }} · {{ warehouse.countryCode }}</p>
            </address>
          </section>
        </div>

        <section hlmCard class="lg:col-span-3" data-testid="stock">
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'warehouses.stock.title' | transloco }}</h2>
            <p hlmCardDescription>
              @if (store.total() !== null) {
                {{ 'warehouses.stock.count' | transloco: { count: store.total() } }}
              }
            </p>
            <div hlmCardAction>
              <button
                hlmBtn
                variant="ghost"
                size="icon-sm"
                type="button"
                [attr.aria-label]="'common.refresh' | transloco"
                (click)="store.reload()"
              >
                <ng-icon name="lucideRefreshCw" />
              </button>
            </div>
          </div>
          <div hlmCardContent>
            @if (store.error(); as stockError) {
              <pf-error-banner [error]="stockError" (retry)="store.reload()" />
            } @else if (store.loading() && !store.loaded()) {
              <pf-list-skeleton [rows]="4" />
            } @else if (store.isEmpty()) {
              <pf-empty-state
                icon="lucidePackageOpen"
                [title]="'warehouses.stock.empty' | transloco"
              />
            } @else {
              <div hlmTableContainer [attr.aria-busy]="store.loading()">
                <table hlmTable>
                  <thead hlmTHead>
                    <tr hlmTr>
                      <th hlmTh>{{ 'shipments.trackingNumber' | transloco }}</th>
                      <th hlmTh>{{ 'warehouses.stock.lastScan' | transloco }}</th>
                    </tr>
                  </thead>
                  <tbody hlmTBody>
                    @for (parcel of store.items(); track parcel.shipmentId) {
                      <tr hlmTr>
                        <td hlmTd>
                          <a
                            class="font-mono font-medium text-primary hover:underline"
                            [routerLink]="['/shipments', parcel.shipmentId]"
                            >{{ parcel.trackingNumber }}</a
                          >
                        </td>
                        <td hlmTd>{{ parcel.lastScanAtUtc | pfDate }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
            <pf-pager
              [page]="store.page()"
              [pageCount]="store.pageCount()"
              [hasPrevious]="store.hasPrevious()"
              [hasNext]="store.hasMore()"
              [disabled]="store.loading()"
              (previous)="store.previous()"
              (next)="store.next()"
            />
          </div>
        </section>
      </div>
    }
  `,
})
export class WarehouseDetailPage {
  /** Route parameter. */
  readonly id = input.required<string>();
  protected readonly store = inject(WarehouseDetailStore);

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => this.store.load(id));
    });
  }
}
