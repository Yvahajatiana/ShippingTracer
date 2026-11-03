import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowLeft, lucideCreditCard, lucidePackage } from '@ng-icons/lucide';
import {
  ErrorBannerComponent,
  PageHeaderComponent,
  PfDatePipe,
  PfMoneyPipe,
  StatusTagComponent,
} from '@parcelflow/shared';
import { HlmAlertImports } from '@spartan-ng/helm/alert';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { InvoiceDetailStore } from './invoice-detail.store';
import { PaymentDialogComponent } from './payment-dialog.component';

/** Invoice details, payment status, mock payment and credit notes. */
@Component({
  selector: 'pf-invoice-detail-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    NgIcon,
    PageHeaderComponent,
    ErrorBannerComponent,
    StatusTagComponent,
    PfDatePipe,
    PfMoneyPipe,
    HlmAlertImports,
    HlmButtonImports,
    HlmCardImports,
    HlmSkeletonImports,
    HlmTableImports,
    PaymentDialogComponent,
  ],
  providers: [
    InvoiceDetailStore,
    provideIcons({ lucideArrowLeft, lucideCreditCard, lucidePackage }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a hlmBtn variant="ghost" size="sm" class="mb-2 -ms-2" routerLink="/invoices">
      <ng-icon name="lucideArrowLeft" />
      {{ 'invoices.backToList' | transloco }}
    </a>

    @if (store.error(); as error) {
      <pf-error-banner [error]="error" (retry)="store.load(id())" />
    } @else if (store.invoice(); as invoice) {
      <pf-page-header
        [title]="'invoice.title' | transloco: { number: invoice.number }"
        [subtitle]="
          'invoice.issuedOn' | transloco: { date: (invoice.issuedAtUtc | pfDate: 'date') }
        "
      >
        <a hlmBtn variant="outline" [routerLink]="['/shipments', invoice.shipmentId]">
          <ng-icon name="lucidePackage" />
          {{ 'invoice.viewShipment' | transloco }}
        </a>
      </pf-page-header>

      <div class="grid max-w-4xl gap-6">
        @if (store.payable()) {
          <div hlmAlert>
            <ng-icon name="lucideCreditCard" />
            <h2 hlmAlertTitle>{{ 'payment.pendingTitle' | transloco }}</h2>
            <p hlmAlertDescription>{{ 'payment.pendingMessage' | transloco }}</p>
            <div hlmAlertAction>
              <button hlmBtn size="sm" (click)="paymentOpen.set(true)">
                {{ 'payment.payNow' | transloco }}
              </button>
            </div>
          </div>
        }

        <section hlmCard aria-labelledby="pf-invoice-summary">
          <div hlmCardHeader>
            <h2 hlmCardTitle id="pf-invoice-summary">{{ 'invoice.summary' | transloco }}</h2>
            <div hlmCardAction>
              <pf-status-tag type="InvoiceStatus" [value]="invoice.status" />
            </div>
          </div>
          <div hlmCardContent>
            <dl class="grid grid-cols-2 gap-4 text-sm sm:grid-cols-3">
              <div>
                <dt class="text-muted-foreground">{{ 'invoices.columns.amount' | transloco }}</dt>
                <dd class="text-lg font-semibold tabular-nums">
                  {{ invoice.amount | pfMoney: invoice.currency }}
                </dd>
              </div>
              <div>
                <dt class="text-muted-foreground">{{ 'invoice.creditTotal' | transloco }}</dt>
                <dd class="font-medium tabular-nums">
                  {{ invoice.creditTotal | pfMoney: invoice.currency }}
                </dd>
              </div>
              <div>
                <dt class="text-muted-foreground">{{ 'invoices.columns.issuedAt' | transloco }}</dt>
                <dd class="font-medium">{{ invoice.issuedAtUtc | pfDate }}</dd>
              </div>
            </dl>
          </div>
        </section>

        <section hlmCard aria-labelledby="pf-invoice-payment">
          <div hlmCardHeader>
            <h2 hlmCardTitle id="pf-invoice-payment">{{ 'payment.title' | transloco }}</h2>
            @if (store.payment(); as payment) {
              <div hlmCardAction>
                <pf-status-tag type="MockPaymentOutcome" [value]="payment.outcome" />
              </div>
            }
          </div>
          <div hlmCardContent>
            @if (store.payment(); as payment) {
              <dl class="grid grid-cols-2 gap-4 text-sm sm:grid-cols-3">
                <div>
                  <dt class="text-muted-foreground">{{ 'invoices.columns.amount' | transloco }}</dt>
                  <dd class="font-medium tabular-nums">
                    {{ payment.amount | pfMoney: payment.currency }}
                  </dd>
                </div>
                <div>
                  <dt class="text-muted-foreground">{{ 'payment.date' | transloco }}</dt>
                  <dd class="font-medium">{{ payment.occurredAtUtc | pfDate }}</dd>
                </div>
                <div class="col-span-2 sm:col-span-1">
                  <dt class="text-muted-foreground">{{ 'payment.reference' | transloco }}</dt>
                  <dd class="font-mono text-xs break-all">{{ payment.operationId }}</dd>
                </div>
              </dl>
            } @else {
              <p class="text-sm text-muted-foreground">{{ 'payment.none' | transloco }}</p>
            }
          </div>
        </section>

        <section hlmCard aria-labelledby="pf-invoice-credit-notes">
          <div hlmCardHeader>
            <h2 hlmCardTitle id="pf-invoice-credit-notes">{{ 'creditNotes.title' | transloco }}</h2>
          </div>
          <div hlmCardContent>
            @if (store.creditNotes().length === 0) {
              <p class="text-sm text-muted-foreground">{{ 'creditNotes.empty' | transloco }}</p>
            } @else {
              <div hlmTableContainer>
                <table hlmTable>
                  <thead hlmTHead>
                    <tr hlmTr>
                      <th hlmTh scope="col">{{ 'creditNotes.number' | transloco }}</th>
                      <th hlmTh scope="col">{{ 'creditNotes.reason' | transloco }}</th>
                      <th hlmTh scope="col">{{ 'payment.date' | transloco }}</th>
                      <th hlmTh scope="col" class="text-end">
                        {{ 'invoices.columns.amount' | transloco }}
                      </th>
                    </tr>
                  </thead>
                  <tbody hlmTBody>
                    @for (note of store.creditNotes(); track note.id) {
                      <tr hlmTr>
                        <td hlmTd class="font-mono">{{ note.number }}</td>
                        <td hlmTd>{{ 'enum.CreditReason.' + note.reason | transloco }}</td>
                        <td hlmTd>{{ note.occurredAtUtc | pfDate: 'date' }}</td>
                        <td hlmTd class="text-end tabular-nums">
                          {{ note.amount | pfMoney: note.currency }}
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

      <pf-payment-dialog [open]="paymentOpen()" (closed)="paymentOpen.set(false)" />
    } @else {
      <div class="grid max-w-4xl gap-4" aria-busy="true">
        <hlm-skeleton class="h-8 w-72" />
        <hlm-skeleton class="h-28 w-full" />
        <hlm-skeleton class="h-28 w-full" />
      </div>
    }
  `,
})
export class InvoiceDetailPage {
  protected readonly store = inject(InvoiceDetailStore);
  /** Route parameter, bound by withComponentInputBinding. */
  readonly id = input.required<string>();
  protected readonly paymentOpen = signal(false);

  constructor() {
    this.store.load(this.id);
  }
}
