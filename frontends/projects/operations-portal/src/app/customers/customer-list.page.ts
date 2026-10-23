import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideRefreshCw, lucideUsers } from '@ng-icons/lucide';
import { ErrorBannerComponent, PageHeaderComponent, PfDatePipe } from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { PagerComponent } from '../core/pager.component';
import { EmptyStateComponent, ListSkeletonComponent } from '../core/states';
import { CustomerListStore } from './customers.store';

@Component({
  selector: 'pf-customer-list-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    NgIcon,
    HlmButtonImports,
    HlmCardImports,
    HlmTableImports,
    ErrorBannerComponent,
    PageHeaderComponent,
    PfDatePipe,
    PagerComponent,
    EmptyStateComponent,
    ListSkeletonComponent,
  ],
  providers: [CustomerListStore, provideIcons({ lucideRefreshCw, lucideUsers })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <pf-page-header
      [title]="'customers.title' | transloco"
      [subtitle]="'customers.subtitle' | transloco"
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
          <pf-list-skeleton />
        } @else if (store.isEmpty()) {
          <pf-empty-state icon="lucideUsers" [title]="'customers.empty' | transloco" />
        } @else {
          <div hlmTableContainer class="hidden md:block" [attr.aria-busy]="store.loading()">
            <table hlmTable>
              <thead hlmTHead>
                <tr hlmTr>
                  <th hlmTh>{{ 'customers.name' | transloco }}</th>
                  <th hlmTh>{{ 'customers.email' | transloco }}</th>
                  <th hlmTh>{{ 'customers.phone' | transloco }}</th>
                  <th hlmTh>{{ 'customers.addresses' | transloco }}</th>
                  <th hlmTh>{{ 'common.createdAt' | transloco }}</th>
                </tr>
              </thead>
              <tbody hlmTBody>
                @for (profile of store.items(); track profile.id) {
                  <tr hlmTr>
                    <td hlmTd>
                      <a
                        class="font-medium text-primary hover:underline"
                        [routerLink]="['/customers', profile.id]"
                        >{{ profile.firstName }} {{ profile.lastName }}</a
                      >
                    </td>
                    <td hlmTd>{{ profile.email }}</td>
                    <td hlmTd>{{ profile.phone ?? '—' }}</td>
                    <td hlmTd>{{ profile.addresses.length }}</td>
                    <td hlmTd>{{ profile.createdAtUtc | pfDate: 'date' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <ul class="space-y-3 md:hidden">
            @for (profile of store.items(); track profile.id) {
              <li>
                <a
                  class="block rounded-lg border bg-background p-3 hover:bg-muted/50"
                  [routerLink]="['/customers', profile.id]"
                >
                  <p class="font-medium text-primary">
                    {{ profile.firstName }} {{ profile.lastName }}
                  </p>
                  <p class="text-sm break-all text-muted-foreground">{{ profile.email }}</p>
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
export class CustomerListPage {
  protected readonly store = inject(CustomerListStore);
}
