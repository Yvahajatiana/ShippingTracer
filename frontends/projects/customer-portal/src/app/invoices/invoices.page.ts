import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideReceipt } from '@ng-icons/lucide';
import {
  ErrorBannerComponent,
  PageHeaderComponent,
  PfDatePipe,
  PfMoneyPipe,
  StatusTagComponent,
} from '@parcelflow/shared';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { PagerComponent } from '../core/pager.component';
import { InvoicesStore } from './invoices.store';

/** Paged list of the customer's invoices. */
@Component({
  selector: 'pf-invoices-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    NgIcon,
    PageHeaderComponent,
    ErrorBannerComponent,
    StatusTagComponent,
    PfDatePipe,
    PfMoneyPipe,
    HlmCardImports,
    HlmEmptyImports,
    HlmSkeletonImports,
    HlmTableImports,
    PagerComponent,
  ],
  providers: [InvoicesStore, provideIcons({ lucideReceipt })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <pf-page-header
      [title]="'invoices.title' | transloco"
      [subtitle]="'invoices.subtitle' | transloco"
    />

    @if (store.error(); as error) {
      <pf-error-banner [error]="error" (retry)="store.reload()" />
    } @else if (store.empty()) {
      <div hlmEmpty class="border bg-card">
        <div hlmEmptyHeader>
          <div hlmEmptyMedia variant="icon"><ng-icon name="lucideReceipt" /></div>
          <h2 hlmEmptyTitle>{{ 'invoices.empty' | transloco }}</h2>
        </div>
      </div>
    } @else {
      <div hlmCard class="py-0" [attr.aria-busy]="store.loading()">
        @if (store.loading() && store.items().length === 0) {
          <div class="grid gap-3 p-4">
            @for (row of [1, 2, 3, 4]; track row) {
              <hlm-skeleton class="h-10 w-full" />
            }
          </div>
        } @else {
          <div hlmTableContainer class="hidden md:block">
            <table hlmTable>
              <caption class="sr-only">
                {{
                  'invoices.title' | transloco
                }}
              </caption>
              <thead hlmTHead>
                <tr hlmTr>
                  <th hlmTh scope="col" class="px-4">
                    {{ 'invoices.columns.number' | transloco }}
                  </th>
                  <th hlmTh scope="col">{{ 'invoices.columns.issuedAt' | transloco }}</th>
                  <th hlmTh scope="col">{{ 'common.status' | transloco }}</th>
                  <th hlmTh scope="col" class="px-4 text-end">
                    {{ 'invoices.columns.amount' | transloco }}
                  </th>
                </tr>
              </thead>
              <tbody hlmTBody>
                @for (invoice of store.items(); track invoice.id) {
                  <tr hlmTr>
                    <td hlmTd class="px-4">
                      <a
                        class="font-mono font-medium text-primary hover:underline"
                        [routerLink]="['/invoices', invoice.id]"
                        >{{ invoice.number }}</a
                      >
                    </td>
                    <td hlmTd>{{ invoice.issuedAtUtc | pfDate: 'date' }}</td>
                    <td hlmTd><pf-status-tag type="InvoiceStatus" [value]="invoice.status" /></td>
                    <td hlmTd class="px-4 text-end tabular-nums">
                      {{ invoice.amount | pfMoney: invoice.currency }}
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>

          <ul class="divide-y md:hidden">
            @for (invoice of store.items(); track invoice.id) {
              <li>
                <a
                  class="flex items-center justify-between gap-3 p-4 hover:bg-muted/50"
                  [routerLink]="['/invoices', invoice.id]"
                >
                  <div class="min-w-0">
                    <p class="font-mono font-medium text-primary">{{ invoice.number }}</p>
                    <p class="text-xs text-muted-foreground">
                      {{ invoice.issuedAtUtc | pfDate: 'date' }}
                    </p>
                  </div>
                  <div class="flex flex-col items-end gap-1">
                    <span class="font-medium tabular-nums">
                      {{ invoice.amount | pfMoney: invoice.currency }}
                    </span>
                    <pf-status-tag type="InvoiceStatus" [value]="invoice.status" />
                  </div>
                </a>
              </li>
            }
          </ul>
        }
      </div>
      <pf-pager
        [page]="store.page()"
        [total]="store.total()"
        [hasPrevious]="store.hasPrevious()"
        [hasNext]="store.hasNext()"
        [loading]="store.loading()"
        (previous)="store.previous()"
        (next)="store.next()"
      />
    }
  `,
})
export class InvoicesPage {
  protected readonly store = inject(InvoicesStore);

  constructor() {
    this.store.loadPage(1);
  }
}
