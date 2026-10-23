import { ChangeDetectionStrategy, Component, inject, output } from '@angular/core';
import {
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  DeliveryRoute,
  FieldErrorComponent,
  Notifier,
  applyServerErrors,
  toIsoDate,
  trimmedRequired,
} from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { RouteListStore } from './delivery-lists.store';

/** Mirrors the server rule: a service date cannot be in the past. */
export function notBeforeToday(today: () => string = () => toIsoDate(new Date())): ValidatorFn {
  return (control) => {
    const value = control.value as string;
    return value && value < today() ? { pastDate: true } : null;
  };
}

@Component({
  selector: 'pf-create-route-form',
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    HlmButtonImports,
    HlmDialogImports,
    HlmInputImports,
    HlmLabelImports,
    HlmSpinnerImports,
    FieldErrorComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="grid gap-5" [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <div hlmDialogHeader>
        <h2 hlmDialogTitle>{{ 'routes.create.title' | transloco }}</h2>
        <p hlmDialogDescription>{{ 'routes.create.description' | transloco }}</p>
      </div>
      <div class="grid gap-2">
        <label hlmLabel for="route-name">{{ 'routes.name' | transloco }}</label>
        <input hlmInput id="route-name" autocomplete="off" formControlName="name" />
        <pf-field-error [control]="form.controls.name" />
      </div>
      <div class="grid gap-2">
        <label hlmLabel for="route-service-date">{{ 'routes.serviceDate' | transloco }}</label>
        <input
          hlmInput
          id="route-service-date"
          type="date"
          formControlName="serviceDate"
          [min]="today"
        />
        @if (form.controls.serviceDate.hasError('pastDate') && form.controls.serviceDate.touched) {
          <small class="text-sm text-destructive" role="alert">{{
            'routes.create.pastDate' | transloco
          }}</small>
        } @else {
          <pf-field-error [control]="form.controls.serviceDate" />
        }
      </div>
      <div hlmDialogFooter>
        <button hlmBtn variant="outline" type="button" (click)="cancelled.emit()">
          {{ 'common.cancel' | transloco }}
        </button>
        <button
          hlmBtn
          type="submit"
          [disabled]="store.creating()"
          data-testid="create-route-submit"
        >
          @if (store.creating()) {
            <hlm-spinner [aria-label]="'common.loading' | transloco" />
          }
          {{ 'common.create' | transloco }}
        </button>
      </div>
    </form>
  `,
})
export class CreateRouteFormComponent {
  readonly created = output<DeliveryRoute>();
  readonly cancelled = output<void>();

  protected readonly store = inject(RouteListStore);
  private readonly notifier = inject(Notifier);
  protected readonly today = toIsoDate(new Date());

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [trimmedRequired, Validators.maxLength(200)]],
    serviceDate: [this.today, [Validators.required, notBeforeToday()]],
  });

  protected async submit(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.store.creating()) {
      return;
    }
    const { name, serviceDate } = this.form.getRawValue();
    try {
      this.created.emit(await this.store.create(name, serviceDate));
    } catch (error) {
      if (!applyServerErrors(this.form, error)) {
        this.notifier.error(error);
      }
    }
  }
}
