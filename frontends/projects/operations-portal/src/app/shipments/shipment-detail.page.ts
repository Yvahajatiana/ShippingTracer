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
import { lucideArrowLeft, lucideDownload, lucideMapPin } from '@ng-icons/lucide';
import {
  ErrorBannerComponent,
  PageHeaderComponent,
  PfDatePipe,
  PfMoneyPipe,
  PfNumberPipe,
  StatusTagComponent,
} from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { EmptyStateComponent, ListSkeletonComponent } from '../core/states';
import { ShipmentDetailStore } from './shipments.store';

@Component({
  selector: 'pf-shipment-detail-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    NgIcon,
    HlmButtonImports,
    HlmCardImports,
    HlmSkeletonImports,
    HlmSpinnerImports,
    ErrorBannerComponent,
    PageHeaderComponent,
    PfDatePipe,
    PfMoneyPipe,
    PfNumberPipe,
    StatusTagComponent,
    EmptyStateComponent,
    ListSkeletonComponent,
  ],
  providers: [ShipmentDetailStore, provideIcons({ lucideArrowLeft, lucideDownload, lucideMapPin })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shipment-detail.page.html',
})
export class ShipmentDetailPage {
  /** Route parameter. */
  readonly id = input.required<string>();
  protected readonly store = inject(ShipmentDetailStore);

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => this.store.load(id));
    });
  }
}
