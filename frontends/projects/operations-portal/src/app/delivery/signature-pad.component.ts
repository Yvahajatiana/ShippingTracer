import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterNextRender,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEraser } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';

/** Logical canvas height in CSS pixels. */
const PAD_HEIGHT = 160;

/** Strips the `data:image/png;base64,` prefix of a canvas export. */
export function dataUrlToBase64(dataUrl: string): string {
  return dataUrl.slice(dataUrl.indexOf(',') + 1);
}

/**
 * Handwritten signature captured with pointer events (finger, stylus or mouse).
 * Emits the drawing as a base64 PNG after each stroke, or null once cleared.
 */
@Component({
  selector: 'pf-signature-pad',
  imports: [HlmButtonImports, NgIcon, TranslocoPipe],
  providers: [provideIcons({ lucideEraser })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    <div class="relative">
      <canvas
        #canvas
        class="block h-40 w-full cursor-crosshair touch-none rounded-md border bg-background"
        role="img"
        data-testid="signature-canvas"
        [attr.aria-label]="label()"
        [attr.aria-describedby]="describedBy() || null"
        (pointerdown)="start($event)"
        (pointermove)="move($event)"
        (pointerup)="end($event)"
        (pointercancel)="end($event)"
        (pointerleave)="end($event)"
      ></canvas>
      @if (empty()) {
        <span
          class="pointer-events-none absolute inset-0 flex items-center justify-center text-sm text-muted-foreground"
          aria-hidden="true"
        >
          {{ 'routes.delivery.signHere' | transloco }}
        </span>
      }
    </div>
    <div class="mt-2 flex justify-end">
      <button
        hlmBtn
        variant="ghost"
        size="sm"
        type="button"
        [disabled]="empty() || disabled()"
        (click)="clear()"
      >
        <ng-icon name="lucideEraser" />
        {{ 'routes.delivery.clearSignature' | transloco }}
      </button>
    </div>
  `,
})
export class SignaturePadComponent {
  readonly label = input.required<string>();
  readonly describedBy = input<string>('');
  readonly disabled = input(false);
  /** Base64 PNG (without data URL prefix) or null when empty. */
  readonly signatureChange = output<string | null>();

  protected readonly empty = signal(true);
  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private context: CanvasRenderingContext2D | null = null;
  private drawing = false;
  private last: { x: number; y: number } | null = null;

  constructor() {
    afterNextRender(() => this.setup());
  }

  protected start(event: PointerEvent): void {
    if (this.disabled() || !this.context) {
      return;
    }
    event.preventDefault();
    this.canvas().nativeElement.setPointerCapture?.(event.pointerId);
    this.drawing = true;
    this.last = this.point(event);
    // A tap draws a dot.
    this.line(this.last, this.last);
  }

  protected move(event: PointerEvent): void {
    if (!this.drawing || !this.last) {
      return;
    }
    event.preventDefault();
    const next = this.point(event);
    this.line(this.last, next);
    this.last = next;
  }

  protected end(event: PointerEvent): void {
    if (!this.drawing) {
      return;
    }
    this.drawing = false;
    this.last = null;
    this.canvas().nativeElement.releasePointerCapture?.(event.pointerId);
    this.empty.set(false);
    this.signatureChange.emit(dataUrlToBase64(this.canvas().nativeElement.toDataURL('image/png')));
  }

  clear(): void {
    const canvas = this.canvas().nativeElement;
    this.context?.clearRect(0, 0, canvas.width, canvas.height);
    this.empty.set(true);
    this.signatureChange.emit(null);
  }

  private setup(): void {
    const canvas = this.canvas().nativeElement;
    const ratio = window.devicePixelRatio || 1;
    const width = canvas.clientWidth || 320;
    canvas.width = Math.round(width * ratio);
    canvas.height = Math.round(PAD_HEIGHT * ratio);
    this.context = canvas.getContext('2d');
    if (this.context) {
      this.context.scale(ratio, ratio);
      this.context.lineWidth = 2.5;
      this.context.lineCap = 'round';
      this.context.lineJoin = 'round';
      this.context.strokeStyle = '#111827';
    }
  }

  private point(event: PointerEvent): { x: number; y: number } {
    // Maps CSS pixels to the canvas logical space, whatever the rendered size.
    const canvas = this.canvas().nativeElement;
    const rect = canvas.getBoundingClientRect();
    const ratio = window.devicePixelRatio || 1;
    const width = canvas.width / ratio;
    const height = canvas.height / ratio;
    return {
      x: rect.width ? ((event.clientX - rect.left) * width) / rect.width : 0,
      y: rect.height ? ((event.clientY - rect.top) * height) / rect.height : 0,
    };
  }

  private line(from: { x: number; y: number }, to: { x: number; y: number }): void {
    if (!this.context) {
      return;
    }
    this.context.beginPath();
    this.context.moveTo(from.x, from.y);
    this.context.lineTo(to.x + 0.01, to.y + 0.01);
    this.context.stroke();
  }
}
