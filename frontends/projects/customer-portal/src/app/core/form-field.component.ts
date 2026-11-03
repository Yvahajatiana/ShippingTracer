import { ChangeDetectionStrategy, Component, booleanAttribute, input } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { FieldErrorComponent } from '@parcelflow/shared';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { UppercaseDirective } from './forms';

/** Labelled text input bound to a reactive control, with its translated validation error. */
@Component({
  selector: 'pf-form-field',
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    HlmInputImports,
    HlmLabelImports,
    FieldErrorComponent,
    UppercaseDirective,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex min-w-0 flex-col gap-1.5' },
  template: `
    <label hlmLabel [for]="fieldId()">
      {{ labelKey() | transloco }}
      @if (!required()) {
        <span class="font-normal text-muted-foreground">{{ 'form.optional' | transloco }}</span>
      }
    </label>
    @if (uppercase()) {
      <input
        hlmInput
        pfUppercase
        [id]="fieldId()"
        [type]="type()"
        [formControl]="control()"
        [attr.autocomplete]="autocomplete()"
        [attr.maxlength]="maxLength()"
        [attr.aria-required]="required()"
        class="uppercase"
      />
    } @else {
      <input
        hlmInput
        [id]="fieldId()"
        [type]="type()"
        [formControl]="control()"
        [attr.autocomplete]="autocomplete()"
        [attr.inputmode]="inputMode()"
        [attr.step]="step()"
        [attr.min]="min()"
        [attr.max]="max()"
        [attr.aria-required]="required()"
      />
    }
    <pf-field-error [control]="control()" />
  `,
})
export class FormFieldComponent {
  readonly control = input.required<FormControl<string> | FormControl<number>>();
  readonly fieldId = input.required<string>();
  readonly labelKey = input.required<string>();
  readonly type = input<'text' | 'email' | 'tel' | 'number'>('text');
  readonly autocomplete = input<string | null>(null);
  readonly required = input(true, { transform: booleanAttribute });
  readonly uppercase = input(false, { transform: booleanAttribute });
  readonly maxLength = input<number | null>(null);
  readonly inputMode = input<string | null>(null);
  readonly step = input<number | string | null>(null);
  readonly min = input<number | null>(null);
  readonly max = input<number | null>(null);
}
