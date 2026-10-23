import { setupWorker } from 'msw/browser';
import { createHandlers } from './handlers';

/** Starts the in-browser mock API (development `mock` configuration only). */
export async function startMockApi(): Promise<void> {
  const worker = setupWorker(...createHandlers());
  await worker.start({
    serviceWorker: { url: 'mockServiceWorker.js' },
    onUnhandledFrame: 'bypass',
    quiet: true,
  });
}
