import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBuilding2, lucideRefreshCw } from '@ng-icons/lucide';
import { ErrorBannerComponent, PageHeaderComponent } from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { EmptyStateComponent, ListSkeletonComponent } from '../core/states';
import { CompanyListStore } from './customers.store';

@Component({
  selector: 'pf-company-list-page',
  imports: [
    TranslocoPipe,
    NgIcon,
    HlmButtonImports,
    HlmCardImports,
    HlmTableImports,
    ErrorBannerComponent,
    PageHeaderComponent,
    EmptyStateComponent,
    ListSkeletonComponent,
  ],
  providers: [CompanyListStore, provideIcons({ lucideBuilding2, lucideRefreshCw })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <pf-page-header
      [title]="'companies.title' | transloco"
      [subtitle]="'companies.subtitle' | transloco"
    >
      <button hlmBtn variant="outline" type="button" (click)="store.load()">
        <ng-icon name="lucideRefreshCw" />
        {{ 'common.refresh' | transloco }}
      </button>
    </pf-page-header>

    <div hlmCard>
      <div hlmCardContent>
        @if (store.error(); as error) {
          <pf-error-banner [error]="error" (retry)="store.load()" />
        } @else if (store.loading() && !store.loaded()) {
          <pf-list-skeleton [rows]="3" />
        } @else if (store.isEmpty()) {
          <pf-empty-state icon="lucideBuilding2" [title]="'companies.empty' | transloco" />
        } @else {
          <div hlmTableContainer class="hidden md:block">
            <table hlmTable>
              <thead hlmTHead>
                <tr hlmTr>
                  <th hlmTh>{{ 'companies.name' | transloco }}</th>
                  <th hlmTh>{{ 'companies.registration' | transloco }}</th>
                  <th hlmTh>{{ 'companies.email' | transloco }}</th>
                </tr>
              </thead>
              <tbody hlmTBody>
                @for (company of store.companies(); track company.id) {
                  <tr hlmTr>
                    <td hlmTd class="font-medium">{{ company.name }}</td>
                    <td hlmTd class="font-mono">{{ company.registrationNumber }}</td>
                    <td hlmTd>{{ company.email }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <ul class="space-y-3 md:hidden">
            @for (company of store.companies(); track company.id) {
              <li class="rounded-lg border bg-background p-3">
                <p class="font-medium">{{ company.name }}</p>
                <p class="font-mono text-xs text-muted-foreground">
                  {{ company.registrationNumber }}
                </p>
                <p class="text-sm break-all text-muted-foreground">{{ company.email }}</p>
              </li>
            }
          </ul>
        }
      </div>
    </div>
  `,
})
export class CompanyListPage {
  protected readonly store = inject(CompanyListStore);
}
