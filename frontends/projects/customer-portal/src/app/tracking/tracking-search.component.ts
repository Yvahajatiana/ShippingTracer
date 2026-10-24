import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSearch } from '@ng-icons/lucide';
import { FieldErrorComponent, trimmedRequired } from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';

/** Same rule as the Tracking service: 6 to 32 letters or digits. */
export const TRACKING_NUMBER_PATTERN = /^\s*[A-Za-z0-9]{6,32}\s*$/;

/** Tracking number search box that navigates to the public tracking page. */
@Component({
  selector: 'pf-tracking-search',
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    NgIcon,
    HlmButtonImports,
    HlmInputImports,
    HlmLabelImports,
    FieldErrorComponent,
  ],
  providers: [provideIcons({ lucideSearch })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form
      class="flex flex-col gap-2"
      role="search"
      [formGroup]="form"
      (ngSubmit)="search()"
      novalidate
    >
      <label hlmLabel [for]="inputId()" [class.sr-only]="hideLabel()">
        {{ 'tracking.search.label' | transloco }}
      </label>
      <div class="flex flex-col gap-2 sm:flex-row">
        <input
          hlmInput
          [id]="inputId()"
          class="h-10 uppercase placeholder:normal-case"
          formControlName="trackingNumber"
          autocomplete="off"
          autocapitalize="characters"
          spellcheck="false"
          [attr.aria-describedby]="inputId() + '-hint'"
          [placeholder]="'tracking.search.placeholder' | transloco"
        />
        <button hlmBtn size="lg" type="submit">
          <ng-icon name="lucideSearch" />
          {{ 'tracking.search.submit' | transloco }}
        </button>
      </div>
      <p class="text-xs text-muted-foreground" [id]="inputId() + '-hint'">
        {{ 'tracking.search.hint' | transloco }}
      </p>
      <pf-field-error [control]="form.controls.trackingNumber" />
    </form>
  `,
})
export class TrackingSearchComponent {
  private readonly router = inject(Router);
  readonly inputId = input('pf-tracking-number');
  readonly hideLabel = input(false);

  protected readonly form = inject(NonNullableFormBuilder).group({
    trackingNumber: ['', [trimmedRequired, Validators.pattern(TRACKING_NUMBER_PATTERN)]],
  });

  protected search(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.controls.trackingNumber.value.trim().toUpperCase();
    void this.router.navigate(['/track', value]);
  }
}
