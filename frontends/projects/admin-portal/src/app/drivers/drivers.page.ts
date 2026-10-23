import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePencil, lucidePlus } from '@ng-icons/lucide';
import { Driver, ErrorBannerComponent, PageHeaderComponent } from '@parcelflow/shared';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { EmptyStateComponent, ListSkeletonComponent } from '../core/list-states.component';
import { PagerComponent } from '../core/pager.component';
import { DriverDialogComponent } from './driver-dialog.component';
import { DriversStore } from './drivers.store';

@Component({
  selector: 'pf-drivers-page',
  imports: [
    TranslocoPipe,
    NgIcon,
    PageHeaderComponent,
    ErrorBannerComponent,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmTableImports,
    EmptyStateComponent,
    ListSkeletonComponent,
    PagerComponent,
    DriverDialogComponent,
  ],
  providers: [DriversStore, provideIcons({ lucidePencil, lucidePlus })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <pf-page-header
      [title]="'drivers.title' | transloco"
      [subtitle]="'drivers.subtitle' | transloco"
    >
      <button hlmBtn type="button" (click)="openCreate()">
        <ng-icon name="lucidePlus" />
        {{ 'drivers.new' | transloco }}
      </button>
    </pf-page-header>

    <section hlmCard>
      <div hlmCardContent>
        @if (store.error()) {
          <pf-error-banner [error]="store.error()" (retry)="store.reload()" />
        } @else if (store.loading() && store.items().length === 0) {
          <pf-list-skeleton />
        } @else if (store.items().length === 0) {
          <pf-empty-state titleKey="drivers.empty" />
        } @else {
          <div hlmTableContainer [attr.aria-busy]="store.loading()">
            <table hlmTable>
              <caption class="sr-only">
                {{
                  'drivers.title' | transloco
                }}
              </caption>
              <thead hlmTHead>
                <tr hlmTr>
                  <th hlmTh scope="col">{{ 'drivers.fields.displayName' | transloco }}</th>
                  <th hlmTh scope="col" class="hidden sm:table-cell">
                    {{ 'drivers.fields.subject' | transloco }}
                  </th>
                  <th hlmTh scope="col">{{ 'common.status' | transloco }}</th>
                  <th hlmTh scope="col" class="text-right">{{ 'common.actions' | transloco }}</th>
                </tr>
              </thead>
              <tbody hlmTBody>
                @for (driver of store.items(); track driver.subject) {
                  <tr hlmTr>
                    <td hlmTd class="font-medium">
                      <div>{{ driver.displayName }}</div>
                      <div class="font-mono text-xs text-muted-foreground sm:hidden">
                        {{ driver.subject }}
                      </div>
                    </td>
                    <td hlmTd class="hidden font-mono text-xs sm:table-cell">
                      {{ driver.subject }}
                    </td>
                    <td hlmTd>
                      <span
                        hlmBadge
                        [variant]="driver.isActive ? 'secondary' : 'outline'"
                        [class]="driver.isActive ? 'bg-success/15 text-success' : ''"
                      >
                        {{ (driver.isActive ? 'common.active' : 'common.inactive') | transloco }}
                      </span>
                    </td>
                    <td hlmTd class="text-right">
                      <button
                        hlmBtn
                        type="button"
                        variant="ghost"
                        size="icon-sm"
                        [attr.aria-label]="
                          'drivers.editNamed' | transloco: { name: driver.displayName }
                        "
                        (click)="openEdit(driver)"
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

    <pf-driver-dialog [(open)]="dialogOpen" [driver]="editing()" />
  `,
})
export class DriversPage {
  protected readonly store = inject(DriversStore);
  protected readonly dialogOpen = signal(false);
  protected readonly editing = signal<Driver | null>(null);

  constructor() {
    this.store.loadPage(1);
  }

  openCreate(): void {
    this.editing.set(null);
    this.dialogOpen.set(true);
  }

  openEdit(driver: Driver): void {
    this.editing.set(driver);
    this.dialogOpen.set(true);
  }
}
