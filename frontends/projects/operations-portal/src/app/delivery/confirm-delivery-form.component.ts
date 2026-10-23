import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import {
  AbstractControl,
  FormControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCamera, lucideTrash2 } from '@ng-icons/lucide';
import {
  FieldErrorComponent,
  Guid,
  Notifier,
  PfNumberPipe,
  applyServerErrors,
  newOperationId,
  trimmedRequired,
} from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { RouteDetailStore } from './route-detail.store';
import { SignaturePadComponent } from './signature-pad.component';

/** Maximum photo size accepted by the delivery service. */
export const MAX_PHOTO_BYTES = 10 * 1024 * 1024;

/** A delivery needs a signature, a photo, or both. */
function proofRequired(group: AbstractControl): ValidationErrors | null {
  const { signature, photo } = group.value as { signature: string | null; photo: Blob | null };
  return signature || photo ? null : { proofRequired: true };
}

/**
 * Delivery confirmation form shown in a dialog. One operationId is generated per dialog
 * instance and reused on every retry so a duplicate submission is recognized by the server.
 */
@Component({
  selector: 'pf-confirm-delivery-form',
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    NgIcon,
    HlmButtonImports,
    HlmDialogImports,
    HlmInputImports,
    HlmLabelImports,
    HlmSpinnerImports,
    FieldErrorComponent,
    PfNumberPipe,
    SignaturePadComponent,
  ],
  providers: [provideIcons({ lucideCamera, lucideTrash2 })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="grid gap-5" [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <div hlmDialogHeader>
        <h2 hlmDialogTitle>{{ 'routes.delivery.confirmTitle' | transloco }}</h2>
        <p hlmDialogDescription>
          {{ 'routes.delivery.confirmDescription' | transloco: { number: trackingNumber() } }}
        </p>
      </div>

      <div class="grid gap-2">
        <label hlmLabel for="delivery-recipient">{{
          'routes.delivery.recipientName' | transloco
        }}</label>
        <input
          hlmInput
          id="delivery-recipient"
          autocomplete="off"
          formControlName="recipientName"
          [placeholder]="defaultRecipient() ?? ''"
        />
        <pf-field-error [control]="form.controls.recipientName" />
      </div>

      <fieldset class="grid gap-2">
        <legend class="mb-2 text-sm font-medium">{{ 'routes.delivery.proof' | transloco }}</legend>
        <p id="proof-hint" class="text-xs text-muted-foreground">
          {{ 'routes.delivery.proofHint' | transloco }}
        </p>
        <span hlmLabel class="mt-2">{{ 'routes.delivery.signature' | transloco }}</span>
        <pf-signature-pad
          describedBy="proof-hint"
          [label]="'routes.delivery.signatureArea' | transloco"
          [disabled]="pending()"
          (signatureChange)="setSignature($event)"
        />

        <span hlmLabel class="mt-2">{{ 'routes.delivery.photo' | transloco }}</span>
        @if (photoPreview(); as preview) {
          <div class="flex items-center gap-3">
            <img
              class="size-20 rounded-md border object-cover"
              [src]="preview"
              [alt]="'routes.delivery.photoPreview' | transloco"
            />
            <div class="min-w-0 flex-1 text-sm">
              <p class="truncate">{{ photoName() }}</p>
              <p class="text-xs text-muted-foreground">
                @if (photoSizeMb() >= 1) {
                  {{
                    'routes.delivery.photoSize' | transloco: { size: (photoSizeMb() | pfNumber: 1) }
                  }}
                } @else {
                  {{
                    'routes.delivery.photoSizeKb'
                      | transloco: { size: (photoSizeMb() * 1024 | pfNumber: 0) }
                  }}
                }
              </p>
            </div>
            <button
              hlmBtn
              variant="ghost"
              size="icon-sm"
              type="button"
              [attr.aria-label]="'routes.delivery.removePhoto' | transloco"
              (click)="clearPhoto(fileInput)"
            >
              <ng-icon name="lucideTrash2" />
            </button>
          </div>
        }
        <button
          hlmBtn
          variant="outline"
          type="button"
          class="w-full sm:w-fit"
          [disabled]="pending()"
          (click)="fileInput.click()"
        >
          <ng-icon name="lucideCamera" />
          {{
            (photoPreview() ? 'routes.delivery.replacePhoto' : 'routes.delivery.takePhoto')
              | transloco
          }}
        </button>
        <input
          #fileInput
          id="delivery-photo"
          class="sr-only"
          tabindex="-1"
          type="file"
          accept="image/*"
          capture="environment"
          data-testid="photo-input"
          aria-describedby="proof-hint"
          (change)="pickPhoto(fileInput)"
        />
        @if (photoError(); as photoErrorKey) {
          <small class="text-sm text-destructive" role="alert">{{
            photoErrorKey | transloco: { max: 10 }
          }}</small>
        }
        @if (form.hasError('proofRequired') && form.touched) {
          <small class="text-sm text-destructive" role="alert">{{
            'routes.delivery.proofRequired' | transloco
          }}</small>
        }
        <pf-field-error [control]="form.controls.proof" />
      </fieldset>

      <div hlmDialogFooter>
        <button hlmBtn variant="outline" type="button" (click)="cancelled.emit()">
          {{ 'common.cancel' | transloco }}
        </button>
        <button hlmBtn type="submit" [disabled]="pending()" data-testid="confirm-delivery-submit">
          @if (pending()) {
            <hlm-spinner [aria-label]="'common.loading' | transloco" />
          }
          {{ 'routes.delivery.confirmAction' | transloco }}
        </button>
      </div>
    </form>
  `,
})
export class ConfirmDeliveryFormComponent {
  readonly shipmentId = input.required<Guid>();
  readonly trackingNumber = input.required<string>();
  readonly defaultRecipient = input<string | null>(null);
  readonly completed = output<void>();
  readonly cancelled = output<void>();

  private readonly store = inject(RouteDetailStore);
  private readonly notifier = inject(Notifier);

  /** Idempotency key of this delivery attempt, kept across retries. */
  readonly operationId = newOperationId();

  protected readonly form = inject(NonNullableFormBuilder).group(
    {
      recipientName: ['', [trimmedRequired, Validators.maxLength(200)]],
      signature: new FormControl<string | null>(null),
      photo: new FormControl<Blob | null>(null),
      // Carries the server-side `Proof` error, if any.
      proof: new FormControl<null>(null),
    },
    { validators: proofRequired },
  );

  protected readonly pending = computed(() => this.store.busy() === 'confirm');
  protected readonly photoPreview = signal<string | null>(null);
  protected readonly photoName = signal('');
  protected readonly photoSizeMb = signal(0);
  protected readonly photoError = signal<string | null>(null);

  constructor() {
    inject(DestroyRef).onDestroy(() => this.revokePreview());
  }

  protected setSignature(signature: string | null): void {
    this.form.controls.signature.setValue(signature);
    this.form.controls.proof.setErrors(null);
  }

  protected pickPhoto(fileInput: HTMLInputElement): void {
    const file = fileInput.files?.[0] ?? null;
    this.photoError.set(null);
    if (!file) {
      return;
    }
    if (!file.type.startsWith('image/')) {
      this.photoError.set('routes.delivery.photoType');
      fileInput.value = '';
      return;
    }
    if (file.size > MAX_PHOTO_BYTES) {
      this.photoError.set('routes.delivery.photoTooLarge');
      fileInput.value = '';
      return;
    }
    this.revokePreview();
    this.photoPreview.set(URL.createObjectURL(file));
    this.photoName.set(file.name);
    this.photoSizeMb.set(file.size / (1024 * 1024));
    this.form.controls.photo.setValue(file);
    this.form.controls.proof.setErrors(null);
  }

  protected clearPhoto(fileInput: HTMLInputElement): void {
    fileInput.value = '';
    this.revokePreview();
    this.photoName.set('');
    this.form.controls.photo.setValue(null);
  }

  protected async submit(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.pending()) {
      return;
    }
    const { recipientName, signature, photo } = this.form.getRawValue();
    const result = await this.store.confirmDelivery(this.shipmentId(), {
      operationId: this.operationId,
      recipientName,
      signaturePng: signature,
      photo,
    });
    if (result.ok) {
      this.completed.emit();
    } else if (!applyServerErrors(this.form, result.error)) {
      this.notifier.error(result.error);
    }
  }

  private revokePreview(): void {
    const preview = this.photoPreview();
    if (preview) {
      URL.revokeObjectURL(preview);
      this.photoPreview.set(null);
    }
  }
}
