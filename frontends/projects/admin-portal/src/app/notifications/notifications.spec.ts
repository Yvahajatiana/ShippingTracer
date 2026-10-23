import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { notification, page, settle, typeInto } from '../testing/fixtures';
import { NotificationsPage } from './notifications.page';
import { NotificationsStore } from './notifications.store';

const SHIPMENT = '11111111-2222-3333-4444-555555555555';

describe('notifications', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideParcelFlowTesting({ subject: 'admin-david' }),
        provideRouter([]),
        NotificationsStore,
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    backend.verify();
    document.body.innerHTML = '';
  });

  it('filters by shipment and keeps the filter while paging', async () => {
    const store = TestBed.inject(NotificationsStore);
    store.loadPage(1);
    await settle();
    backend
      .expectOne('/api/v1/notifications?page=1&pageSize=20')
      .flush(page([notification('n1')], 30));

    store.search(SHIPMENT);
    await settle();
    backend
      .expectOne(`/api/v1/notifications?shipmentId=${SHIPMENT}&page=1&pageSize=20`)
      .flush(page([notification('n2')], 21));
    expect(store.query()).toBe(SHIPMENT);

    store.nextPage();
    await settle();
    backend
      .expectOne(`/api/v1/notifications?shipmentId=${SHIPMENT}&page=2&pageSize=20`)
      .flush(page([notification('n3')], 21, 2));
    expect(store.items()[0].id).toBe('n3');

    store.select(store.items()[0]);
    expect(store.selected()?.id).toBe('n3');
  });

  it('validates the shipment filter and opens the detail sheet', async () => {
    const fixture = TestBed.createComponent(NotificationsPage);
    await fixture.whenStable();
    await settle();
    backend
      .expectOne('/api/v1/notifications?page=1&pageSize=20')
      .flush(page([notification('n1', { subject: 'Colis expédié' })]));
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;

    typeInto('notification-shipment', 'not-a-guid');
    element.querySelector<HTMLButtonElement>('form[role="search"] button[type="submit"]')!.click();
    await settle();
    await fixture.whenStable();
    backend.expectNone((request) => request.url === '/api/v1/notifications');
    expect(element.textContent).toContain('validation.pattern');

    typeInto('notification-shipment', SHIPMENT);
    element.querySelector<HTMLButtonElement>('form[role="search"] button[type="submit"]')!.click();
    await settle();
    backend
      .expectOne(`/api/v1/notifications?shipmentId=${SHIPMENT}&page=1&pageSize=20`)
      .flush(page([notification('n1', { subject: 'Colis expédié' })]));
    await fixture.whenStable();

    element.querySelector<HTMLButtonElement>('tbody button')!.click();
    await fixture.whenStable();
    const sheet = document.querySelector('hlm-sheet-content');
    expect(sheet?.textContent).toContain('Colis expédié');
    expect(sheet?.textContent).toContain('Your parcel is on its way.');
    expect(sheet?.textContent).toContain('alice@test.dev');
  });
});
