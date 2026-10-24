import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowLeft,
  lucideCircleCheck,
  lucideCircleX,
  lucideEye,
  lucidePlay,
  lucidePlus,
  lucideRefreshCw,
  lucideTrash2,
  lucideUserCheck,
} from '@ng-icons/lucide';
import {
  ConfirmService,
  ErrorBannerComponent,
  PageHeaderComponent,
  PfDatePipe,
  StatusTagComponent,
} from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmProgressImports } from '@spartan-ng/helm/progress';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { PagerComponent } from '../core/pager.component';
import { EmptyStateComponent, ListSkeletonComponent } from '../core/states';
import { ConfirmDeliveryFormComponent } from './confirm-delivery-form.component';
import { FailDeliveryFormComponent } from './fail-delivery-form.component';
import { ProofViewComponent } from './proof-view.component';
import { RouteDetailStore, StopView } from './route-detail.store';

interface StopDialog {
  kind: 'confirm' | 'fail' | 'proof';
  stop: StopView;
}

@Component({
  selector: 'pf-route-detail-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    NgIcon,
    HlmButtonImports,
    HlmCardImports,
    HlmDialogImports,
    HlmLabelImports,
    HlmNativeSelectImports,
    HlmProgressImports,
    HlmSkeletonImports,
    HlmSpinnerImports,
    ErrorBannerComponent,
    PageHeaderComponent,
    PfDatePipe,
    StatusTagComponent,
    EmptyStateComponent,
    ListSkeletonComponent,
    PagerComponent,
    ConfirmDeliveryFormComponent,
    FailDeliveryFormComponent,
    ProofViewComponent,
  ],
  providers: [
    RouteDetailStore,
    provideIcons({
      lucideArrowLeft,
      lucideCircleCheck,
      lucideCircleX,
      lucideEye,
      lucidePlay,
      lucidePlus,
      lucideRefreshCw,
      lucideTrash2,
      lucideUserCheck,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './route-detail.page.html',
})
export class RouteDetailPage {
  /** Route parameter. */
  readonly id = input.required<string>();
  protected readonly store = inject(RouteDetailStore);
  private readonly confirm = inject(ConfirmService);

  protected readonly selectedDriver = signal('');
  protected readonly selectedParcel = signal('');
  protected readonly dialog = signal<StopDialog | null>(null);
  protected readonly dialogState = computed(() => (this.dialog() ? 'open' : 'closed'));

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => void this.store.load(id));
    });
    // Preselects the current driver in the picker.
    effect(() => {
      const subject = this.store.route()?.driverSubject ?? '';
      untracked(() => this.selectedDriver.set(subject));
    });
    // Drops a parcel selection that is no longer offered (other page, already planned).
    effect(() => {
      const parcels = this.store.parcels();
      untracked(() => {
        const selected = this.selectedParcel();
        if (selected && !parcels.some((parcel) => parcel.shipmentId === selected)) {
          this.selectedParcel.set('');
        }
      });
    });
  }

  protected label(stop: StopView): string {
    return stop.trackingNumber ?? stop.shipmentId.slice(0, 8);
  }

  protected async assignDriver(): Promise<void> {
    const subject = this.selectedDriver();
    if (subject) {
      await this.store.assignDriver(subject);
    }
  }

  protected async addStop(): Promise<void> {
    const shipmentId = this.selectedParcel();
    if (shipmentId && (await this.store.addStop(shipmentId))) {
      this.selectedParcel.set('');
    }
  }

  protected async previousParcels(): Promise<void> {
    this.selectedParcel.set('');
    await this.store.previousParcels();
  }

  protected async nextParcels(): Promise<void> {
    this.selectedParcel.set('');
    await this.store.nextParcels();
  }

  protected async removeStop(stop: StopView): Promise<void> {
    const confirmed = await this.confirm.confirm({
      titleKey: 'routes.detail.removeStopTitle',
      messageKey: 'routes.detail.removeStopConfirm',
      params: { number: this.label(stop) },
      confirmKey: 'routes.detail.removeStop',
      destructive: true,
    });
    if (confirmed) {
      await this.store.removeStop(stop.shipmentId);
    }
  }

  protected async start(): Promise<void> {
    const confirmed = await this.confirm.confirm({
      titleKey: 'routes.detail.startTitle',
      messageKey: 'routes.detail.startConfirm',
      params: { count: this.store.progress().total },
      confirmKey: 'routes.detail.start',
    });
    if (confirmed) {
      await this.store.start();
    }
  }

  protected open(kind: StopDialog['kind'], stop: StopView): void {
    this.dialog.set({ kind, stop });
  }

  protected closeDialog(): void {
    this.dialog.set(null);
  }
}
