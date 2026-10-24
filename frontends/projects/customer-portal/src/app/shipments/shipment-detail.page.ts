import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowLeft,
  lucideArrowRight,
  lucideDownload,
  lucideMail,
  lucideMessageSquare,
} from '@ng-icons/lucide';
import {
  ErrorBannerComponent,
  Notifier,
  PageHeaderComponent,
  PfDatePipe,
  PfMoneyPipe,
  PfNumberPipe,
  StatusTagComponent,
} from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { TrackingTimelineComponent } from '../tracking/tracking-timeline.component';
import { ShipmentDetailStore } from './shipment-detail.store';

/** Shipment details with label download, tracking, invoice and notifications. */
@Component({
  selector: 'pf-shipment-detail-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    NgIcon,
    PageHeaderComponent,
    ErrorBannerComponent,
    StatusTagComponent,
    PfDatePipe,
    PfMoneyPipe,
    PfNumberPipe,
    HlmButtonImports,
    HlmCardImports,
    HlmSkeletonImports,
    HlmSpinnerImports,
    TrackingTimelineComponent,
  ],
  providers: [
    ShipmentDetailStore,
    provideIcons({
      lucideArrowLeft,
      lucideArrowRight,
      lucideDownload,
      lucideMail,
      lucideMessageSquare,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a hlmBtn variant="ghost" size="sm" class="mb-2 -ms-2" routerLink="/shipments">
      <ng-icon name="lucideArrowLeft" />
      {{ 'shipments.backToList' | transloco }}
    </a>

    @if (store.error(); as error) {
      <pf-error-banner [error]="error" (retry)="store.load(id())" />
    } @else if (store.shipment(); as shipment) {
      <pf-page-header
        [title]="'shipment.title' | transloco: { number: shipment.trackingNumber }"
        [subtitle]="'shipment.createdOn' | transloco: { date: (shipment.createdAtUtc | pfDate) }"
      >
        <button hlmBtn variant="outline" [disabled]="store.downloading()" (click)="download()">
          @if (store.downloading()) {
            <hlm-spinner [aria-label]="'common.loading' | transloco" />
          } @else {
            <ng-icon name="lucideDownload" />
          }
          {{ 'shipment.downloadLabel' | transloco }}
        </button>
      </pf-page-header>

      <div class="grid items-start gap-6 xl:grid-cols-[minmax(0,1fr)_24rem]">
        <div class="grid min-w-0 gap-6">
          <section hlmCard aria-labelledby="pf-shipment-summary">
            <div hlmCardHeader>
              <h2 hlmCardTitle id="pf-shipment-summary">{{ 'shipment.summary' | transloco }}</h2>
              <div hlmCardAction>
                <pf-status-tag type="ShipmentStatus" [value]="shipment.status" />
              </div>
            </div>
            <div hlmCardContent>
              <dl class="grid grid-cols-2 gap-4 text-sm sm:grid-cols-4">
                <div>
                  <dt class="text-muted-foreground">{{ 'shipments.columns.price' | transloco }}</dt>
                  <dd class="text-lg font-semibold tabular-nums">
                    {{ shipment.price | pfMoney: shipment.currency }}
                  </dd>
                </div>
                <div>
                  <dt class="text-muted-foreground">{{ 'shipments.columns.zone' | transloco }}</dt>
                  <dd class="font-medium">
                    {{ 'enum.DeliveryZone.' + shipment.zone | transloco }}
                  </dd>
                </div>
                <div>
                  <dt class="text-muted-foreground">
                    {{ 'newShipment.fields.weightKg' | transloco }}
                  </dt>
                  <dd class="font-medium tabular-nums">
                    {{ shipment.parcel.weightKg | pfNumber }}
                  </dd>
                </div>
                <div>
                  <dt class="text-muted-foreground">{{ 'shipment.dimensions' | transloco }}</dt>
                  <dd class="font-medium tabular-nums">
                    {{ shipment.parcel.lengthCm | pfNumber }} ×
                    {{ shipment.parcel.widthCm | pfNumber }} ×
                    {{ shipment.parcel.heightCm | pfNumber }}
                  </dd>
                </div>
              </dl>
            </div>
          </section>

          <div class="grid gap-6 md:grid-cols-2">
            @for (party of parties(); track party.key) {
              <section hlmCard [attr.aria-labelledby]="'pf-party-' + party.key">
                <div hlmCardHeader>
                  <h2 hlmCardTitle [id]="'pf-party-' + party.key">
                    {{ party.titleKey | transloco }}
                  </h2>
                </div>
                <div hlmCardContent>
                  <address class="grid gap-0.5 text-sm not-italic">
                    <span class="font-medium">{{ party.address.name }}</span>
                    <span>{{ party.address.line1 }}</span>
                    @if (party.address.line2) {
                      <span>{{ party.address.line2 }}</span>
                    }
                    <span>{{ party.address.postalCode }} {{ party.address.city }}</span>
                    <span>{{ party.address.countryCode }}</span>
                    <span class="mt-2 break-all text-muted-foreground">{{
                      party.address.email
                    }}</span>
                    <span class="text-muted-foreground">{{ party.address.phone }}</span>
                  </address>
                </div>
              </section>
            }
          </div>

          <section hlmCard aria-labelledby="pf-shipment-notifications">
            <div hlmCardHeader>
              <h2 hlmCardTitle id="pf-shipment-notifications">
                {{ 'notifications.title' | transloco }}
              </h2>
            </div>
            <div hlmCardContent>
              @if (store.notificationsLoading()) {
                <hlm-skeleton class="h-12 w-full" />
              } @else if (store.notificationsError(); as error) {
                <pf-error-banner [error]="error" [retryable]="false" />
              } @else {
                <ul class="grid gap-3">
                  @for (notification of store.notifications(); track notification.id) {
                    <li class="flex gap-3 rounded-md border p-3">
                      <ng-icon
                        class="mt-0.5 shrink-0 text-muted-foreground"
                        [name]="
                          notification.channel === 'Email' ? 'lucideMail' : 'lucideMessageSquare'
                        "
                        aria-hidden="true"
                      />
                      <div class="min-w-0 text-sm">
                        <p class="font-medium">{{ notification.subject }}</p>
                        <p class="text-muted-foreground">
                          {{ 'enum.NotificationChannel.' + notification.channel | transloco }} ·
                          <span class="break-all">{{ notification.destination }}</span> ·
                          {{ notification.occurredAtUtc | pfDate }}
                        </p>
                      </div>
                    </li>
                  } @empty {
                    <li class="text-sm text-muted-foreground">
                      {{ 'notifications.emptyForShipment' | transloco }}
                    </li>
                  }
                </ul>
              }
            </div>
          </section>
        </div>

        <div class="grid min-w-0 gap-6">
          <section hlmCard aria-labelledby="pf-shipment-invoice">
            <div hlmCardHeader>
              <h2 hlmCardTitle id="pf-shipment-invoice">{{ 'shipment.invoice' | transloco }}</h2>
            </div>
            <div hlmCardContent>
              @if (store.invoiceLoading()) {
                <hlm-skeleton class="h-10 w-full" />
              } @else if (store.invoice(); as invoice) {
                <div class="flex flex-wrap items-center justify-between gap-2">
                  <div>
                    <p class="font-mono font-medium">{{ invoice.number }}</p>
                    <p class="text-sm tabular-nums text-muted-foreground">
                      {{ invoice.amount | pfMoney: invoice.currency }}
                    </p>
                  </div>
                  <pf-status-tag type="InvoiceStatus" [value]="invoice.status" />
                </div>
                <a
                  hlmBtn
                  variant="outline"
                  class="mt-4 w-full"
                  [routerLink]="['/invoices', invoice.id]"
                >
                  {{
                    (invoice.status === 'PendingPayment'
                      ? 'shipment.payInvoice'
                      : 'shipment.viewInvoice'
                    ) | transloco
                  }}
                  <ng-icon name="lucideArrowRight" />
                </a>
              } @else {
                <p class="text-sm text-muted-foreground">{{ 'shipment.noInvoice' | transloco }}</p>
              }
            </div>
          </section>

          <section hlmCard aria-labelledby="pf-shipment-tracking">
            <div hlmCardHeader>
              <h2 hlmCardTitle id="pf-shipment-tracking">{{ 'tracking.history' | transloco }}</h2>
              @if (store.tracking(); as tracking) {
                <div hlmCardAction>
                  <pf-status-tag type="ParcelStatus" [value]="tracking.currentStatus" />
                </div>
              }
            </div>
            <div hlmCardContent>
              @if (store.trackingLoading()) {
                <hlm-skeleton class="h-24 w-full" />
              } @else if (store.trackingError(); as error) {
                @if (error.status === 404) {
                  <p class="text-sm text-muted-foreground">{{ 'tracking.noEvents' | transloco }}</p>
                } @else {
                  <pf-error-banner [error]="error" [retryable]="false" />
                }
              } @else {
                <pf-tracking-timeline [entries]="store.timeline()" />
                <a
                  class="mt-4 inline-block text-sm text-primary hover:underline"
                  [routerLink]="['/track', shipment.trackingNumber]"
                  >{{ 'shipment.publicTrackingLink' | transloco }}</a
                >
              }
            </div>
          </section>
        </div>
      </div>
    } @else {
      <div class="grid gap-4" aria-busy="true">
        <hlm-skeleton class="h-8 w-72" />
        <hlm-skeleton class="h-32 w-full" />
        <hlm-skeleton class="h-48 w-full" />
      </div>
    }
  `,
})
export class ShipmentDetailPage {
  protected readonly store = inject(ShipmentDetailStore);
  private readonly notifier = inject(Notifier);
  /** Route parameter, bound by withComponentInputBinding. */
  readonly id = input.required<string>();

  constructor() {
    this.store.load(this.id);
  }

  protected readonly parties = computed(() => {
    const shipment = this.store.shipment();
    return shipment
      ? [
          { key: 'sender', titleKey: 'newShipment.sender', address: shipment.sender },
          { key: 'recipient', titleKey: 'newShipment.recipient', address: shipment.recipient },
        ]
      : [];
  });

  protected async download(): Promise<void> {
    const result = await this.store.downloadLabel();
    if (!result.ok) {
      this.notifier.error(result.error);
    }
  }
}
