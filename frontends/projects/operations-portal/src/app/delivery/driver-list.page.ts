import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideRefreshCw, lucideTruck } from '@ng-icons/lucide';
import { ErrorBannerComponent, PageHeaderComponent } from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { ActiveBadgeComponent } from '../core/active-badge.component';
import { PagerComponent } from '../core/pager.component';
import { EmptyStateComponent, ListSkeletonComponent } from '../core/states';
import { DriverListStore } from './delivery-lists.store';

@Component({
  selector: 'pf-driver-list-page',
  imports: [
    TranslocoPipe,
    NgIcon,
    HlmButtonImports,
    HlmCardImports,
    HlmTableImports,
    ErrorBannerComponent,
    PageHeaderComponent,
    ActiveBadgeComponent,
    PagerComponent,
    EmptyStateComponent,
    ListSkeletonComponent,
  ],
  providers: [DriverListStore, provideIcons({ lucideRefreshCw, lucideTruck })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <pf-page-header
      [title]="'drivers.title' | transloco"
      [subtitle]="'drivers.subtitle' | transloco"
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
          <pf-empty-state icon="lucideTruck" [title]="'drivers.empty' | transloco" />
        } @else {
          <div hlmTableContainer [attr.aria-busy]="store.loading()">
            <table hlmTable>
              <thead hlmTHead>
                <tr hlmTr>
                  <th hlmTh>{{ 'drivers.name' | transloco }}</th>
                  <th hlmTh>{{ 'drivers.subject' | transloco }}</th>
                  <th hlmTh>{{ 'common.status' | transloco }}</th>
                </tr>
              </thead>
              <tbody hlmTBody>
                @for (driver of store.items(); track driver.subject) {
                  <tr hlmTr>
                    <td hlmTd class="font-medium">{{ driver.displayName }}</td>
                    <td hlmTd class="font-mono text-xs">{{ driver.subject }}</td>
                    <td hlmTd><pf-active-badge [active]="driver.isActive" /></td>
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
    </div>
  `,
})
export class DriverListPage {
  protected readonly store = inject(DriverListStore);
}
