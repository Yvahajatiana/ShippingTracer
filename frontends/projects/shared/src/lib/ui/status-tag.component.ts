import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';

export type EnumType =
  | 'ParcelStatus'
  | 'ShipmentStatus'
  | 'DeliveryZone'
  | 'WarehouseKind'
  | 'ScanDirection'
  | 'DeliveryRouteStatus'
  | 'DeliveryStopStatus'
  | 'DeliveryOutcome'
  | 'DeliveryFailureReason'
  | 'InvoiceStatus'
  | 'MockPaymentOutcome'
  | 'CreditReason'
  | 'NotificationChannel'
  | 'Role';

export type StatusTone = 'success' | 'info' | 'warning' | 'danger' | 'neutral';

const TONES: Record<string, StatusTone> = {
  Delivered: 'success',
  Paid: 'success',
  Approved: 'success',
  Completed: 'success',
  ReadyForPickup: 'info',
  InTransit: 'info',
  AtSortingCenter: 'info',
  OutForDelivery: 'info',
  InProgress: 'info',
  PickedUp: 'info',
  AwaitingPayment: 'warning',
  PendingPayment: 'warning',
  Pending: 'warning',
  PartiallyCredited: 'warning',
  Returned: 'warning',
  Failed: 'danger',
  PaymentFailed: 'danger',
  Declined: 'danger',
};

const TONE_CLASSES: Record<StatusTone, string> = {
  success: 'bg-success/15 text-success',
  info: 'bg-info/15 text-info',
  warning: 'bg-warning/15 text-warning',
  danger: 'bg-destructive/15 text-destructive',
  neutral: 'bg-secondary text-secondary-foreground',
};

/** Translation key of an enum value, e.g. `enum.ParcelStatus.Delivered`. */
export function enumKey(type: EnumType, value: string): string {
  return `enum.${type}.${value}`;
}

export function statusTone(value: string): StatusTone {
  return TONES[value] ?? 'neutral';
}

/** Translated, color-coded badge for a contract enum value. */
@Component({
  selector: 'pf-status-tag',
  imports: [HlmBadgeImports, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span hlmBadge variant="secondary" [class]="toneClass()">{{
    key() | transloco
  }}</span>`,
})
export class StatusTagComponent {
  readonly type = input.required<EnumType>();
  readonly value = input.required<string>();

  readonly key = computed(() => enumKey(this.type(), this.value()));
  readonly toneClass = computed(() => TONE_CLASSES[statusTone(this.value())]);
}
