import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowLeft, lucideReceipt } from '@ng-icons/lucide';
import {
  ErrorBannerComponent,
  PageHeaderComponent,
  PfDatePipe,
  PfMoneyPipe,
  StatusTagComponent,
} from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { EmptyStateComponent } from '../core/list-states.component';
import { CreditNoteDialogComponent } from './credit-note-dialog.component';
import { InvoiceDetailStore } from './invoice-detail.store';

@Component({
  selector: 'pf-invoice-detail-page',
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
    HlmSkeletonImports,
    HlmTableImports,
    EmptyStateComponent,
    CreditNoteDialogComponent,
  ],
  providers: [InvoiceDetailStore, provideIcons({ lucideArrowLeft, lucideReceipt })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a hlmBtn variant="ghost" size="sm" class="mb-4" routerLink="/billing/invoices">
      <ng-icon name="lucideArrowLeft" />
      {{ 'invoices.backToList' | transloco }}
    </a>

    @if (store.error()) {
      <pf-error-banner [error]="store.error()" (retry)="store.load(id())" />
    } @else if (!store.invoice()) {
      <div class="grid gap-4" role="status" [attr.aria-label]="'common.loading' | transloco">
        <hlm-skeleton class="h-8 w-64" />
        <hlm-skeleton class="h-48 w-full" />
        <hlm-skeleton class="h-32 w-full" />
      </div>
    } @else {
      @let invoice = store.invoice()!;
      <pf-page-header
        [title]="'invoices.detailTitle' | transloco: { number: invoice.number }"
        [subtitle]="'invoices.issuedOn' | transloco: { date: (invoice.issuedAtUtc | pfDate) }"
      >
        @if (store.canIssueCreditNote()) {
          <button hlmBtn type="button" (click)="creditDialogOpen.set(true)">
            <ng-icon name="lucideReceipt" />
            {{ 'invoices.creditNote.open' | transloco }}
          </button>
        }
      </pf-page-header>

      <div class="grid gap-6 lg:grid-cols-2">
        <section hlmCard aria-labelledby="invoice-summary-title">
          <div hlmCardHeader>
            <h2 hlmCardTitle id="invoice-summary-title">{{ 'invoices.summary' | transloco }}</h2>
          </div>
          <dl hlmCardContent class="grid grid-cols-[auto_1fr] gap-x-6 gap-y-3">
            <dt class="text-muted-foreground">{{ 'common.status' | transloco }}</dt>
            <dd><pf-status-tag type="InvoiceStatus" [value]="invoice.status" /></dd>
            <dt class="text-muted-foreground">{{ 'invoices.fields.amount' | transloco }}</dt>
            <dd class="font-medium tabular-nums">
              {{ invoice.amount | pfMoney: invoice.currency }}
            </dd>
            <dt class="text-muted-foreground">{{ 'invoices.fields.creditTotal' | transloco }}</dt>
            <dd class="tabular-nums">{{ invoice.creditTotal | pfMoney: invoice.currency }}</dd>
            <dt class="text-muted-foreground">
              {{ 'invoices.fields.remainingCredit' | transloco }}
            </dt>
            <dd class="tabular-nums">{{ invoice.remainingCredit | pfMoney: invoice.currency }}</dd>
            <dt class="text-muted-foreground">{{ 'invoices.fields.customer' | transloco }}</dt>
            <dd class="font-mono text-xs break-all">{{ invoice.customerSubject }}</dd>
            <dt class="text-muted-foreground">{{ 'invoices.fields.shipment' | transloco }}</dt>
            <dd class="font-mono text-xs break-all">{{ invoice.shipmentId }}</dd>
          </dl>
        </section>

        <section hlmCard aria-labelledby="invoice-payment-title">
          <div hlmCardHeader>
            <h2 hlmCardTitle id="invoice-payment-title">
              {{ 'invoices.payment.title' | transloco }}
            </h2>
          </div>
          <div hlmCardContent>
            @if (store.payment(); as payment) {
              <dl class="grid grid-cols-[auto_1fr] gap-x-6 gap-y-3">
                <dt class="text-muted-foreground">{{ 'invoices.payment.outcome' | transloco }}</dt>
                <dd><pf-status-tag type="MockPaymentOutcome" [value]="payment.outcome" /></dd>
                <dt class="text-muted-foreground">{{ 'invoices.fields.amount' | transloco }}</dt>
                <dd class="tabular-nums">{{ payment.amount | pfMoney: payment.currency }}</dd>
                <dt class="text-muted-foreground">{{ 'invoices.payment.date' | transloco }}</dt>
                <dd>{{ payment.occurredAtUtc | pfDate }}</dd>
                <dt class="text-muted-foreground">
                  {{ 'invoices.payment.operation' | transloco }}
                </dt>
                <dd class="font-mono text-xs break-all">{{ payment.operationId }}</dd>
              </dl>
            } @else {
              <p class="text-muted-foreground">{{ 'invoices.payment.none' | transloco }}</p>
            }
          </div>
        </section>

        <section hlmCard class="lg:col-span-2" aria-labelledby="invoice-credit-notes-title">
          <div hlmCardHeader>
            <h2 hlmCardTitle id="invoice-credit-notes-title">
              {{ 'invoices.creditNotes' | transloco }}
            </h2>
            @if (!store.canIssueCreditNote()) {
              <p hlmCardDescription>{{ 'invoices.creditNote.notAllowed' | transloco }}</p>
            }
          </div>
          <div hlmCardContent>
            @if (store.creditNotes().length === 0) {
              <pf-empty-state titleKey="invoices.noCreditNotes" />
            } @else {
              <div hlmTableContainer>
                <table hlmTable>
                  <caption class="sr-only">
                    {{
                      'invoices.creditNotes' | transloco
                    }}
                  </caption>
                  <thead hlmTHead>
                    <tr hlmTr>
                      <th hlmTh scope="col">{{ 'invoices.fields.number' | transloco }}</th>
                      <th hlmTh scope="col" class="text-right">
                        {{ 'invoices.fields.amount' | transloco }}
                      </th>
                      <th hlmTh scope="col" class="hidden sm:table-cell">
                        {{ 'invoices.creditNote.reason' | transloco }}
                      </th>
                      <th hlmTh scope="col" class="hidden md:table-cell">
                        {{ 'invoices.creditNote.date' | transloco }}
                      </th>
                    </tr>
                  </thead>
                  <tbody hlmTBody>
                    @for (note of store.creditNotes(); track note.id) {
                      <tr hlmTr>
                        <td hlmTd class="font-mono">
                          {{ note.number }}
                          <div class="text-xs text-muted-foreground sm:hidden">
                            {{ 'enum.CreditReason.' + note.reason | transloco }}
                          </div>
                        </td>
                        <td hlmTd class="text-right tabular-nums">
                          {{ note.amount | pfMoney: note.currency }}
                        </td>
                        <td hlmTd class="hidden sm:table-cell">
                          {{ 'enum.CreditReason.' + note.reason | transloco }}
                        </td>
                        <td hlmTd class="hidden md:table-cell">
                          {{ note.occurredAtUtc | pfDate }}
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          </div>
        </section>
      </div>

      <pf-credit-note-dialog [(open)]="creditDialogOpen" />
    }
  `,
})
export class InvoiceDetailPage {
  protected readonly store = inject(InvoiceDetailStore);

  /** Route parameter bound by withComponentInputBinding. */
  readonly id = input.required<string>();
  protected readonly creditDialogOpen = signal(false);

  constructor() {
    this.store.load(this.id);
  }
}
