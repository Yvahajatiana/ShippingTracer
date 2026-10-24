import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronLeft, lucideReceipt } from '@ng-icons/lucide';
import {
  ErrorBannerComponent,
  PfDatePipe,
  PfMoneyPipe,
  StatusTagComponent,
} from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmTableImports } from '@spartan-ng/helm/table';
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
    ErrorBannerComponent,
    StatusTagComponent,
    HlmButtonImports,
    HlmCardImports,
    HlmSkeletonImports,
    HlmTableImports,
    CreditNoteDialogComponent,
  ],
  providers: [InvoiceDetailStore, provideIcons({ lucideChevronLeft, lucideReceipt })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a
      class="mb-4 inline-flex items-center gap-1 rounded-sm text-sm font-medium text-primary hover:underline focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
      routerLink="/billing/invoices"
    >
      <ng-icon name="lucideChevronLeft" size="16" aria-hidden="true" />
      {{ 'invoices.backToList' | transloco }}
    </a>

    @if (store.error()) {
      <pf-error-banner [error]="store.error()" (retry)="store.load(id())" />
    } @else if (!store.invoice()) {
      <div class="grid gap-4" role="status" [attr.aria-label]="'common.loading' | transloco">
        <hlm-skeleton class="h-8 w-64" />
        <hlm-skeleton class="h-24 w-full" />
        <hlm-skeleton class="h-48 w-full" />
      </div>
    } @else {
      @let invoice = store.invoice()!;
      <!-- Same layout as pf-page-header, with a mono invoice number and the status next to it -->
      <header class="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div class="min-w-0">
          <p class="mb-1.5 text-xs font-semibold tracking-[0.08em] text-primary uppercase">
            {{ 'nav.groups.finance' | transloco }}
          </p>
          <div class="flex flex-wrap items-center gap-3">
            <h1 class="text-2xl font-semibold tracking-tight">
              {{ 'invoices.invoiceLabel' | transloco }}
              <span class="font-mono font-medium">{{ invoice.number }}</span>
            </h1>
            <pf-status-tag type="InvoiceStatus" [value]="invoice.status" />
          </div>
          <p class="mt-1 text-muted-foreground">
            {{ 'invoices.issuedOn' | transloco: { date: (invoice.issuedAtUtc | pfDate) } }}
          </p>
        </div>
        @if (store.canIssueCreditNote()) {
          <div class="flex flex-wrap gap-2">
            <button hlmBtn type="button" (click)="creditDialogOpen.set(true)">
              <ng-icon name="lucideReceipt" />
              {{ 'invoices.creditNote.open' | transloco }}
            </button>
          </div>
        }
      </header>

      <!-- Amounts band -->
      <div hlmCard class="mb-6 gap-0 py-0">
        <dl class="grid divide-y sm:grid-cols-3 sm:divide-x sm:divide-y-0">
          <div class="px-6 py-4">
            <dt class="text-xs font-semibold tracking-[0.06em] text-muted-foreground uppercase">
              {{ 'invoices.fields.amount' | transloco }}
            </dt>
            <dd class="mt-1 font-mono text-2xl font-semibold">
              {{ invoice.amount | pfMoney: invoice.currency }}
            </dd>
          </div>
          <div class="px-6 py-4">
            <dt class="text-xs font-semibold tracking-[0.06em] text-muted-foreground uppercase">
              {{ 'invoices.fields.creditTotal' | transloco }}
            </dt>
            <dd class="mt-1 font-mono text-2xl font-semibold">
              {{ invoice.creditTotal | pfMoney: invoice.currency }}
            </dd>
          </div>
          <div class="px-6 py-4">
            <dt class="text-xs font-semibold tracking-[0.06em] text-muted-foreground uppercase">
              {{ 'invoices.fields.remainingCredit' | transloco }}
            </dt>
            <dd class="mt-1 font-mono text-2xl font-semibold text-success">
              {{ invoice.remainingCredit | pfMoney: invoice.currency }}
            </dd>
          </div>
        </dl>
      </div>

      <div class="flex flex-wrap items-start gap-6">
        <!-- Main column -->
        <div class="flex min-w-0 grow-2 basis-[520px] flex-col gap-6">
          <section hlmCard class="gap-0 py-0" aria-labelledby="invoice-payment-title">
            <div class="border-b px-6 py-4">
              <h2 id="invoice-payment-title" class="text-[15px] font-semibold">
                {{ 'invoices.payment.title' | transloco }}
              </h2>
            </div>
            <div class="px-6 py-5">
              @if (store.payment(); as payment) {
                <dl class="grid gap-4 sm:grid-cols-3">
                  <div>
                    <dt class="text-[13px] text-muted-foreground">
                      {{ 'invoices.payment.outcome' | transloco }}
                    </dt>
                    <dd class="mt-1.5">
                      <pf-status-tag type="MockPaymentOutcome" [value]="payment.outcome" />
                    </dd>
                  </div>
                  <div>
                    <dt class="text-[13px] text-muted-foreground">
                      {{ 'invoices.payment.date' | transloco }}
                    </dt>
                    <dd class="mt-1.5 tabular-nums">{{ payment.occurredAtUtc | pfDate }}</dd>
                  </div>
                  <div class="min-w-0">
                    <dt class="text-[13px] text-muted-foreground">
                      {{ 'invoices.payment.operation' | transloco }}
                    </dt>
                    <dd class="mt-1.5 font-mono text-[13px] break-all">
                      {{ payment.operationId }}
                    </dd>
                  </div>
                </dl>
              } @else {
                <p class="text-sm text-muted-foreground">
                  {{ 'invoices.payment.none' | transloco }}
                </p>
              }
            </div>
          </section>

          <section hlmCard class="gap-0 py-0" aria-labelledby="invoice-credit-notes-title">
            <div class="border-b px-6 py-4">
              <h2 id="invoice-credit-notes-title" class="text-[15px] font-semibold">
                {{ 'invoices.creditNotes' | transloco }}
              </h2>
              @if (!store.canIssueCreditNote()) {
                <p class="mt-1 text-[13px] text-muted-foreground">
                  {{ 'invoices.creditNote.notAllowed' | transloco }}
                </p>
              }
            </div>
            @if (store.creditNotes().length === 0) {
              <p class="px-6 py-8 text-center text-sm text-muted-foreground">
                {{ 'invoices.noCreditNotes' | transloco }}
              </p>
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
                        <td hlmTd class="font-mono text-[13px] font-medium">
                          {{ note.number }}
                          <div class="font-sans text-xs text-muted-foreground sm:hidden">
                            {{ 'enum.CreditReason.' + note.reason | transloco }}
                          </div>
                        </td>
                        <td hlmTd class="text-right font-mono text-[13px]">
                          {{ note.amount | pfMoney: note.currency }}
                        </td>
                        <td hlmTd class="hidden sm:table-cell">
                          {{ 'enum.CreditReason.' + note.reason | transloco }}
                        </td>
                        <td hlmTd class="hidden text-muted-foreground tabular-nums md:table-cell">
                          {{ note.occurredAtUtc | pfDate }}
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          </section>
        </div>

        <!-- Side column -->
        <section
          hlmCard
          class="min-w-0 grow basis-[280px] gap-0 py-0"
          aria-labelledby="invoice-summary-title"
        >
          <div class="border-b px-6 py-4">
            <h2 id="invoice-summary-title" class="text-[15px] font-semibold">
              {{ 'invoices.summary' | transloco }}
            </h2>
          </div>
          <dl class="px-6 py-2">
            <div class="flex justify-between gap-3 border-b py-2.5 last:border-0">
              <dt class="text-[13px] text-muted-foreground">{{ 'common.status' | transloco }}</dt>
              <dd class="text-right">
                <pf-status-tag type="InvoiceStatus" [value]="invoice.status" />
              </dd>
            </div>
            <div class="flex justify-between gap-3 border-b py-2.5 last:border-0">
              <dt class="text-[13px] text-muted-foreground">
                {{ 'invoices.fields.issuedAt' | transloco }}
              </dt>
              <dd class="text-right text-sm tabular-nums">
                {{ invoice.issuedAtUtc | pfDate: 'date' }}
              </dd>
            </div>
            <div class="flex justify-between gap-3 border-b py-2.5 last:border-0">
              <dt class="text-[13px] text-muted-foreground">
                {{ 'invoices.fields.customer' | transloco }}
              </dt>
              <dd class="min-w-0 text-right font-mono text-[13px] break-all">
                {{ invoice.customerSubject }}
              </dd>
            </div>
            <div class="flex justify-between gap-3 border-b py-2.5 last:border-0">
              <dt class="text-[13px] text-muted-foreground">
                {{ 'invoices.fields.shipment' | transloco }}
              </dt>
              <dd class="min-w-0 text-right font-mono text-[13px] break-all">
                {{ invoice.shipmentId }}
              </dd>
            </div>
          </dl>
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
