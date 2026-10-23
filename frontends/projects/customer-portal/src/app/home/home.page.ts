import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowRight,
  lucideCreditCard,
  lucideLogIn,
  lucidePackagePlus,
  lucideReceipt,
  lucideTruck,
} from '@ng-icons/lucide';
import {
  AuthService,
  ErrorBannerComponent,
  PfDatePipe,
  PfMoneyPipe,
  StatusTagComponent,
} from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { TrackingSearchComponent } from '../tracking/tracking-search.component';
import { DashboardStore } from './dashboard.store';

/** Public landing page with tracking search; signed-in customers also get a dashboard. */
@Component({
  selector: 'pf-home-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    NgIcon,
    ErrorBannerComponent,
    StatusTagComponent,
    PfDatePipe,
    PfMoneyPipe,
    HlmButtonImports,
    HlmCardImports,
    HlmSkeletonImports,
    TrackingSearchComponent,
  ],
  providers: [
    DashboardStore,
    provideIcons({
      lucideArrowRight,
      lucideCreditCard,
      lucideLogIn,
      lucidePackagePlus,
      lucideReceipt,
      lucideTruck,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section
      class="relative overflow-hidden rounded-2xl border bg-card px-5 py-10 sm:px-10 sm:py-14"
      aria-labelledby="pf-hero-title"
    >
      <div
        class="pointer-events-none absolute -end-24 -top-24 size-72 rounded-full bg-primary/10 blur-3xl"
        aria-hidden="true"
      ></div>
      <div class="relative max-w-2xl">
        <p class="mb-2 flex items-center gap-2 text-sm font-medium text-primary">
          <ng-icon name="lucideTruck" aria-hidden="true" />
          ParcelFlow
        </p>
        <h1 id="pf-hero-title" class="text-3xl font-semibold tracking-tight sm:text-4xl">
          {{ 'home.hero.title' | transloco }}
        </h1>
        <p class="mt-3 text-muted-foreground sm:text-lg">{{ 'home.hero.subtitle' | transloco }}</p>
        <div class="mt-6">
          <pf-tracking-search />
        </div>
      </div>
    </section>

    @if (isCustomer()) {
      <section class="mt-8" aria-labelledby="pf-dashboard-title">
        <div class="mb-4 flex flex-wrap items-center justify-between gap-3">
          <h2 id="pf-dashboard-title" class="text-xl font-semibold tracking-tight">
            {{ 'home.dashboard.title' | transloco: { name: auth.user()?.displayName } }}
          </h2>
          <a hlmBtn routerLink="/shipments/new">
            <ng-icon name="lucidePackagePlus" />
            {{ 'shipments.new' | transloco }}
          </a>
        </div>

        @if (store.error(); as error) {
          <pf-error-banner [error]="error" (retry)="store.load(true)" />
        } @else {
          <div class="grid gap-6 lg:grid-cols-[minmax(0,1fr)_18rem]">
            <div hlmCard class="min-w-0">
              <div hlmCardHeader>
                <h3 hlmCardTitle>{{ 'home.dashboard.recent' | transloco }}</h3>
                <div hlmCardAction>
                  <a hlmBtn variant="link" size="sm" routerLink="/shipments">
                    {{ 'home.dashboard.allShipments' | transloco }}
                    <ng-icon name="lucideArrowRight" />
                  </a>
                </div>
              </div>
              <div hlmCardContent>
                @if (store.loading()) {
                  <div class="grid gap-3" aria-busy="true">
                    @for (row of [1, 2, 3]; track row) {
                      <hlm-skeleton class="h-12 w-full" />
                    }
                  </div>
                } @else {
                  <ul class="grid gap-2">
                    @for (shipment of store.recentShipments(); track shipment.id) {
                      <li>
                        <a
                          class="flex flex-wrap items-center justify-between gap-2 rounded-md border p-3 hover:bg-muted/50"
                          [routerLink]="['/shipments', shipment.id]"
                        >
                          <span class="min-w-0">
                            <span class="block font-mono text-sm font-medium">
                              {{ shipment.trackingNumber }}
                            </span>
                            <span class="block text-xs text-muted-foreground">
                              {{ shipment.recipient.city }} ·
                              {{ shipment.createdAtUtc | pfDate: 'date' }}
                            </span>
                          </span>
                          <span class="flex items-center gap-3">
                            <span class="text-sm tabular-nums">
                              {{ shipment.price | pfMoney: shipment.currency }}
                            </span>
                            <pf-status-tag type="ShipmentStatus" [value]="shipment.status" />
                          </span>
                        </a>
                      </li>
                    } @empty {
                      <li class="text-sm text-muted-foreground">
                        {{ 'shipments.empty.message' | transloco }}
                      </li>
                    }
                  </ul>
                }
              </div>
            </div>

            <div hlmCard>
              <div hlmCardHeader>
                <h3 hlmCardTitle class="flex items-center gap-2">
                  <ng-icon name="lucideCreditCard" aria-hidden="true" />
                  {{ 'home.dashboard.pendingInvoices' | transloco }}
                </h3>
              </div>
              <div hlmCardContent>
                @if (store.loading()) {
                  <hlm-skeleton class="h-10 w-16" />
                } @else {
                  <p
                    class="text-4xl font-semibold tabular-nums"
                    [class.text-warning]="store.pendingInvoices() > 0"
                    data-testid="pending-invoices"
                  >
                    {{ store.pendingInvoices() }}
                  </p>
                  <p class="mt-1 text-sm text-muted-foreground">
                    {{ 'home.dashboard.pendingHint' | transloco }}
                  </p>
                }
              </div>
              <div hlmCardFooter>
                <a hlmBtn variant="outline" class="w-full" routerLink="/invoices">
                  <ng-icon name="lucideReceipt" />
                  {{ 'home.dashboard.viewInvoices' | transloco }}
                </a>
              </div>
            </div>
          </div>
        }
      </section>
    } @else if (!auth.authenticated()) {
      <section
        class="mt-8 flex flex-col items-start gap-4 rounded-xl border bg-card p-6 sm:flex-row sm:items-center sm:justify-between"
      >
        <div>
          <h2 class="text-lg font-semibold">{{ 'home.signIn.title' | transloco }}</h2>
          <p class="text-sm text-muted-foreground">{{ 'home.signIn.message' | transloco }}</p>
        </div>
        <button hlmBtn (click)="auth.login('/')">
          <ng-icon name="lucideLogIn" />
          {{ 'layout.login' | transloco }}
        </button>
      </section>
    }
  `,
})
export class HomePage {
  protected readonly auth = inject(AuthService);
  protected readonly store = inject(DashboardStore);
  protected readonly isCustomer = computed(() => this.auth.roles().includes('customer'));

  constructor() {
    // Follows sign-in, sign-out and identity switches without a page reload.
    this.store.load(this.isCustomer);
  }
}
