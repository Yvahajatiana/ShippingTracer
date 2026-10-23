import {
  ChangeDetectionStrategy,
  Component,
  Injectable,
  computed,
  inject,
  signal,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';

export interface ConfirmOptions {
  titleKey?: string;
  messageKey: string;
  params?: Record<string, unknown>;
  confirmKey?: string;
  /** Styles the confirm button as a destructive action. */
  destructive?: boolean;
}

interface PendingConfirmation extends ConfirmOptions {
  resolve: (confirmed: boolean) => void;
}

/** Promise-based confirmation dialog, rendered once by the shell. */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private readonly pending = signal<PendingConfirmation | null>(null);
  readonly current = this.pending.asReadonly();

  confirm(options: ConfirmOptions): Promise<boolean> {
    // A new request supersedes an unanswered one.
    this.pending()?.resolve(false);
    return new Promise((resolve) => this.pending.set({ ...options, resolve }));
  }

  answer(confirmed: boolean): void {
    const pending = this.pending();
    if (pending) {
      this.pending.set(null);
      pending.resolve(confirmed);
    }
  }
}

@Component({
  selector: 'pf-confirm-dialog',
  imports: [HlmAlertDialogImports, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <hlm-alert-dialog [state]="state()" (closed)="confirm.answer(false)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal>
        @if (confirm.current(); as request) {
          <hlm-alert-dialog-header>
            <h2 hlmAlertDialogTitle>{{ request.titleKey ?? 'common.confirmTitle' | transloco }}</h2>
            <p hlmAlertDialogDescription>
              {{ request.messageKey | transloco: request.params ?? {} }}
            </p>
          </hlm-alert-dialog-header>
          <hlm-alert-dialog-footer>
            <button hlmAlertDialogCancel>{{ 'common.cancel' | transloco }}</button>
            <button
              hlmAlertDialogAction
              [variant]="request.destructive ? 'destructive' : 'default'"
              (click)="confirm.answer(true)"
            >
              {{ request.confirmKey ?? 'common.confirm' | transloco }}
            </button>
          </hlm-alert-dialog-footer>
        }
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class ConfirmDialogComponent {
  protected readonly confirm = inject(ConfirmService);
  protected readonly state = computed(() => (this.confirm.current() ? 'open' : 'closed'));
}
