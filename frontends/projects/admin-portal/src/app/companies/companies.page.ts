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
      [eyebrow]="'nav.groups.referenceData' | transloco"
      [title]="'companies.title' | transloco"
      [subtitle]="'companies.subtitle' | transloco"
    >
      <button hlmBtn type="button" (click)="openCreate()">
        <ng-icon name="lucidePlus" />
        {{ 'companies.new' | transloco }}
      </button>
    </pf-page-header>

    <section hlmCard class="gap-0 py-0">
      @if (store.error()) {
        <div class="p-5">
          <pf-error-banner [error]="store.error()" (retry)="store.load()" />
        </div>
      } @else if (store.loading() && store.companies().length === 0) {
        <div class="p-5">
          <pf-list-skeleton />
        </div>
      } @else if (store.sorted().length === 0) {
        <div class="p-5">
          <pf-empty-state titleKey="companies.empty" />
        </div>
      } @else {
        <div class="flex flex-wrap items-center gap-3 border-b px-5 py-3.5">
          <p class="ml-auto text-sm text-muted-foreground" aria-live="polite">
            {{ 'companies.count' | transloco: { count: store.sorted().length } }}
          </p>
        </div>
        <div hlmTableContainer class="[&_tr>*:first-child]:pl-5! [&_tr>*:last-child]:pr-5!">
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
                    <div class="text-xs font-normal text-muted-foreground md:hidden">
                      <span class="font-mono">{{ company.registrationNumber }}</span> ·
                      {{ company.email }}
                    </div>
                  </td>
                  <td hlmTd class="hidden font-mono text-[13px] sm:table-cell">
                    {{ company.registrationNumber }}
                  </td>
                  <td hlmTd class="hidden text-muted-foreground md:table-cell">
                    {{ company.email }}
                  </td>
                  <td hlmTd class="text-right">
                    <button
                      hlmBtn
                      type="button"
                      variant="ghost"
                      size="icon-sm"
                      class="max-sm:size-10"
                      [attr.aria-label]="'companies.editNamed' | transloco: { name: company.name }"
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
