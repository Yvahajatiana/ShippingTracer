import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronRight } from '@ng-icons/lucide';
import {
  ErrorBannerComponent,
  INVOICE_STATUSES,
  PageHeaderComponent,
  PfDatePipe,
  PfMoneyPipe,
  StatusTagComponent,
} from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { EmptyStateComponent, ListSkeletonComponent } from '../core/list-states.component';
import { PagerComponent } from '../core/pager.component';
import { InvoiceStatusFilter, InvoicesStore } from './invoices.store';

@Component({
  selector: 'pf-invoices-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    NgIcon,
    PfDatePipe,
    PfMoneyPipe,
    PageHeaderComponent,
    ErrorBannerComponent,
    StatusTagComponent,
    HlmButtonImports,
    HlmCardImports,
    HlmNativeSelectImports,
    HlmTableImports,
    EmptyStateComponent,
    ListSkeletonComponent,
    PagerComponent,
  ],
  providers: [InvoicesStore, provideIcons({ lucideChevronRight })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <pf-page-header
      [eyebrow]="'nav.groups.finance' | transloco"
      [title]="'invoices.title' | transloco"
      [subtitle]="'invoices.subtitle' | transloco"
    />

    <section hlmCard class="gap-0 py-0">
      <!-- Toolbar strip: status filter, hint and total count -->
      <div class="flex flex-wrap items-center gap-3 border-b px-5 py-3.5">
        <label for="invoice-status-filter" class="text-sm font-medium">
          {{ 'invoices.statusFilter' | transloco }}
        </label>
        <hlm-native-select
          class="w-52"
          selectId="invoice-status-filter"
          [value]="store.statusFilter()"
          (valueChange)="setFilter($event)"
        >
          <option value="">{{ 'invoices.allStatuses' | transloco }}</option>
          @for (status of statuses; track status) {
            <option [value]="status">{{ 'enum.InvoiceStatus.' + status | transloco }}</option>
          }
        </hlm-native-select>
        <p id="invoice-status-filter-hint" class="text-xs text-muted-foreground">
          {{ 'invoices.filterHint' | transloco }}
        </p>
        @if (store.items().length > 0) {
          <p class="ml-auto text-sm text-muted-foreground tabular-nums">
            {{ 'invoices.totalCount' | transloco: { total: store.total() } }}
          </p>
        }
      </div>

      @if (store.error()) {
        <div class="px-5 py-4">
          <pf-error-banner [error]="store.error()" (retry)="store.reload()" />
        </div>
      } @else if (store.loading() && store.items().length === 0) {
        <div class="px-5 py-3">
          <pf-list-skeleton />
        </div>
      } @else if (store.visible().length === 0) {
        <div class="p-5">
          <pf-empty-state
            [titleKey]="store.items().length === 0 ? 'invoices.empty' : 'invoices.emptyFiltered'"
          />
        </div>
      } @else {
        <div hlmTableContainer [attr.aria-busy]="store.loading()">
          <table hlmTable>
            <caption class="sr-only">
              {{
                'invoices.title' | transloco
              }}
            </caption>
            <thead hlmTHead>
              <tr hlmTr>
                <th hlmTh scope="col">{{ 'invoices.fields.number' | transloco }}</th>
                <th hlmTh scope="col" class="hidden md:table-cell">
                  {{ 'invoices.fields.customer' | transloco }}
                </th>
                <th hlmTh scope="col" class="text-right">
                  {{ 'invoices.fields.amount' | transloco }}
                </th>
                <th hlmTh scope="col" class="hidden sm:table-cell">
                  {{ 'common.status' | transloco }}
                </th>
                <th hlmTh scope="col" class="hidden text-right lg:table-cell">
                  {{ 'invoices.fields.creditTotal' | transloco }}
                </th>
                <th hlmTh scope="col" class="hidden text-right lg:table-cell">
                  {{ 'invoices.fields.remainingCredit' | transloco }}
                </th>
                <th hlmTh scope="col" class="hidden xl:table-cell">
                  {{ 'invoices.fields.issuedAt' | transloco }}
                </th>
                <th hlmTh scope="col">
                  <span class="sr-only">{{ 'common.details' | transloco }}</span>
                </th>
              </tr>
            </thead>
            <tbody hlmTBody>
              @for (invoice of store.visible(); track invoice.id) {
                <tr hlmTr>
                  <td hlmTd>
                    <a
                      class="rounded-sm font-mono text-[13px] font-medium text-primary hover:underline focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
                      [routerLink]="['/billing/invoices', invoice.id]"
                    >
                      {{ invoice.number }}
                    </a>
                    <div class="mt-1 sm:hidden">
                      <pf-status-tag type="InvoiceStatus" [value]="invoice.status" />
                    </div>
                  </td>
                  <td hlmTd class="hidden font-mono text-[13px] md:table-cell">
                    {{ invoice.customerSubject }}
                  </td>
                  <td hlmTd class="text-right font-mono text-[13px]">
                    {{ invoice.amount | pfMoney: invoice.currency }}
                  </td>
                  <td hlmTd class="hidden sm:table-cell">
                    <pf-status-tag type="InvoiceStatus" [value]="invoice.status" />
                  </td>
                  <td hlmTd class="hidden text-right font-mono text-[13px] lg:table-cell">
                    {{ invoice.creditTotal | pfMoney: invoice.currency }}
                  </td>
                  <td hlmTd class="hidden text-right font-mono text-[13px] lg:table-cell">
                    {{ invoice.remainingCredit | pfMoney: invoice.currency }}
                  </td>
                  <td hlmTd class="hidden text-muted-foreground tabular-nums xl:table-cell">
                    {{ invoice.issuedAtUtc | pfDate: 'date' }}
                  </td>
                  <td hlmTd class="text-right">
                    <a
                      hlmBtn
                      variant="ghost"
                      size="icon-sm"
                      [routerLink]="['/billing/invoices', invoice.id]"
                      [attr.aria-label]="
                        'invoices.openNamed' | transloco: { number: invoice.number }
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

      <!-- Pager strip -->
      <div class="border-t px-5 py-3">
        <pf-pager
          [page]="store.page()"
          [pageCount]="store.pageCount()"
          [total]="store.total()"
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
export class InvoicesPage {
  protected readonly store = inject(InvoicesStore);
  protected readonly statuses = INVOICE_STATUSES;

  constructor() {
    this.store.loadPage(1);
  }

  protected setFilter(value: string | null | undefined): void {
    this.store.setStatusFilter((value ?? '') as InvoiceStatusFilter);
  }
}
