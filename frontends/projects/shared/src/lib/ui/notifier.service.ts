import { Injectable, inject } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { toast } from '@spartan-ng/brain/sonner';
import { toApiError } from '../http/api-error';

type HashMap = Record<string, unknown>;

/** Translated toast notifications (rendered by the shell's toaster); errors are mapped from ApiError keys. */
@Injectable({ providedIn: 'root' })
export class Notifier {
  private readonly transloco = inject(TranslocoService);

  success(key: string, params?: HashMap): void {
    toast.success(this.transloco.translate(key, params));
  }

  info(key: string, params?: HashMap): void {
    toast.info(this.transloco.translate(key, params));
  }

  error(error: unknown): void {
    const { summary, detail } = this.describe(error);
    toast.error(summary, { description: detail, duration: 8000 });
  }

  /** Summary and detail lines for an error, reused by inline error banners. */
  describe(error: unknown): { summary: string; detail?: string } {
    const apiError = toApiError(error);
    const summary = this.transloco.translate(apiError.messageKey);
    const detail = apiError.traceId
      ? this.transloco.translate('errors.traceId', { traceId: apiError.traceId })
      : undefined;
    return { summary, detail };
  }
}
