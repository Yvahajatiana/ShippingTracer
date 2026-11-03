import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { aPage, anInvoice, flushProblem, settle } from '../../testing/fixtures';
import { InvoiceDetailPage } from './invoice-detail.page';

const BASE = '/api/v1/billing/invoices/invoice-1';
const UUID = /^[0-9a-f-]{36}$/;

describe('InvoiceDetailPage payment flow', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideParcelFlowTesting({ subject: 'customer-alice' }), provideRouter([])],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    backend.verify();
    document.body.querySelectorAll('.cdk-overlay-container').forEach((node) => node.remove());
  });

  async function renderPendingInvoice() {
    const fixture = TestBed.createComponent(InvoiceDetailPage);
    fixture.componentRef.setInput('id', 'invoice-1');
    await fixture.whenStable();
    await settle();
    backend.expectOne(BASE).flush(anInvoice());
    flushProblem(backend.expectOne(`${BASE}/payment`), 404);
    backend.expectOne((req) => req.url === `${BASE}/credit-notes`).flush(aPage([]));
    await fixture.whenStable();
    return fixture;
  }

  function buttonByText(root: ParentNode, text: string): HTMLButtonElement {
    const button = [...root.querySelectorAll('button')].find((item) =>
      item.textContent?.includes(text),
    );
    if (!button) {
      throw new Error(`No button "${text}"`);
    }
    return button;
  }

  async function openDialog(fixture: Awaited<ReturnType<typeof renderPendingInvoice>>) {
    buttonByText(fixture.nativeElement, 'payment.payNow').click();
    await fixture.whenStable();
    return buttonByText(document.body, 'payment.confirmApproved');
  }

  it('shows the pending payment call to action and no payment yet', async () => {
    const fixture = await renderPendingInvoice();
    const text = (fixture.nativeElement as HTMLElement).textContent;
    expect(text).toContain('payment.pendingTitle');
    expect(text).toContain('payment.none');
  });

  it('reuses the operation id when retrying an attempt and renews it per dialog', async () => {
    const fixture = await renderPendingInvoice();

    let confirm = await openDialog(fixture);
    confirm.click();
    await settle();
    const first = backend.expectOne(`${BASE}/payments`);
    const operationId = first.request.body.operationId as string;
    expect(operationId).toMatch(UUID);
    expect(first.request.body.outcome).toBe('Approved');
    first.error(new ProgressEvent('error'));
    await settle();
    await fixture.whenStable();

    // Retry inside the same dialog: same idempotency key.
    buttonByText(document.body, 'payment.confirmApproved').click();
    await settle();
    const retry = backend.expectOne(`${BASE}/payments`);
    expect(retry.request.body.operationId).toBe(operationId);
    retry.error(new ProgressEvent('error'));
    await settle();
    await fixture.whenStable();

    // Close and reopen: a new attempt gets a new key.
    buttonByText(document.body, 'common.cancel').click();
    await fixture.whenStable();
    confirm = await openDialog(fixture);
    confirm.click();
    await settle();
    const next = backend.expectOne(`${BASE}/payments`);
    expect(next.request.body.operationId).toMatch(UUID);
    expect(next.request.body.operationId).not.toBe(operationId);
    next.flush({
      operationId: next.request.body.operationId,
      invoiceId: 'invoice-1',
      outcome: 'Approved',
      amount: 16.1,
      currency: 'EUR',
      occurredAtUtc: '2026-10-22T09:00:00Z',
    });
    await settle();
    backend.expectOne(BASE).flush(anInvoice({ status: 'Paid' }));
    await settle();
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent;
    expect(text).not.toContain('payment.pendingTitle');
    expect(text).toContain('payment.reference');
  });
});
