import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  DELIVERY_FAILURE_REASONS,
  DeliveryFailureReason,
  FieldErrorComponent,
  Guid,
  Notifier,
  applyServerErrors,
  newOperationId,
} from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { RouteDetailStore } from './route-detail.store';

/** Failed delivery report; one operationId per dialog, reused on retries. */
@Component({
  selector: 'pf-fail-delivery-form',
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    HlmButtonImports,
    HlmDialogImports,
    HlmLabelImports,
    HlmNativeSelectImports,
    HlmSpinnerImports,
    FieldErrorComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="grid gap-5" [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <div hlmDialogHeader>
        <h2 hlmDialogTitle>{{ 'routes.delivery.failTitle' | transloco }}</h2>
        <p hlmDialogDescription>
          {{ 'routes.delivery.failDescription' | transloco: { number: trackingNumber() } }}
        </p>
      </div>
      <div class="grid gap-2">
        <label hlmLabel for="failure-reason">{{ 'routes.delivery.reason' | transloco }}</label>
        <hlm-native-select class="w-full" selectId="failure-reason" formControlName="reason">
          <option value="" disabled>{{ 'routes.delivery.chooseReason' | transloco }}</option>
          @for (reason of reasons; track reason) {
            <option [value]="reason">
              {{ 'enum.DeliveryFailureReason.' + reason | transloco }}
            </option>
          }
        </hlm-native-select>
        <pf-field-error [control]="form.controls.reason" />
      </div>
      <div hlmDialogFooter>
        <button hlmBtn variant="outline" type="button" (click)="cancelled.emit()">
          {{ 'common.cancel' | transloco }}
        </button>
        <button
          hlmBtn
          variant="destructive"
          type="submit"
          [disabled]="pending()"
          data-testid="fail-delivery-submit"
        >
          @if (pending()) {
            <hlm-spinner [aria-label]="'common.loading' | transloco" />
          }
          {{ 'routes.delivery.failAction' | transloco }}
        </button>
      </div>
    </form>
  `,
})
export class FailDeliveryFormComponent {
  readonly shipmentId = input.required<Guid>();
  readonly trackingNumber = input.required<string>();
  readonly completed = output<void>();
  readonly cancelled = output<void>();

  private readonly store = inject(RouteDetailStore);
  private readonly notifier = inject(Notifier);

  /** Idempotency key of this failure report, kept across retries. */
  readonly operationId = newOperationId();
  protected readonly reasons = DELIVERY_FAILURE_REASONS;
  protected readonly pending = computed(() => this.store.busy() === 'fail');

  protected readonly form = inject(NonNullableFormBuilder).group({
    reason: ['' as DeliveryFailureReason | '', Validators.required],
  });

  protected async submit(): Promise<void> {
    this.form.markAllAsTouched();
    const reason = this.form.controls.reason.value;
    if (this.form.invalid || !reason || this.pending()) {
      return;
    }
    const result = await this.store.failDelivery(this.shipmentId(), this.operationId, reason);
    if (result.ok) {
      this.completed.emit();
    } else if (!applyServerErrors(this.form, result.error)) {
      this.notifier.error(result.error);
    }
  }
}
