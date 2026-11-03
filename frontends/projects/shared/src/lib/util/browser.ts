import { DownloadedFile } from '../api/clients';

/** Idempotency key for mutations that accept an OperationId or ScanId. */
export function newOperationId(): string {
  return crypto.randomUUID();
}

/** Saves a downloaded blob through a temporary object URL. */
export function saveFile(file: DownloadedFile): void {
  const url = URL.createObjectURL(file.blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = file.fileName;
  anchor.rel = 'noopener';
  anchor.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/** Reads a File or Blob as base64 without the `data:` prefix, as expected by byte[] JSON fields. */
export function blobToBase64(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const result = String(reader.result);
      resolve(result.slice(result.indexOf(',') + 1));
    };
    reader.onerror = () => reject(reader.error ?? new Error('Unable to read file.'));
    reader.readAsDataURL(blob);
  });
}

/** Formats a Date as the `YYYY-MM-DD` DateOnly wire format, using the local calendar day. */
export function toIsoDate(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}
