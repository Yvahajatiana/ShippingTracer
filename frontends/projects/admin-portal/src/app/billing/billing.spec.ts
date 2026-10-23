import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Invoice, Notifier } from '@parcelflow/shared';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import {
  creditNote,
  invoice,
  page,
  selectOption,
  settle,
  submitDialog,
  typeInto,
} from '../testing/fixtures';
import { CreditNoteDialogComponent } from './credit-note-dialog.component';
import { InvoiceDetailPage } from './invoice-detail.page';
import { InvoiceDetailStore, canIssueCreditNote } from './invoice-detail.store';
import { InvoicesStore } from './invoices.store';

const INVOICE = '/api/v1/billing/invoices/i1';
const PAYMENT = '/api/v1/billing/invoices/i1/payment';
const NOTES_PAGE = '/api/v1/billing/invoices/i1/credit-notes?page=1&pageSize=100';
const NOTES = '/api/v1/billing/invoices/i1/credit-notes';

describe('billing', () => {
  let backend: HttpTestingController;
  let notifier: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    notifier = { success: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideParcelFlowTesting({ subject: 'admin-david' }),
        provideRouter([]),
        InvoicesStore,
        InvoiceDetailStore,
        { provide: Notifier, useValue: notifier },
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    backend.verify();
    document.body.innerHTML = '';
  });

  /** Answers the three detail requests issued by InvoiceDetailStore.load. */
  async function answerDetail(current: Invoice, withPayment = true, notes = [creditNote('n0')]) {
    await settle();
    backend.expectOne(INVOICE).flush(current);
    const payment = backend.expectOne(PAYMENT);
    if (withPayment) {
      payment.flush({
        operationId: 'op-pay',
        invoiceId: current.id,
        outcome: 'Approved',
        amount: current.amount,
        currency: 'EUR',
        occurredAtUtc: '2026-10-01T10:05:00Z',
      });
    } else {
      payment.flush({ title: 'Payment not found' }, { status: 404, statusText: 'Not Found' });
    }
    backend.expectOne(NOTES_PAGE).flush(page(notes));
  }

  describe('InvoicesStore', () => {
    it('pages invoices and filters the loaded page by status', async () => {
      const store = TestBed.inject(InvoicesStore);
      store.loadPage(1);
      await settle();
      backend
        .expectOne('/api/v1/billing/invoices?page=1&pageSize=20')
        .flush(
          page(
            [
              invoice('a', { status: 'Paid' }),
              invoice('b', { status: 'PendingPayment' }),
              invoice('c', { status: 'Paid' }),
            ],
            45,
          ),
        );
      expect(store.pageCount()).toBe(3);
      store.setStatusFilter('Paid');
      expect(store.visible().map((item) => item.id)).toEqual(['a', 'c']);
      store.setStatusFilter('');
      expect(store.visible().length).toBe(3);
    });
  });

  describe('InvoiceDetailStore', () => {
    it('treats a missing payment (404) as no payment', async () => {
      const store = TestBed.inject(InvoiceDetailStore);
      store.load('i1');
      await answerDetail(invoice('i1', { status: 'PendingPayment' }), false, []);
      expect(store.error()).toBeNull();
      expect(store.payment()).toBeNull();
      expect(store.canIssueCreditNote()).toBe(false);
    });

    it('issues a credit note and refreshes the invoice totals', async () => {
      const store = TestBed.inject(InvoiceDetailStore);
      store.load('i1');
      await answerDetail(invoice('i1'));
      expect(store.payment()?.outcome).toBe('Approved');
      expect(store.canIssueCreditNote()).toBe(true);

      const issued = store.issueCreditNote('op-1', 5, 'ServiceIssue');
      expect(store.issuing()).toBe(true);
      await settle();
      const request = backend.expectOne(NOTES);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({
        operationId: 'op-1',
        amount: 5,
        reason: 'ServiceIssue',
      });
      request.flush(creditNote('n1'));
      await settle();
      backend
        .expectOne(INVOICE)
        .flush(invoice('i1', { status: 'PartiallyCredited', creditTotal: 5, remainingCredit: 15 }));
      await issued;
      expect(store.issuing()).toBe(false);
      expect(store.creditNotes().map((note) => note.id)).toEqual(['n0', 'n1']);
      expect(store.invoice()).toMatchObject({ status: 'PartiallyCredited', remainingCredit: 15 });
    });

    it('only allows credit notes on paid invoices with remaining credit', () => {
      expect(canIssueCreditNote(invoice('x', { status: 'Paid' }))).toBe(true);
      expect(canIssueCreditNote(invoice('x', { status: 'PartiallyCredited' }))).toBe(true);
      expect(canIssueCreditNote(invoice('x', { status: 'PendingPayment' }))).toBe(false);
      expect(canIssueCreditNote(invoice('x', { status: 'Credited', remainingCredit: 0 }))).toBe(
        false,
      );
      expect(canIssueCreditNote(null)).toBe(false);
    });
  });

  describe('CreditNoteDialogComponent', () => {
    async function openDialog() {
      const store = TestBed.inject(InvoiceDetailStore);
      store.load('i1');
      await answerDetail(invoice('i1', { amount: 20, remainingCredit: 12.5, creditTotal: 7.5 }));
      const fixture = TestBed.createComponent(CreditNoteDialogComponent);
      fixture.componentRef.setInput('open', true);
      await fixture.whenStable();
      return fixture;
    }

    async function submitAndCapture(): Promise<TestRequest> {
      submitDialog();
      await settle();
      return backend.expectOne(NOTES);
    }

    it('validates the amount against the remaining credit', async () => {
      const fixture = await openDialog();
      for (const amount of ['0', '12.51', '-1', '1.234']) {
        typeInto('credit-amount', amount);
        submitDialog();
        await settle();
        backend.expectNone(NOTES);
      }
      await fixture.whenStable();
      expect(document.body.textContent).toMatch(/validation\.(min|max|pattern)/);
    });

    it('reuses the same operationId when retrying after a failure', async () => {
      const fixture = await openDialog();
      typeInto('credit-amount', '12.5');
      selectOption('credit-reason', 'ServiceIssue');

      const first = await submitAndCapture();
      first.flush({ title: 'Down' }, { status: 503, statusText: 'Unavailable' });
      await settle();
      expect(notifier.error).toHaveBeenCalledTimes(1);
      expect(fixture.componentInstance.open()).toBe(true);
      // Let change detection re-enable the submit button.
      await fixture.whenStable();

      const retry = await submitAndCapture();
      expect(retry.request.body.operationId).toBe(first.request.body.operationId);
      expect(retry.request.body).toMatchObject({ amount: 12.5, reason: 'ServiceIssue' });
      retry.flush(creditNote('n2', { amount: 12.5 }));
      await settle();
      backend
        .expectOne(INVOICE)
        .flush(invoice('i1', { status: 'Credited', creditTotal: 20, remainingCredit: 0 }));
      await settle();
      expect(notifier.success).toHaveBeenCalledWith('invoices.creditNote.issued', {
        number: 'CN-n2',
      });
      expect(fixture.componentInstance.open()).toBe(false);
    });

    it('starts a new operation when the dialog is opened again', async () => {
      const fixture = await openDialog();
      typeInto('credit-amount', '2');
      const first = await submitAndCapture();
      first.flush({ title: 'Conflict' }, { status: 409, statusText: 'Conflict' });
      await settle();
      await fixture.whenStable();

      fixture.componentRef.setInput('open', false);
      await fixture.whenStable();
      fixture.componentRef.setInput('open', true);
      await fixture.whenStable();
      typeInto('credit-amount', '2');
      const second = await submitAndCapture();
      expect(second.request.body.operationId).not.toBe(first.request.body.operationId);
      second.flush({}, { status: 409, statusText: 'Conflict' });
      await settle();
    });

    it('applies the server amount error (400) on the field', async () => {
      const fixture = await openDialog();
      typeInto('credit-amount', '3');
      const request = await submitAndCapture();
      request.flush(
        { errors: { Amount: ['Amount must be between 0 and 2.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
      await settle();
      await fixture.whenStable();
      expect(document.body.textContent).toContain('Amount must be between 0 and 2.');
      expect(notifier.error).not.toHaveBeenCalled();
    });
  });

  describe('InvoiceDetailPage', () => {
    it('renders payment, credit notes and the credit action for paid invoices', async () => {
      const fixture = TestBed.createComponent(InvoiceDetailPage);
      fixture.componentRef.setInput('id', 'i1');
      await fixture.whenStable();
      await answerDetail(invoice('i1'));
      await fixture.whenStable();
      const text = fixture.nativeElement.textContent as string;
      expect(text).toContain('invoices.detailTitle');
      expect(text).toContain('CN-n0');
      expect(text).toContain('invoices.creditNote.open');
      expect(text).toContain('op-pay');
    });

    it('hides the credit action for unpaid invoices', async () => {
      const fixture = TestBed.createComponent(InvoiceDetailPage);
      fixture.componentRef.setInput('id', 'i1');
      await fixture.whenStable();
      await answerDetail(invoice('i1', { status: 'PendingPayment' }), false, []);
      await fixture.whenStable();
      const text = fixture.nativeElement.textContent as string;
      expect(text).not.toContain('invoices.creditNote.open');
      expect(text).toContain('invoices.payment.none');
      expect(text).toContain('invoices.creditNote.notAllowed');
    });
  });
});
