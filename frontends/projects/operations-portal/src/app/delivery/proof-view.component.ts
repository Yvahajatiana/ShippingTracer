import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  input,
  untracked,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { ErrorBannerComponent, Guid, PfDatePipe, StatusTagComponent } from '@parcelflow/shared';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { ProofStore } from './proof.store';

/** Read-only delivery proof (attempt details, signature and photo). */
@Component({
  selector: 'pf-proof-view',
  imports: [
    TranslocoPipe,
    HlmDialogImports,
    HlmSkeletonImports,
    ErrorBannerComponent,
    PfDatePipe,
    StatusTagComponent,
  ],
  providers: [ProofStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="grid gap-4" data-testid="proof-view">
      <div hlmDialogHeader>
        <h2 hlmDialogTitle>{{ 'routes.proof.title' | transloco }}</h2>
        <p hlmDialogDescription>{{ trackingNumber() }}</p>
      </div>
      @if (store.error(); as error) {
        <pf-error-banner [error]="error" (retry)="store.load(routeId(), shipmentId())" />
      } @else if (store.loading()) {
        <hlm-skeleton class="h-6 w-1/2" />
        <hlm-skeleton class="h-32 w-full" />
      } @else if (store.attempt(); as attempt) {
        <dl class="grid grid-cols-2 gap-3 text-sm">
          <div>
            <dt class="text-xs text-muted-foreground">{{ 'routes.proof.outcome' | transloco }}</dt>
            <dd class="mt-1"><pf-status-tag type="DeliveryOutcome" [value]="attempt.outcome" /></dd>
          </div>
          <div>
            <dt class="text-xs text-muted-foreground">{{ 'routes.proof.when' | transloco }}</dt>
            <dd class="mt-1">{{ attempt.occurredAtUtc | pfDate }}</dd>
          </div>
          @if (attempt.recipientName) {
            <div>
              <dt class="text-xs text-muted-foreground">
                {{ 'routes.delivery.recipientName' | transloco }}
              </dt>
              <dd class="mt-1 font-medium">{{ attempt.recipientName }}</dd>
            </div>
          }
          @if (attempt.failureReason) {
            <div>
              <dt class="text-xs text-muted-foreground">
                {{ 'routes.delivery.reason' | transloco }}
              </dt>
              <dd class="mt-1">
                <pf-status-tag type="DeliveryFailureReason" [value]="attempt.failureReason" />
              </dd>
            </div>
          }
          <div>
            <dt class="text-xs text-muted-foreground">{{ 'routes.proof.actor' | transloco }}</dt>
            <dd class="mt-1 font-mono text-xs break-all">{{ attempt.actorSubject }}</dd>
          </div>
        </dl>
        @if (store.signatureUrl(); as signature) {
          <figure>
            <figcaption class="mb-1 text-xs text-muted-foreground">
              {{ 'routes.delivery.signature' | transloco }}
            </figcaption>
            <img
              class="max-h-40 w-full rounded-md border bg-white object-contain"
              data-testid="proof-signature"
              [src]="signature"
              [alt]="'routes.proof.signatureAlt' | transloco"
            />
          </figure>
        }
        @if (store.photoUrl(); as photo) {
          <figure>
            <figcaption class="mb-1 text-xs text-muted-foreground">
              {{ 'routes.delivery.photo' | transloco }}
            </figcaption>
            <img
              class="max-h-64 w-full rounded-md border object-contain"
              data-testid="proof-photo"
              [src]="photo"
              [alt]="'routes.proof.photoAlt' | transloco"
            />
          </figure>
        }
        @if (store.imageError()) {
          <p class="text-sm text-destructive" role="alert">
            {{ 'routes.proof.imageError' | transloco }}
          </p>
        }
        @if (attempt.outcome === 'Delivered' && !attempt.hasSignature && !attempt.hasPhoto) {
          <p class="text-sm text-muted-foreground">{{ 'routes.proof.noImage' | transloco }}</p>
        }
      }
    </div>
  `,
})
export class ProofViewComponent {
  readonly routeId = input.required<Guid>();
  readonly shipmentId = input.required<Guid>();
  readonly trackingNumber = input.required<string>();
  protected readonly store = inject(ProofStore);

  constructor() {
    effect(() => {
      const routeId = this.routeId();
      const shipmentId = this.shipmentId();
      untracked(() => void this.store.load(routeId, shipmentId));
    });
  }
}
