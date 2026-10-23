import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePencil } from '@ng-icons/lucide';
import {
  ErrorBannerComponent,
  PageHeaderComponent,
  PfMoneyPipe,
  Tariff,
  enumKey,
} from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { EmptyStateComponent } from '../core/list-states.component';
import { TariffDialogComponent } from './tariff-dialog.component';
import { TariffsStore } from './tariffs.store';

@Component({
  selector: 'pf-tariffs-page',
  imports: [
    TranslocoPipe,
    NgIcon,
    PfMoneyPipe,
    PageHeaderComponent,
    ErrorBannerComponent,
    HlmButtonImports,
    HlmCardImports,
    HlmSkeletonImports,
    EmptyStateComponent,
    TariffDialogComponent,
  ],
  providers: [TariffsStore, provideIcons({ lucidePencil })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <pf-page-header
      [title]="'tariffs.title' | transloco"
      [subtitle]="'tariffs.subtitle' | transloco"
    />

    @if (store.error()) {
      <pf-error-banner [error]="store.error()" (retry)="store.load()" />
    } @else if (store.loading() && store.tariffs().length === 0) {
      <div
        class="grid gap-4 md:grid-cols-3"
        role="status"
        [attr.aria-label]="'common.loading' | transloco"
      >
        @for (placeholder of [1, 2, 3]; track placeholder) {
          <hlm-skeleton class="h-44 w-full rounded-xl" />
        }
      </div>
    } @else if (store.ordered().length === 0) {
      <pf-empty-state titleKey="tariffs.empty" />
    } @else {
      <ul class="grid gap-4 md:grid-cols-3">
        @for (tariff of store.ordered(); track tariff.zone) {
          <li hlmCard>
            <div hlmCardHeader>
              <h2 hlmCardTitle>{{ zoneKey(tariff) | transloco }}</h2>
              <p hlmCardDescription>
                {{ 'tariffs.currency' | transloco: { currency: tariff.currency } }}
              </p>
            </div>
            <dl hlmCardContent class="grid grid-cols-[1fr_auto] gap-2">
              <dt class="text-muted-foreground">{{ 'tariffs.basePrice' | transloco }}</dt>
              <dd class="text-right font-medium tabular-nums">
                {{ tariff.basePrice | pfMoney: tariff.currency }}
              </dd>
              <dt class="text-muted-foreground">{{ 'tariffs.pricePerKg' | transloco }}</dt>
              <dd class="text-right font-medium tabular-nums">
                {{ tariff.pricePerKg | pfMoney: tariff.currency }}
              </dd>
            </dl>
            <div hlmCardFooter>
              <button
                hlmBtn
                type="button"
                variant="outline"
                class="w-full"
                [attr.aria-label]="
                  'tariffs.editNamed' | transloco: { zone: (zoneKey(tariff) | transloco) }
                "
                (click)="edit(tariff)"
              >
                <ng-icon name="lucidePencil" />
                {{ 'common.edit' | transloco }}
              </button>
            </div>
          </li>
        }
      </ul>
    }

    <pf-tariff-dialog [(open)]="dialogOpen" [tariff]="editing()" />
  `,
})
export class TariffsPage {
  protected readonly store = inject(TariffsStore);
  protected readonly dialogOpen = signal(false);
  protected readonly editing = signal<Tariff | null>(null);

  constructor() {
    this.store.load();
  }

  protected zoneKey(tariff: Tariff): string {
    return enumKey('DeliveryZone', tariff.zone);
  }

  edit(tariff: Tariff): void {
    this.editing.set(tariff);
    this.dialogOpen.set(true);
  }
}
