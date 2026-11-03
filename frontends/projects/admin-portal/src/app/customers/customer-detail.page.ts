import { ChangeDetectionStrategy, Component, inject, input, linkedSignal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronLeft } from '@ng-icons/lucide';
import {
  ErrorBannerComponent,
  Notifier,
  PageHeaderComponent,
  PfDatePipe,
} from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { EmptyStateComponent } from '../core/list-states.component';
import { CustomerDetailStore } from './customer-detail.store';

@Component({
  selector: 'pf-customer-detail-page',
  imports: [
    FormsModule,
    RouterLink,
    TranslocoPipe,
    NgIcon,
    PfDatePipe,
    PageHeaderComponent,
    ErrorBannerComponent,
    HlmButtonImports,
    HlmCardImports,
    HlmFieldImports,
    HlmNativeSelectImports,
    HlmSkeletonImports,
    HlmSpinnerImports,
    EmptyStateComponent,
  ],
  providers: [CustomerDetailStore, provideIcons({ lucideChevronLeft })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a
      class="mb-3 inline-flex min-h-10 items-center gap-1 rounded-sm text-sm font-medium text-primary hover:underline focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none sm:min-h-0"
      routerLink="/customers"
    >
      <ng-icon name="lucideChevronLeft" aria-hidden="true" />
      {{ 'customers.backToList' | transloco }}
    </a>

    @if (store.error()) {
      <pf-error-banner [error]="store.error()" (retry)="store.load(id())" />
    } @else if (!store.profile()) {
      <div class="grid gap-4" role="status" [attr.aria-label]="'common.loading' | transloco">
        <hlm-skeleton class="h-8 w-64" />
        <div class="flex flex-wrap gap-6">
          <hlm-skeleton class="h-64 min-w-0 flex-[2_1_480px]" />
          <hlm-skeleton class="h-64 min-w-0 flex-[1_1_280px]" />
        </div>
      </div>
    } @else {
      @let profile = store.profile()!;
      <pf-page-header
        [eyebrow]="'nav.groups.referenceData' | transloco"
        [title]="profile.firstName + ' ' + profile.lastName"
        [subtitle]="profile.email"
      />

      <div class="flex flex-wrap items-start gap-6">
        <div class="flex min-w-0 flex-[2_1_480px] flex-col gap-6">
          <section hlmCard class="gap-0 py-0" aria-labelledby="customer-company-title">
            <div class="border-b px-6 py-4">
              <h2 id="customer-company-title" class="text-[15px] font-semibold">
                {{ 'customers.company.title' | transloco }}
              </h2>
              <p class="mt-1 text-[13px] text-muted-foreground">
                @if (store.company(); as company) {
                  {{ 'customers.company.current' | transloco: { name: company.name } }}
                } @else {
                  {{ 'customers.company.noneCurrent' | transloco }}
                }
              </p>
            </div>
            <form
              class="flex flex-col gap-3 px-6 py-5 sm:flex-row sm:items-end"
              (ngSubmit)="assign()"
            >
              <div hlmField class="flex-1">
                <label hlmFieldLabel for="customer-company">
                  {{ 'customers.company.select' | transloco }}
                </label>
                <hlm-native-select
                  class="w-full"
                  selectId="customer-company"
                  [value]="selectedCompany()"
                  (valueChange)="selectedCompany.set($event ?? '')"
                >
                  <option value="">{{ 'customers.company.none' | transloco }}</option>
                  @for (company of store.sortedCompanies(); track company.id) {
                    <option [value]="company.id">{{ company.name }}</option>
                  }
                </hlm-native-select>
              </div>
              <button
                hlmBtn
                type="submit"
                class="max-sm:h-10"
                [disabled]="store.assigning() || selectedCompany() === (profile.companyId ?? '')"
              >
                @if (store.assigning()) {
                  <hlm-spinner class="size-4" [aria-label]="'common.loading' | transloco" />
                }
                {{ 'customers.company.assign' | transloco }}
              </button>
            </form>
          </section>

          <section hlmCard class="gap-0 py-0" aria-labelledby="customer-addresses-title">
            <div class="border-b px-6 py-4">
              <h2 id="customer-addresses-title" class="text-[15px] font-semibold">
                {{ 'customers.addresses' | transloco }}
              </h2>
            </div>
            <div class="px-6 py-5">
              @if (profile.addresses.length === 0) {
                <pf-empty-state titleKey="customers.noAddresses" />
              } @else {
                <ul class="grid gap-3 sm:grid-cols-2">
                  @for (address of profile.addresses; track address.id) {
                    <li class="rounded-md border p-4">
                      <p class="font-medium">{{ address.label }}</p>
                      <address class="mt-1 text-sm text-muted-foreground not-italic">
                        {{ address.line1 }}<br />
                        @if (address.line2) {
                          {{ address.line2 }}<br />
                        }
                        {{ address.postalCode }} {{ address.city }}<br />
                        <span class="font-mono text-[13px]">{{ address.countryCode }}</span>
                      </address>
                    </li>
                  }
                </ul>
              }
            </div>
          </section>
        </div>

        <section
          hlmCard
          class="min-w-0 flex-[1_1_280px] gap-0 py-0"
          aria-labelledby="customer-profile-title"
        >
          <div class="border-b px-6 py-4">
            <h2 id="customer-profile-title" class="text-[15px] font-semibold">
              {{ 'customers.profile' | transloco }}
            </h2>
          </div>
          <dl class="px-6 py-2">
            <div class="flex justify-between gap-4 border-b py-2.5">
              <dt class="text-[13px] text-muted-foreground">
                {{ 'customers.fields.name' | transloco }}
              </dt>
              <dd class="text-right text-sm">{{ profile.firstName }} {{ profile.lastName }}</dd>
            </div>
            <div class="flex justify-between gap-4 border-b py-2.5">
              <dt class="text-[13px] text-muted-foreground">
                {{ 'customers.fields.email' | transloco }}
              </dt>
              <dd class="min-w-0 text-right text-sm break-all">{{ profile.email }}</dd>
            </div>
            <div class="flex justify-between gap-4 border-b py-2.5">
              <dt class="text-[13px] text-muted-foreground">
                {{ 'customers.fields.phone' | transloco }}
              </dt>
              <dd class="text-right text-sm tabular-nums">{{ profile.phone ?? '—' }}</dd>
            </div>
            <div class="flex justify-between gap-4 border-b py-2.5">
              <dt class="shrink-0 text-[13px] text-muted-foreground">
                {{ 'customers.fields.subject' | transloco }}
              </dt>
              <dd class="min-w-0 text-right font-mono text-[13px] break-all">
                {{ profile.keycloakSubject }}
              </dd>
            </div>
            <div class="flex justify-between gap-4 py-2.5">
              <dt class="text-[13px] text-muted-foreground">
                {{ 'common.createdAt' | transloco }}
              </dt>
              <dd class="text-right text-sm tabular-nums">{{ profile.createdAtUtc | pfDate }}</dd>
            </div>
          </dl>
        </section>
      </div>
    }
  `,
})
export class CustomerDetailPage {
  protected readonly store = inject(CustomerDetailStore);
  private readonly notifier = inject(Notifier);

  /** Route parameter bound by withComponentInputBinding. */
  readonly id = input.required<string>();

  /** Selected company id ('' = none), reset whenever the loaded assignment changes. */
  protected readonly selectedCompany = linkedSignal(() => this.store.profile()?.companyId ?? '');

  constructor() {
    this.store.load(this.id);
  }

  async assign(): Promise<void> {
    if (this.store.assigning()) {
      return;
    }
    const companyId = this.selectedCompany() || null;
    try {
      await this.store.assignCompany(companyId);
      this.notifier.success(
        companyId ? 'customers.company.assigned' : 'customers.company.unassigned',
      );
    } catch (error) {
      this.notifier.error(error);
    }
  }
}
