import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus, lucideRefreshCw, lucideRoute } from '@ng-icons/lucide';
import {
  DeliveryRoute,
  ErrorBannerComponent,
  PageHeaderComponent,
  PfDatePipe,
  StatusTagComponent,
} from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { PagerComponent } from '../core/pager.component';
import { EmptyStateComponent, ListSkeletonComponent } from '../core/states';
import { CreateRouteFormComponent } from './create-route-form.component';
import { RouteListStore } from './delivery-lists.store';

@Component({
  selector: 'pf-route-list-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    NgIcon,
    HlmButtonImports,
    HlmCardImports,
    HlmDialogImports,
    HlmTableImports,
    ErrorBannerComponent,
    PageHeaderComponent,
    PfDatePipe,
    StatusTagComponent,
    PagerComponent,
    EmptyStateComponent,
    ListSkeletonComponent,
    CreateRouteFormComponent,
  ],
  providers: [RouteListStore, provideIcons({ lucidePlus, lucideRefreshCw, lucideRoute })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <pf-page-header [title]="'routes.title' | transloco" [subtitle]="'routes.subtitle' | transloco">
      <button
        hlmBtn
        variant="outline"
        size="icon"
        type="button"
        [attr.aria-label]="'common.refresh' | transloco"
        (click)="store.reload()"
      >
        <ng-icon name="lucideRefreshCw" />
      </button>
      <button hlmBtn type="button" data-testid="open-create-route" (click)="creating.set(true)">
        <ng-icon name="lucidePlus" />
        {{ 'routes.create.open' | transloco }}
      </button>
    </pf-page-header>

    <div hlmCard>
      <div hlmCardContent>
        @if (store.error(); as error) {
          <pf-error-banner [error]="error" (retry)="store.reload()" />
        } @else if (store.loading() && !store.loaded()) {
          <pf-list-skeleton />
        } @else if (store.isEmpty()) {
          <pf-empty-state icon="lucideRoute" [title]="'routes.empty' | transloco" />
        } @else {
          <div hlmTableContainer class="hidden md:block" [attr.aria-busy]="store.loading()">
            <table hlmTable>
              <thead hlmTHead>
                <tr hlmTr>
                  <th hlmTh>{{ 'routes.name' | transloco }}</th>
                  <th hlmTh>{{ 'routes.serviceDate' | transloco }}</th>
                  <th hlmTh>{{ 'routes.driver' | transloco }}</th>
                  <th hlmTh>{{ 'common.status' | transloco }}</th>
                  <th hlmTh class="text-right">{{ 'routes.stops' | transloco }}</th>
                </tr>
              </thead>
              <tbody hlmTBody>
                @for (route of store.items(); track route.id) {
                  <tr hlmTr>
                    <td hlmTd>
                      <a
                        class="font-medium text-primary hover:underline"
                        [routerLink]="['/delivery/routes', route.id]"
                        >{{ route.name }}</a
                      >
                    </td>
                    <td hlmTd>{{ route.serviceDate | pfDate: 'date' }}</td>
                    <td hlmTd>{{ driverName(route) }}</td>
                    <td hlmTd>
                      <pf-status-tag type="DeliveryRouteStatus" [value]="route.status" />
                    </td>
                    <td hlmTd class="text-right">{{ stopSummary(route) }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <ul class="space-y-3 md:hidden">
            @for (route of store.items(); track route.id) {
              <li>
                <a
                  class="block rounded-lg border bg-background p-3 hover:bg-muted/50"
                  [routerLink]="['/delivery/routes', route.id]"
                >
                  <div class="flex items-center justify-between gap-2">
                    <span class="font-medium text-primary">{{ route.name }}</span>
                    <pf-status-tag type="DeliveryRouteStatus" [value]="route.status" />
                  </div>
                  <p class="mt-1 text-sm text-muted-foreground">
                    {{ route.serviceDate | pfDate: 'date' }} · {{ driverName(route) }}
                  </p>
                  <p class="text-xs text-muted-foreground">
                    {{ 'routes.stopsLabel' | transloco: { summary: stopSummary(route) } }}
                  </p>
                </a>
              </li>
            }
          </ul>
        }
        <pf-pager
          [page]="store.page()"
          [pageCount]="store.pageCount()"
          [hasPrevious]="store.hasPrevious()"
          [hasNext]="store.hasMore()"
          [disabled]="store.loading()"
          (previous)="store.previous()"
          (next)="store.next()"
        />
      </div>
    </div>

    <hlm-dialog [state]="creating() ? 'open' : 'closed'" (closed)="creating.set(false)">
      <hlm-dialog-content *hlmDialogPortal="let ctx" [closeLabel]="'common.close' | transloco">
        @if (creating()) {
          <pf-create-route-form (created)="onCreated($event)" (cancelled)="creating.set(false)" />
        }
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class RouteListPage {
  protected readonly store = inject(RouteListStore);
  private readonly router = inject(Router);
  protected readonly creating = signal(false);

  protected driverName(route: DeliveryRoute): string {
    if (!route.driverSubject) {
      return '—';
    }
    return this.store.driverNames().get(route.driverSubject) ?? route.driverSubject;
  }

  /** "done / total" where done counts delivered and failed stops. */
  protected stopSummary(route: DeliveryRoute): string {
    const done = route.stops.filter((stop) => stop.status !== 'Pending').length;
    return `${done} / ${route.stops.length}`;
  }

  protected onCreated(route: DeliveryRoute): void {
    this.creating.set(false);
    void this.router.navigate(['/delivery/routes', route.id]);
  }
}
