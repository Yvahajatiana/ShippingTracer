import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideRefreshCw, lucideWarehouse } from '@ng-icons/lucide';
import { ErrorBannerComponent, PageHeaderComponent, StatusTagComponent } from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { ActiveBadgeComponent } from '../core/active-badge.component';
import { PagerComponent } from '../core/pager.component';
import { EmptyStateComponent, ListSkeletonComponent } from '../core/states';
import { WarehouseListStore } from './warehouses.store';

@Component({
  selector: 'pf-warehouse-list-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    NgIcon,
    HlmButtonImports,
    HlmCardImports,
    HlmTableImports,
    ErrorBannerComponent,
    PageHeaderComponent,
    StatusTagComponent,
    ActiveBadgeComponent,
    PagerComponent,
    EmptyStateComponent,
    ListSkeletonComponent,
  ],
  providers: [WarehouseListStore, provideIcons({ lucideRefreshCw, lucideWarehouse })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <pf-page-header
      [title]="'warehouses.title' | transloco"
      [subtitle]="'warehouses.subtitle' | transloco"
    >
      <button hlmBtn variant="outline" type="button" (click)="store.reload()">
        <ng-icon name="lucideRefreshCw" />
        {{ 'common.refresh' | transloco }}
      </button>
    </pf-page-header>

    <div hlmCard>
      <div hlmCardContent>
        @if (store.error(); as error) {
          <pf-error-banner [error]="error" (retry)="store.reload()" />
        } @else if (store.loading() && !store.loaded()) {
          <pf-list-skeleton [rows]="3" />
        } @else if (store.isEmpty()) {
          <pf-empty-state icon="lucideWarehouse" [title]="'warehouses.empty' | transloco" />
        } @else {
          <div hlmTableContainer class="hidden md:block" [attr.aria-busy]="store.loading()">
            <table hlmTable>
              <thead hlmTHead>
                <tr hlmTr>
                  <th hlmTh>{{ 'warehouses.code' | transloco }}</th>
                  <th hlmTh>{{ 'warehouses.name' | transloco }}</th>
                  <th hlmTh>{{ 'warehouses.kind' | transloco }}</th>
                  <th hlmTh>{{ 'warehouses.city' | transloco }}</th>
                  <th hlmTh>{{ 'common.status' | transloco }}</th>
                </tr>
              </thead>
              <tbody hlmTBody>
                @for (warehouse of store.items(); track warehouse.id) {
                  <tr hlmTr>
                    <td hlmTd class="font-mono">{{ warehouse.code }}</td>
                    <td hlmTd>
                      <a
                        class="font-medium text-primary hover:underline"
                        [routerLink]="['/warehouses', warehouse.id]"
                        >{{ warehouse.name }}</a
                      >
                    </td>
                    <td hlmTd><pf-status-tag type="WarehouseKind" [value]="warehouse.kind" /></td>
                    <td hlmTd>{{ warehouse.city }} ({{ warehouse.countryCode }})</td>
                    <td hlmTd><pf-active-badge [active]="warehouse.isActive" /></td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <ul class="space-y-3 md:hidden">
            @for (warehouse of store.items(); track warehouse.id) {
              <li>
                <a
                  class="block rounded-lg border bg-background p-3 hover:bg-muted/50"
                  [routerLink]="['/warehouses', warehouse.id]"
                >
                  <div class="flex items-center justify-between gap-2">
                    <span class="font-medium text-primary">{{ warehouse.name }}</span>
                    <pf-active-badge [active]="warehouse.isActive" />
                  </div>
                  <p class="mt-1 text-sm text-muted-foreground">
                    <span class="font-mono">{{ warehouse.code }}</span> · {{ warehouse.city }}
                  </p>
                  <pf-status-tag
                    class="mt-2 inline-block"
                    type="WarehouseKind"
                    [value]="warehouse.kind"
                  />
                </a>
              </li>
            }
          </ul>
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
    </div>
  `,
})
export class WarehouseListPage {
  protected readonly store = inject(WarehouseListStore);
}
