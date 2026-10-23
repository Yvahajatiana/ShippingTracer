import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePencil, lucidePlus } from '@ng-icons/lucide';
import { Company, ErrorBannerComponent, PageHeaderComponent } from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { EmptyStateComponent, ListSkeletonComponent } from '../core/list-states.component';
import { CompaniesStore } from './companies.store';
import { CompanyDialogComponent } from './company-dialog.component';

@Component({
  selector: 'pf-companies-page',
  imports: [
    TranslocoPipe,
    NgIcon,
    PageHeaderComponent,
    ErrorBannerComponent,
    HlmButtonImports,
    HlmCardImports,
    HlmTableImports,
    EmptyStateComponent,
    ListSkeletonComponent,
    CompanyDialogComponent,
  ],
  providers: [CompaniesStore, provideIcons({ lucidePencil, lucidePlus })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <pf-page-header
      [title]="'companies.title' | transloco"
      [subtitle]="'companies.subtitle' | transloco"
    >
      <button hlmBtn type="button" (click)="openCreate()">
        <ng-icon name="lucidePlus" />
        {{ 'companies.new' | transloco }}
      </button>
    </pf-page-header>

    <section hlmCard>
      <div hlmCardContent>
        @if (store.error()) {
          <pf-error-banner [error]="store.error()" (retry)="store.load()" />
        } @else if (store.loading() && store.companies().length === 0) {
          <pf-list-skeleton />
        } @else if (store.sorted().length === 0) {
          <pf-empty-state titleKey="companies.empty" />
        } @else {
          <div hlmTableContainer>
            <table hlmTable>
              <caption class="sr-only">
                {{
                  'companies.title' | transloco
                }}
              </caption>
              <thead hlmTHead>
                <tr hlmTr>
                  <th hlmTh scope="col">{{ 'companies.fields.name' | transloco }}</th>
                  <th hlmTh scope="col" class="hidden sm:table-cell">
                    {{ 'companies.fields.registrationNumber' | transloco }}
                  </th>
                  <th hlmTh scope="col" class="hidden md:table-cell">
                    {{ 'companies.fields.email' | transloco }}
                  </th>
                  <th hlmTh scope="col" class="text-right">{{ 'common.actions' | transloco }}</th>
                </tr>
              </thead>
              <tbody hlmTBody>
                @for (company of store.sorted(); track company.id) {
                  <tr hlmTr>
                    <td hlmTd class="font-medium">
                      <div>{{ company.name }}</div>
                      <div class="text-xs text-muted-foreground md:hidden">
                        {{ company.registrationNumber }} · {{ company.email }}
                      </div>
                    </td>
                    <td hlmTd class="hidden sm:table-cell">{{ company.registrationNumber }}</td>
                    <td hlmTd class="hidden md:table-cell">{{ company.email }}</td>
                    <td hlmTd class="text-right">
                      <button
                        hlmBtn
                        type="button"
                        variant="ghost"
                        size="icon-sm"
                        [attr.aria-label]="
                          'companies.editNamed' | transloco: { name: company.name }
                        "
                        (click)="openEdit(company)"
                      >
                        <ng-icon name="lucidePencil" />
                      </button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>
    </section>

    <pf-company-dialog [(open)]="dialogOpen" [company]="editing()" />
  `,
})
export class CompaniesPage {
  protected readonly store = inject(CompaniesStore);
  protected readonly dialogOpen = signal(false);
  protected readonly editing = signal<Company | null>(null);

  constructor() {
    this.store.load();
  }

  openCreate(): void {
    this.editing.set(null);
    this.dialogOpen.set(true);
  }

  openEdit(company: Company): void {
    this.editing.set(company);
    this.dialogOpen.set(true);
  }
}
