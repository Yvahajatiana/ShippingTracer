import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { aNotification, aPage, anInvoice, expectRequest } from '../../testing/fixtures';
import { NotificationsStore } from '../notifications/notifications.store';
import { INVOICES_PAGE_SIZE, InvoicesStore } from './invoices.store';

describe('InvoicesStore', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideParcelFlowTesting({ subject: 'customer-alice' }),
        InvoicesStore,
        NotificationsStore,
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('loads page/pageSize results and derives navigation from the total', async () => {
    const store = TestBed.inject(InvoicesStore);
    store.loadPage(1);
    const request = await expectRequest(backend, (req) => req.url === '/api/v1/billing/invoices');
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe(String(INVOICES_PAGE_SIZE));
    request.flush(aPage([anInvoice()], 1, INVOICES_PAGE_SIZE, 45));

    expect(store.total()).toBe(45);
    expect(store.hasNext()).toBe(true);

    store.next();
    const second = await expectRequest(backend, (req) => req.url === '/api/v1/billing/invoices');
    expect(second.request.params.get('page')).toBe('2');
    second.flush(aPage([anInvoice({ id: 'invoice-2' })], 2, INVOICES_PAGE_SIZE, 45));
    expect(store.hasNext()).toBe(true);

    store.next();
    const third = await expectRequest(backend, (req) => req.url === '/api/v1/billing/invoices');
    third.flush(aPage([anInvoice({ id: 'invoice-3' })], 3, INVOICES_PAGE_SIZE, 45));
    expect(store.page()).toBe(3);
    expect(store.hasNext()).toBe(false);
  });
});

describe('NotificationsStore', () => {
  it('lists every notification of the customer without a shipment filter', async () => {
    TestBed.configureTestingModule({
      providers: [provideParcelFlowTesting({ subject: 'customer-alice' }), NotificationsStore],
    });
    const backend = TestBed.inject(HttpTestingController);
    const store = TestBed.inject(NotificationsStore);

    store.loadPage(1);
    const request = await expectRequest(backend, (req) => req.url === '/api/v1/notifications');
    expect(request.request.params.has('shipmentId')).toBe(false);
    request.flush(aPage([aNotification(0), aNotification(1)]));

    expect(store.items().map((item) => item.channel)).toEqual(['Email', 'Sms']);
    expect(store.hasNext()).toBe(false);
    backend.verify();
  });
});
