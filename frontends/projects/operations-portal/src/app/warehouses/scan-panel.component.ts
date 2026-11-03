import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterNextRender,
  inject,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCircleCheck,
  lucideCircleX,
  lucideLogIn,
  lucideLogOut,
  lucideRotateCw,
  lucideScanBarcode,
} from '@ng-icons/lucide';
import {
  FieldErrorComponent,
  PfDatePipe,
  ScanDirection,
  StatusTagComponent,
} from '@parcelflow/shared';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import { HlmToggleGroupImports } from '@spartan-ng/helm/toggle-group';
import { TRACKING_NUMBER_PATTERN, WarehouseDetailStore } from './warehouses.store';

/**
 * Barcode scan panel. The input keeps focus and submits on Enter, which is what
 * keyboard-wedge barcode scanners send after each code.
 */
@Component({
  selector: 'pf-scan-panel',
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    NgIcon,
    HlmButtonImports,
    HlmCardImports,
    HlmInputImports,
    HlmLabelImports,
    HlmSpinnerImports,
    HlmToggleGroupImports,
    FieldErrorComponent,
    PfDatePipe,
    StatusTagComponent,
  ],
  providers: [
    provideIcons({
      lucideCircleCheck,
      lucideCircleX,
      lucideLogIn,
      lucideLogOut,
      lucideRotateCw,
      lucideScanBarcode,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section hlmCard data-testid="scan-panel">
      <div hlmCardHeader>
        <h2 hlmCardTitle class="flex items-center gap-2">
          <ng-icon name="lucideScanBarcode" aria-hidden="true" />
          {{ 'warehouses.scan.title' | transloco }}
        </h2>
        <p hlmCardDescription>{{ 'warehouses.scan.description' | transloco }}</p>
      </div>
      <div hlmCardContent class="space-y-4">
        @if (!store.canScan()) {
          <p class="rounded-md bg-warning/15 p-3 text-sm text-warning" role="status">
            {{ 'warehouses.scan.inactive' | transloco }}
          </p>
        }
        <div class="grid gap-2">
          <span hlmLabel id="scan-direction-label">{{
            'warehouses.scan.direction' | transloco
          }}</span>
          <div
            hlmToggleGroup
            type="single"
            variant="outline"
            class="w-full sm:w-fit"
            role="group"
            aria-labelledby="scan-direction-label"
            [value]="store.direction()"
            [disabled]="!store.canScan()"
            (valueChange)="setDirection($event)"
          >
            <button
              hlmToggleGroupItem
              value="Entry"
              class="flex-1 sm:flex-none"
              data-testid="scan-entry"
            >
              <ng-icon name="lucideLogIn" aria-hidden="true" />
              {{ 'enum.ScanDirection.Entry' | transloco }}
            </button>
            <button
              hlmToggleGroupItem
              value="Exit"
              class="flex-1 sm:flex-none"
              data-testid="scan-exit"
            >
              <ng-icon name="lucideLogOut" aria-hidden="true" />
              {{ 'enum.ScanDirection.Exit' | transloco }}
            </button>
          </div>
        </div>

        <form class="grid gap-2" [formGroup]="form" (ngSubmit)="submit()">
          <label hlmLabel for="scan-tracking-number">{{
            'warehouses.scan.trackingNumber' | transloco
          }}</label>
          <div class="flex gap-2">
            <input
              #trackingInput
              hlmInput
              id="scan-tracking-number"
              class="min-w-0 flex-1 font-mono uppercase placeholder:font-sans placeholder:normal-case"
              autocomplete="off"
              autocapitalize="characters"
              spellcheck="false"
              enterkeyhint="send"
              formControlName="trackingNumber"
              [placeholder]="'warehouses.scan.placeholder' | transloco"
              aria-describedby="scan-hint"
            />
            <button hlmBtn type="submit" [disabled]="!store.canScan()">
              {{ 'warehouses.scan.submit' | transloco }}
            </button>
          </div>
          <pf-field-error [control]="form.controls.trackingNumber" />
          <p id="scan-hint" class="text-xs text-muted-foreground">
            {{ 'warehouses.scan.hint' | transloco }}
          </p>
        </form>

        <div>
          <div class="mb-2 flex items-center justify-between gap-2">
            <h3 class="text-sm font-medium">{{ 'warehouses.scan.recent' | transloco }}</h3>
            @if (store.scans().length > 0) {
              <button hlmBtn variant="ghost" size="sm" type="button" (click)="store.clearScans()">
                {{ 'warehouses.scan.clear' | transloco }}
              </button>
            }
          </div>
          @if (store.scans().length === 0) {
            <p class="text-sm text-muted-foreground">{{ 'warehouses.scan.none' | transloco }}</p>
          } @else {
            <ul class="divide-y rounded-md border" aria-live="polite" data-testid="scan-history">
              @for (scan of store.scans(); track scan.scanId) {
                <li class="flex items-start gap-3 p-3 text-sm" [attr.data-outcome]="scan.outcome">
                  @switch (scan.outcome) {
                    @case ('pending') {
                      <hlm-spinner class="mt-0.5" [aria-label]="'common.loading' | transloco" />
                    }
                    @case ('success') {
                      <ng-icon
                        name="lucideCircleCheck"
                        class="mt-0.5 text-success"
                        [attr.aria-label]="'warehouses.scan.success' | transloco"
                      />
                    }
                    @default {
                      <ng-icon
                        name="lucideCircleX"
                        class="mt-0.5 text-destructive"
                        [attr.aria-label]="'warehouses.scan.failure' | transloco"
                      />
                    }
                  }
                  <div class="min-w-0 flex-1">
                    <div class="flex flex-wrap items-center gap-2">
                      <span class="font-mono font-medium break-all">{{ scan.trackingNumber }}</span>
                      <pf-status-tag type="ScanDirection" [value]="scan.direction" />
                    </div>
                    @if (scan.errorKey) {
                      <p class="text-destructive">{{ scan.errorKey | transloco }}</p>
                    }
                    <p class="text-xs text-muted-foreground">{{ scan.at | pfDate: 'time' }}</p>
                  </div>
                  @if (scan.retryable) {
                    <button
                      hlmBtn
                      variant="ghost"
                      size="icon-sm"
                      type="button"
                      [attr.aria-label]="'warehouses.scan.retry' | transloco"
                      (click)="store.retryScan(scan.scanId)"
                    >
                      <ng-icon name="lucideRotateCw" />
                    </button>
                  }
                </li>
              }
            </ul>
          }
        </div>
      </div>
    </section>
  `,
})
export class ScanPanelComponent {
  protected readonly store = inject(WarehouseDetailStore);
  private readonly input = viewChild.required<ElementRef<HTMLInputElement>>('trackingInput');

  protected readonly form = inject(NonNullableFormBuilder).group({
    trackingNumber: ['', [Validators.required, Validators.pattern(TRACKING_NUMBER_PATTERN)]],
  });

  constructor() {
    afterNextRender(() => this.focus());
    // Scanners and phone keyboards may send lowercase: normalize as the operator types.
    this.form.controls.trackingNumber.valueChanges.pipe(takeUntilDestroyed()).subscribe((value) => {
      const normalized = value.toUpperCase().replace(/\s+/g, '');
      if (normalized !== value) {
        this.form.controls.trackingNumber.setValue(normalized, { emitEvent: false });
      }
    });
  }

  protected setDirection(value: unknown): void {
    if (value === 'Entry' || value === 'Exit') {
      this.store.setDirection(value as ScanDirection);
    }
    this.focus();
  }

  protected submit(): void {
    const control = this.form.controls.trackingNumber;
    if (!this.store.canScan()) {
      return;
    }
    if (control.invalid) {
      control.markAsTouched();
      this.focus();
      return;
    }
    // Fire and forget: the next code can be scanned while this one is sent.
    void this.store.scan(control.value);
    this.form.reset();
    this.focus();
  }

  private focus(): void {
    this.input().nativeElement.focus();
  }
}
