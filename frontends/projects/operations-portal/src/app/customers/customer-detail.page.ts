import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  input,
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowLeft, lucideBuilding2, lucideMapPin } from '@ng-icons/lucide';
import { ErrorBannerComponent, PageHeaderComponent, PfDatePipe } from '@parcelflow/shared';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { EmptyStateComponent, ListSkeletonComponent } from '../core/states';
import { CustomerDetailStore } from './customers.store';

@Component({
  selector: 'pf-customer-detail-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    ErrorBannerComponent,
    PageHeaderComponent,
    PfDatePipe,
    EmptyStateComponent,
    ListSkeletonComponent,
  ],
  providers: [
    CustomerDetailStore,
    provideIcons({ lucideArrowLeft, lucideBuilding2, lucideMapPin }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a hlmBtn variant="ghost" size="sm" class="mb-2 -ml-2" routerLink="/customers">
      <ng-icon name="lucideArrowLeft" />
      {{ 'customers.backToList' | transloco }}
    </a>

    @if (store.error(); as error) {
      <pf-error-banner [error]="error" (retry)="store.load(id())" />
    } @else if (store.loading() && !store.profile()) {
      <pf-list-skeleton [rows]="6" />
    } @else if (store.profile(); as profile) {
      <pf-page-header
        [title]="profile.firstName + ' ' + profile.lastName"
        [subtitle]="
          'customers.since' | transloco: { date: (profile.createdAtUtc | pfDate: 'date') }
        "
      />

      <div class="grid gap-6 lg:grid-cols-3">
        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'customers.profile' | transloco }}</h2>
          </div>
          <dl hlmCardContent class="space-y-3">
            <div>
              <dt class="text-xs text-muted-foreground">{{ 'customers.email' | transloco }}</dt>
              <dd class="break-all">{{ profile.email }}</dd>
            </div>
            <div>
              <dt class="text-xs text-muted-foreground">{{ 'customers.phone' | transloco }}</dt>
              <dd>{{ profile.phone ?? '—' }}</dd>
            </div>
            <div>
              <dt class="text-xs text-muted-foreground">{{ 'customers.subject' | transloco }}</dt>
              <dd class="font-mono text-sm break-all">{{ profile.keycloakSubject }}</dd>
            </div>
            <div>
              <dt class="text-xs text-muted-foreground">{{ 'customers.company' | transloco }}</dt>
              <dd class="flex items-center gap-2" data-testid="customer-company">
                @if (store.company(); as company) {
                  <ng-icon name="lucideBuilding2" aria-hidden="true" />
                  {{ company.name }}
                } @else if (profile.companyId) {
                  <span class="text-muted-foreground">{{
                    'customers.companyUnknown' | transloco
                  }}</span>
                } @else {
                  <span class="text-muted-foreground">{{ 'customers.noCompany' | transloco }}</span>
                }
              </dd>
            </div>
          </dl>
        </section>

        <section hlmCard class="lg:col-span-2">
          <div hlmCardHeader>
            <h2 hlmCardTitle>{{ 'customers.addresses' | transloco }}</h2>
          </div>
          <div hlmCardContent>
            @if (profile.addresses.length === 0) {
              <pf-empty-state icon="lucideMapPin" [title]="'customers.noAddress' | transloco" />
            } @else {
              <ul class="grid gap-4 sm:grid-cols-2">
                @for (address of profile.addresses; track address.id) {
                  <li class="rounded-lg border p-3">
                    <span hlmBadge variant="secondary">{{ address.label }}</span>
                    <address class="mt-2 space-y-0.5 text-sm not-italic">
                      <p>{{ address.line1 }}</p>
                      @if (address.line2) {
                        <p>{{ address.line2 }}</p>
                      }
                      <p>{{ address.postalCode }} {{ address.city }} · {{ address.countryCode }}</p>
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
  /** Route parameter. */
  readonly id = input.required<string>();
  protected readonly store = inject(CustomerDetailStore);

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => this.store.load(id));
    });
  }
}
