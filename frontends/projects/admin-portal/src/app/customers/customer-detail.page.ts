import { ChangeDetectionStrategy, Component, inject, input, linkedSignal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowLeft } from '@ng-icons/lucide';
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
  providers: [CustomerDetailStore, provideIcons({ lucideArrowLeft })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a hlmBtn variant="ghost" size="sm" class="mb-4" routerLink="/customers">
      <ng-icon name="lucideArrowLeft" />
      {{ 'customers.backToList' | transloco }}
    </a>

    @if (store.error()) {
      <pf-error-banner [error]="store.error()" (retry)="store.load(id())" />
    } @else if (!store.profile()) {
      <div class="grid gap-4" role="status" [attr.aria-label]="'common.loading' | transloco">
        <hlm-skeleton class="h-8 w-64" />
        <hlm-skeleton class="h-40 w-full" />
        <hlm-skeleton class="h-40 w-full" />
      </div>
    } @else {
      @let profile = store.profile()!;
      <pf-page-header
        [title]="profile.firstName + ' ' + profile.lastName"
        [subtitle]="profile.email"
      />

      <div class="grid gap-6 lg:grid-cols-2">
        <section hlmCard aria-labelledby="customer-company-title">
          <div hlmCardHeader>
            <h2 hlmCardTitle id="customer-company-title">
              {{ 'customers.company.title' | transloco }}
            </h2>
            <p hlmCardDescription>
              @if (store.company(); as company) {
                {{ 'customers.company.current' | transloco: { name: company.name } }}
              } @else {
                {{ 'customers.company.noneCurrent' | transloco }}
              }
            </p>
          </div>
          <div hlmCardContent>
            <form class="flex flex-col gap-3 sm:flex-row sm:items-end" (ngSubmit)="assign()">
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
                [disabled]="store.assigning() || selectedCompany() === (profile.companyId ?? '')"
              >
                @if (store.assigning()) {
                  <hlm-spinner class="size-4" [aria-label]="'common.loading' | transloco" />
                }
                {{ 'customers.company.assign' | transloco }}
              </button>
            </form>
          </div>
        </section>

        <section hlmCard aria-labelledby="customer-profile-title">
          <div hlmCardHeader>
            <h2 hlmCardTitle id="customer-profile-title">
              {{ 'customers.profile' | transloco }}
            </h2>
          </div>
          <dl hlmCardContent class="grid grid-cols-1 gap-3 sm:grid-cols-[auto_1fr] sm:gap-x-6">
            <dt class="text-muted-foreground">{{ 'customers.fields.name' | transloco }}</dt>
            <dd>{{ profile.firstName }} {{ profile.lastName }}</dd>
            <dt class="text-muted-foreground">{{ 'customers.fields.email' | transloco }}</dt>
            <dd class="break-all">{{ profile.email }}</dd>
            <dt class="text-muted-foreground">{{ 'customers.fields.phone' | transloco }}</dt>
            <dd>{{ profile.phone ?? '—' }}</dd>
            <dt class="text-muted-foreground">{{ 'customers.fields.subject' | transloco }}</dt>
            <dd class="font-mono text-xs break-all">{{ profile.keycloakSubject }}</dd>
            <dt class="text-muted-foreground">{{ 'common.createdAt' | transloco }}</dt>
            <dd>{{ profile.createdAtUtc | pfDate }}</dd>
          </dl>
        </section>

        <section hlmCard class="lg:col-span-2" aria-labelledby="customer-addresses-title">
          <div hlmCardHeader>
            <h2 hlmCardTitle id="customer-addresses-title">
              {{ 'customers.addresses' | transloco }}
            </h2>
          </div>
          <div hlmCardContent>
            @if (profile.addresses.length === 0) {
              <pf-empty-state titleKey="customers.noAddresses" />
            } @else {
              <ul class="grid gap-3 sm:grid-cols-2">
                @for (address of profile.addresses; track address.id) {
                  <li class="rounded-lg border p-3">
                    <p class="font-medium">{{ address.label }}</p>
                    <address class="text-muted-foreground not-italic">
                      {{ address.line1 }}<br />
                      @if (address.line2) {
                        {{ address.line2 }}<br />
                      }
                      {{ address.postalCode }} {{ address.city }}<br />
                      {{ address.countryCode }}
                    </address>
                  </li>
                }
              </ul>
            }
          </div>
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
