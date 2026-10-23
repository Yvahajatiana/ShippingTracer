import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronRight } from '@ng-icons/lucide';
import { ErrorBannerComponent, PageHeaderComponent, PfDatePipe } from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { EmptyStateComponent, ListSkeletonComponent } from '../core/list-states.component';
import { PagerComponent } from '../core/pager.component';
import { CustomersStore } from './customers.store';

@Component({
  selector: 'pf-customers-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    NgIcon,
    PfDatePipe,
    PageHeaderComponent,
    ErrorBannerComponent,
    HlmButtonImports,
    HlmCardImports,
    HlmTableImports,
    EmptyStateComponent,
    ListSkeletonComponent,
    PagerComponent,
  ],
  providers: [CustomersStore, provideIcons({ lucideChevronRight })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <pf-page-header
      [title]="'customers.title' | transloco"
      [subtitle]="'customers.subtitle' | transloco"
    />

    <section hlmCard>
      <div hlmCardContent>
        @if (store.error()) {
          <pf-error-banner [error]="store.error()" (retry)="store.reload()" />
        } @else if (store.loading() && store.items().length === 0) {
          <pf-list-skeleton />
        } @else if (store.items().length === 0) {
          <pf-empty-state titleKey="customers.empty" />
        } @else {
          <div hlmTableContainer [attr.aria-busy]="store.loading()">
            <table hlmTable>
              <caption class="sr-only">
                {{
                  'customers.title' | transloco
                }}
              </caption>
              <thead hlmTHead>
                <tr hlmTr>
                  <th hlmTh scope="col">{{ 'customers.fields.name' | transloco }}</th>
                  <th hlmTh scope="col" class="hidden sm:table-cell">
                    {{ 'customers.fields.email' | transloco }}
                  </th>
                  <th hlmTh scope="col" class="hidden md:table-cell">
                    {{ 'customers.fields.subject' | transloco }}
                  </th>
                  <th hlmTh scope="col" class="hidden lg:table-cell">
                    {{ 'common.createdAt' | transloco }}
                  </th>
                  <th hlmTh scope="col" class="text-right">
                    <span class="sr-only">{{ 'common.details' | transloco }}</span>
                  </th>
                </tr>
              </thead>
              <tbody hlmTBody>
                @for (profile of store.items(); track profile.id) {
                  <tr hlmTr>
                    <td hlmTd class="font-medium">
                      <a class="hover:underline" [routerLink]="['/customers', profile.id]">
                        {{ profile.firstName }} {{ profile.lastName }}
                      </a>
                      <div class="text-xs text-muted-foreground sm:hidden">{{ profile.email }}</div>
                    </td>
                    <td hlmTd class="hidden sm:table-cell">{{ profile.email }}</td>
                    <td hlmTd class="hidden font-mono text-xs md:table-cell">
                      {{ profile.keycloakSubject }}
                    </td>
                    <td hlmTd class="hidden lg:table-cell">
                      {{ profile.createdAtUtc | pfDate: 'date' }}
                    </td>
                    <td hlmTd class="text-right">
                      <a
                        hlmBtn
                        variant="ghost"
                        size="icon-sm"
                        [routerLink]="['/customers', profile.id]"
                        [attr.aria-label]="
                          'customers.openNamed'
                            | transloco: { name: profile.firstName + ' ' + profile.lastName }
                        "
                      >
                        <ng-icon name="lucideChevronRight" />
                      </a>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
        <pf-pager
          [page]="store.page()"
          [totalKnown]="false"
          [hasPrevious]="store.hasPrevious()"
          [hasNext]="store.hasNext()"
          [disabled]="store.loading()"
          (previous)="store.previousPage()"
          (next)="store.nextPage()"
        />
      </div>
    </section>
  `,
})
export class CustomersPage {
  protected readonly store = inject(CustomersStore);

  constructor() {
    this.store.loadPage(1);
  }
}
