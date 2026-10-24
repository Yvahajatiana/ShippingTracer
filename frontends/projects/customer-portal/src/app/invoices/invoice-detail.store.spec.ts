import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { aPage, anInvoice, expectRequest, flushProblem, settle } from '../../testing/fixtures';
import { InvoiceDetailStore } from './invoice-detail.store';

const BASE = '/api/v1/billing/invoices/invoice-1';

describe('InvoiceDetailStore', () => {
  let store: InstanceType<typeof InvoiceDetailStore>;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideParcelFlowTesting({ subject: 'customer-alice' }), InvoiceDetailStore],
    });
    store = TestBed.inject(InvoiceDetailStore);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  async function loadPendingInvoice() {
    store.load('invoice-1');
    await settle();
    backend.expectOne(BASE).flush(anInvoice());
    flushProblem(backend.expectOne(`${BASE}/payment`), 404);
    backend.expectOne((req) => req.url === `${BASE}/credit-notes`).flush(aPage([]));
  }

  it('treats a missing payment as "not paid yet"', async () => {
    await loadPendingInvoice();
    expect(store.error()).toBeNull();
    expect(store.invoice()?.status).toBe('PendingPayment');
    expect(store.payment()).toBeNull();
    expect(store.payable()).toBe(true);
  });

  it('loads payment and credit notes of a paid invoice', async () => {
    store.load('invoice-1');
    await settle();
    backend.expectOne(BASE).flush(anInvoice({ status: 'PartiallyCredited', creditTotal: 5 }));
    backend.expectOne(`${BASE}/payment`).flush({
      operationId: 'op-1',
      invoiceId: 'invoice-1',
      outcome: 'Approved',
      amount: 16.1,
      currency: 'EUR',
      occurredAtUtc: '2026-10-20T09:00:00Z',
    });
    backend
      .expectOne((req) => req.url === `${BASE}/credit-notes`)
      .flush(
        aPage([
          {
            id: 'cn-1',
            number: 'CN-2026-101',
            invoiceId: 'invoice-1',
            amount: 5,
            currency: 'EUR',
            reason: 'ServiceIssue',
            occurredAtUtc: '2026-10-21T09:00:00Z',
          },
        ]),
      );

    expect(store.payment()?.outcome).toBe('Approved');
    expect(store.creditNotes()).toHaveLength(1);
    expect(store.payable()).toBe(false);
  });

  it('fails the page on other payment errors', async () => {
    store.load('invoice-1');
    await settle();
    backend.expectOne(BASE).flush(anInvoice());
    flushProblem(backend.expectOne(`${BASE}/payment`), 503);
    // forkJoin unsubscribes from the remaining request once one fails.
    expect(backend.expectOne((req) => req.url === `${BASE}/credit-notes`).cancelled).toBe(true);

    expect(store.error()?.status).toBe(503);
  });

  it('pays with the given operation id and refreshes the invoice', async () => {
    await loadPendingInvoice();

    const pending = store.pay('op-42', 'Approved');
    expect(store.paying()).toBe(true);
    const request = await expectRequest(backend, `${BASE}/payments`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ operationId: 'op-42', outcome: 'Approved' });
    request.flush({
      operationId: 'op-42',
      invoiceId: 'invoice-1',
      outcome: 'Approved',
      amount: 16.1,
      currency: 'EUR',
      occurredAtUtc: '2026-10-22T09:00:00Z',
    });
    (await expectRequest(backend, BASE)).flush(anInvoice({ status: 'Paid' }));

    const result = await pending;
    expect(result.ok).toBe(true);
    expect(store.paying()).toBe(false);
    expect(store.invoice()?.status).toBe('Paid');
    expect(store.payment()?.operationId).toBe('op-42');
    expect(store.payable()).toBe(false);
  });

  it('keeps the invoice payable when the payment call fails', async () => {
    await loadPendingInvoice();
    const pending = store.pay('op-43', 'Declined');
    // Network failure: the caller may retry with the same operation id.
    (await expectRequest(backend, `${BASE}/payments`)).error(new ProgressEvent('error'));

    const result = await pending;
    expect(result.ok).toBe(false);
    expect(store.paying()).toBe(false);
    expect(store.payable()).toBe(true);
    expect(store.payment()).toBeNull();
  });
});
