import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePencil, lucidePlus } from '@ng-icons/lucide';
import {
  ErrorBannerComponent,
  PageHeaderComponent,
  StatusTagComponent,
  Warehouse,
} from '@parcelflow/shared';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { EmptyStateComponent, ListSkeletonComponent } from '../core/list-states.component';
import { PagerComponent } from '../core/pager.component';
import { WarehouseDialogComponent } from './warehouse-dialog.component';
import { WarehousesStore } from './warehouses.store';

@Component({
  selector: 'pf-warehouses-page',
  imports: [
    TranslocoPipe,
    NgIcon,
    PageHeaderComponent,
    ErrorBannerComponent,
    StatusTagComponent,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmTableImports,
    EmptyStateComponent,
    ListSkeletonComponent,
    PagerComponent,
    WarehouseDialogComponent,
  ],
  providers: [WarehousesStore, provideIcons({ lucidePencil, lucidePlus })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <pf-page-header
      [title]="'warehouses.title' | transloco"
      [subtitle]="'warehouses.subtitle' | transloco"
    >
      <button hlmBtn type="button" (click)="openCreate()">
        <ng-icon name="lucidePlus" />
        {{ 'warehouses.new' | transloco }}
      </button>
    </pf-page-header>

    <section hlmCard>
      <div hlmCardContent>
        @if (store.error()) {
          <pf-error-banner [error]="store.error()" (retry)="store.reload()" />
        } @else if (store.loading() && store.items().length === 0) {
          <pf-list-skeleton />
        } @else if (store.items().length === 0) {
          <pf-empty-state titleKey="warehouses.empty" />
        } @else {
          <div hlmTableContainer [attr.aria-busy]="store.loading()">
            <table hlmTable>
              <caption class="sr-only">
                {{
                  'warehouses.title' | transloco
                }}
              </caption>
              <thead hlmTHead>
                <tr hlmTr>
                  <th hlmTh scope="col">{{ 'warehouses.fields.code' | transloco }}</th>
                  <th hlmTh scope="col">{{ 'warehouses.fields.name' | transloco }}</th>
                  <th hlmTh scope="col" class="hidden md:table-cell">
                    {{ 'warehouses.fields.kind' | transloco }}
                  </th>
                  <th hlmTh scope="col" class="hidden lg:table-cell">
                    {{ 'warehouses.fields.address' | transloco }}
                  </th>
                  <th hlmTh scope="col" class="hidden sm:table-cell">
                    {{ 'common.status' | transloco }}
                  </th>
                  <th hlmTh scope="col" class="text-right">{{ 'common.actions' | transloco }}</th>
                </tr>
              </thead>
              <tbody hlmTBody>
                @for (warehouse of store.items(); track warehouse.id) {
                  <tr hlmTr>
                    <td hlmTd class="font-mono font-medium">{{ warehouse.code }}</td>
                    <td hlmTd>
                      <div>{{ warehouse.name }}</div>
                      <div class="text-xs text-muted-foreground lg:hidden">
                        {{ warehouse.city }} ({{ warehouse.countryCode }})
                      </div>
                    </td>
                    <td hlmTd class="hidden md:table-cell">
                      <pf-status-tag type="WarehouseKind" [value]="warehouse.kind" />
                    </td>
                    <td hlmTd class="hidden lg:table-cell">
                      {{ warehouse.street }}, {{ warehouse.postalCode }} {{ warehouse.city }} ({{
                        warehouse.countryCode
                      }})
                    </td>
                    <td hlmTd class="hidden sm:table-cell">
                      <span
                        hlmBadge
                        [variant]="warehouse.isActive ? 'secondary' : 'outline'"
                        [class]="warehouse.isActive ? 'bg-success/15 text-success' : ''"
                      >
                        {{ (warehouse.isActive ? 'common.active' : 'common.inactive') | transloco }}
                      </span>
                    </td>
                    <td hlmTd class="text-right">
                      <button
                        hlmBtn
                        type="button"
                        variant="ghost"
                        size="icon-sm"
                        [attr.aria-label]="
                          'warehouses.editNamed' | transloco: { code: warehouse.code }
                        "
                        (click)="openEdit(warehouse)"
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
        <pf-pager
          [page]="store.page()"
          [pageCount]="store.pageCount()"
          [total]="store.total()"
          [hasPrevious]="store.hasPrevious()"
          [hasNext]="store.hasNext()"
          [disabled]="store.loading()"
          (previous)="store.previousPage()"
          (next)="store.nextPage()"
        />
      </div>
    </section>

    <pf-warehouse-dialog [(open)]="dialogOpen" [warehouse]="editing()" />
  `,
})
export class WarehousesPage {
  protected readonly store = inject(WarehousesStore);
  protected readonly dialogOpen = signal(false);
  protected readonly editing = signal<Warehouse | null>(null);

  constructor() {
    this.store.loadPage(1);
  }

  openCreate(): void {
    this.editing.set(null);
    this.dialogOpen.set(true);
  }

  openEdit(warehouse: Warehouse): void {
    this.editing.set(warehouse);
    this.dialogOpen.set(true);
  }
}
