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
import { HlmTableImports } from '@spartan-ng/helm/table';
import { EmptyStateComponent, ListSkeletonComponent } from '../core/list-states.component';
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
    HlmTableImports,
    EmptyStateComponent,
    ListSkeletonComponent,
    TariffDialogComponent,
  ],
  providers: [TariffsStore, provideIcons({ lucidePencil })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <pf-page-header
      [eyebrow]="'nav.groups.referenceData' | transloco"
      [title]="'tariffs.title' | transloco"
      [subtitle]="'tariffs.subtitle' | transloco"
    />

    <section hlmCard class="gap-0 py-0">
      @if (store.error()) {
        <div class="p-5">
          <pf-error-banner [error]="store.error()" (retry)="store.load()" />
        </div>
      } @else if (store.loading() && store.tariffs().length === 0) {
        <div class="p-5">
          <pf-list-skeleton [rows]="3" />
        </div>
      } @else if (store.ordered().length === 0) {
        <div class="p-5">
          <pf-empty-state titleKey="tariffs.empty" />
        </div>
      } @else {
        <div hlmTableContainer class="[&_tr>*:first-child]:pl-5! [&_tr>*:last-child]:pr-5!">
          <table hlmTable>
            <caption class="sr-only">
              {{
                'tariffs.title' | transloco
              }}
            </caption>
            <thead hlmTHead>
              <tr hlmTr>
                <th hlmTh scope="col">{{ 'tariffs.zone' | transloco }}</th>
                <th hlmTh scope="col" class="text-right">{{ 'tariffs.basePrice' | transloco }}</th>
                <th hlmTh scope="col" class="text-right">
                  {{ 'tariffs.pricePerKg' | transloco }}
                </th>
                <th hlmTh scope="col" class="text-right">{{ 'common.actions' | transloco }}</th>
              </tr>
            </thead>
            <tbody hlmTBody>
              @for (tariff of store.ordered(); track tariff.zone) {
                <tr hlmTr>
                  <td hlmTd>
                    <div class="font-medium">{{ zoneKey(tariff) | transloco }}</div>
                    <div class="text-xs text-muted-foreground">
                      {{ 'tariffs.currency' | transloco: { currency: tariff.currency } }}
                    </div>
                  </td>
                  <td hlmTd class="text-right font-mono text-[13px] tabular-nums">
                    {{ tariff.basePrice | pfMoney: tariff.currency }}
                  </td>
                  <td hlmTd class="text-right font-mono text-[13px] tabular-nums">
                    {{ tariff.pricePerKg | pfMoney: tariff.currency }}
                  </td>
                  <td hlmTd class="text-right">
                    <button
                      hlmBtn
                      type="button"
                      variant="ghost"
                      size="icon-sm"
                      class="max-sm:size-10"
                      [attr.aria-label]="
                        'tariffs.editNamed' | transloco: { zone: (zoneKey(tariff) | transloco) }
                      "
                      (click)="edit(tariff)"
                    >
                      <ng-icon name="lucidePencil" />
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </section>

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
