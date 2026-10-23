import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideMapPin,
  lucidePencil,
  lucidePlus,
  lucideTrash2,
  lucideUserPlus,
} from '@ng-icons/lucide';
import {
  Address,
  ConfirmService,
  ErrorBannerComponent,
  Notifier,
  PageHeaderComponent,
  PfDatePipe,
} from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmItemImports } from '@spartan-ng/helm/item';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { AddressDialogComponent, AddressDialogMode } from './address-dialog.component';
import { ProfileFormComponent } from './profile-form.component';
import { ProfileStore } from './profile.store';

/** Customer profile: onboarding when it does not exist yet, then profile and address book. */
@Component({
  selector: 'pf-profile-page',
  imports: [
    TranslocoPipe,
    NgIcon,
    PageHeaderComponent,
    ErrorBannerComponent,
    PfDatePipe,
    HlmButtonImports,
    HlmCardImports,
    HlmEmptyImports,
    HlmItemImports,
    HlmSkeletonImports,
    ProfileFormComponent,
    AddressDialogComponent,
  ],
  providers: [
    ProfileStore,
    provideIcons({ lucideMapPin, lucidePencil, lucidePlus, lucideTrash2, lucideUserPlus }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <pf-page-header
      [title]="'profile.title' | transloco"
      [subtitle]="'profile.subtitle' | transloco"
    />

    @if (store.loading() && !store.loaded()) {
      <div hlmCard class="max-w-3xl" aria-busy="true">
        <div hlmCardContent class="grid gap-4 sm:grid-cols-2">
          @for (item of [1, 2, 3, 4]; track item) {
            <hlm-skeleton class="h-9 w-full" />
          }
        </div>
      </div>
    } @else if (store.error(); as error) {
      <pf-error-banner class="max-w-3xl" [error]="error" (retry)="store.load()" />
    } @else if (store.missing()) {
      <section hlmCard class="max-w-3xl" aria-labelledby="pf-onboarding-title">
        <div hlmCardHeader>
          <h2 hlmCardTitle id="pf-onboarding-title" class="flex items-center gap-2">
            <ng-icon name="lucideUserPlus" aria-hidden="true" />
            {{ 'profile.onboarding.title' | transloco }}
          </h2>
          <p hlmCardDescription>{{ 'profile.onboarding.description' | transloco }}</p>
        </div>
        <div hlmCardContent>
          <pf-profile-form />
        </div>
      </section>
    } @else if (store.profile(); as profile) {
      <div class="grid max-w-3xl gap-6">
        <section hlmCard aria-labelledby="pf-identity-title">
          <div hlmCardHeader>
            <h2 hlmCardTitle id="pf-identity-title">{{ 'profile.identity' | transloco }}</h2>
            <p hlmCardDescription>
              {{
                'profile.memberSince' | transloco: { date: (profile.createdAtUtc | pfDate: 'date') }
              }}
            </p>
          </div>
          <div hlmCardContent>
            <pf-profile-form [profile]="profile" />
          </div>
        </section>

        <section hlmCard aria-labelledby="pf-addresses-title">
          <div hlmCardHeader>
            <h2 hlmCardTitle id="pf-addresses-title">{{ 'address.title' | transloco }}</h2>
            <p hlmCardDescription>{{ 'address.description' | transloco }}</p>
            <div hlmCardAction>
              <button hlmBtn size="sm" variant="outline" (click)="dialog.set({ kind: 'add' })">
                <ng-icon name="lucidePlus" />
                {{ 'address.add' | transloco }}
              </button>
            </div>
          </div>
          <div hlmCardContent>
            @if (store.addresses().length === 0) {
              <div hlmEmpty class="border border-dashed p-6">
                <div hlmEmptyHeader>
                  <div hlmEmptyMedia variant="icon"><ng-icon name="lucideMapPin" /></div>
                  <h3 hlmEmptyTitle>{{ 'address.empty' | transloco }}</h3>
                </div>
              </div>
            } @else {
              <ul class="grid gap-3">
                @for (address of store.addresses(); track address.id) {
                  <li hlmItem variant="outline">
                    <div hlmItemContent class="min-w-0">
                      <div hlmItemTitle>{{ address.label }}</div>
                      <p hlmItemDescription class="whitespace-normal">
                        {{ address.line2 ? address.line1 + ', ' + address.line2 : address.line1 }}
                        <br />
                        {{ address.postalCode }} {{ address.city }} · {{ address.countryCode }}
                      </p>
                    </div>
                    <div hlmItemActions>
                      <button
                        hlmBtn
                        variant="ghost"
                        size="icon"
                        [attr.aria-label]="
                          'address.editLabel' | transloco: { label: address.label }
                        "
                        (click)="dialog.set({ kind: 'edit', address })"
                      >
                        <ng-icon name="lucidePencil" />
                      </button>
                      <button
                        hlmBtn
                        variant="ghost"
                        size="icon"
                        class="text-destructive"
                        [attr.aria-label]="
                          'address.deleteLabel' | transloco: { label: address.label }
                        "
                        [disabled]="store.saving()"
                        (click)="remove(address)"
                      >
                        <ng-icon name="lucideTrash2" />
                      </button>
                    </div>
                  </li>
                }
              </ul>
            }
          </div>
        </section>
      </div>
      <pf-address-dialog [mode]="dialog()" (closed)="dialog.set({ kind: 'closed' })" />
    }
  `,
})
export class ProfilePage {
  protected readonly store = inject(ProfileStore);
  private readonly confirm = inject(ConfirmService);
  private readonly notifier = inject(Notifier);

  protected readonly dialog = signal<AddressDialogMode>({ kind: 'closed' });

  constructor() {
    this.store.load();
  }

  protected async remove(address: Address): Promise<void> {
    const confirmed = await this.confirm.confirm({
      messageKey: 'address.deleteConfirm',
      params: { label: address.label },
      confirmKey: 'common.delete',
      destructive: true,
    });
    if (!confirmed) {
      return;
    }
    const result = await this.store.deleteAddress(address.id);
    if (result.ok) {
      this.notifier.success('address.deleted');
    } else {
      this.notifier.error(result.error);
    }
  }
}
