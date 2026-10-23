import { setupWorker } from 'msw/browser';
import { createHandlers } from './handlers';

/** Starts the in-browser mock API (development `mock` configuration only). */
export async function startMockApi(): Promise<void> {
  const worker = setupWorker(...createHandlers());
  await worker.start({
    // Resolve against <base href> so deep links (e.g. /billing/invoices/:id) find the worker.
    serviceWorker: { url: new URL('mockServiceWorker.js', document.baseURI).pathname },
    onUnhandledFrame: 'bypass',
    quiet: true,
  });
}
