import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { aProfile, aWireShipment, expectRequest, settle } from '../../testing/fixtures';
import { QUOTE_DEBOUNCE_MS } from './new-shipment.store';
import { NewShipmentPage } from './new-shipment.page';

const isQuote = (req: { url: string }) => req.url === '/api/v1/shipments/quote';

describe('NewShipmentPage', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideParcelFlowTesting({ subject: 'customer-alice' }),
        provideRouter([]),
        { provide: QUOTE_DEBOUNCE_MS, useValue: 0 },
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  function type(root: HTMLElement, id: string, value: string) {
    const input = root.querySelector<HTMLInputElement>(`#${id}`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  it('prefills the sender, quotes the valid form and creates the shipment', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(NewShipmentPage);
    await fixture.whenStable();
    (await expectRequest(backend, '/api/v1/customers/me')).flush(aProfile());
    await fixture.whenStable();

    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector<HTMLInputElement>('#pf-sender-name')!.value).toBe('Alice Martin');
    expect(element.querySelector<HTMLInputElement>('#pf-sender-city')!.value).toBe('Paris');
    // The recipient is still empty: no quote yet.
    await settle();
    backend.expectNone(isQuote);
    expect(element.textContent).toContain('newShipment.quote.hint');

    type(element, 'pf-recipient-name', 'Jonas Weber');
    type(element, 'pf-recipient-email', 'jonas@example.test');
    type(element, 'pf-recipient-phone', '+4930123456');
    type(element, 'pf-recipient-line1', 'Unter den Linden 10');
    type(element, 'pf-recipient-postal-code', '10117');
    type(element, 'pf-recipient-city', 'Berlin');
    type(element, 'pf-recipient-country', 'de');
    expect(element.querySelector<HTMLInputElement>('#pf-recipient-country')!.value).toBe('DE');

    const quote = await expectRequest(backend, isQuote);
    expect(quote.request.body.recipient.countryCode).toBe('DE');
    quote.flush({ price: 19.7, currency: 'EUR', zone: 1, billableWeightKg: 1.8 });
    await fixture.whenStable();
    expect(element.querySelector('[data-testid=quote-price]')?.textContent).toMatch(/19[.,]70/);
    expect(element.textContent).toContain('enum.DeliveryZone.Europe');

    element.querySelector<HTMLButtonElement>('button[type=submit]')!.click();
    const create = await expectRequest(backend, '/api/v1/shipments');
    expect(create.request.method).toBe('POST');
    expect(create.request.body.sender.name).toBe('Alice Martin');
    create.flush(aWireShipment(3), { status: 201, statusText: 'Created' });
    await settle();

    expect(navigate).toHaveBeenCalledWith(['/shipments', 'shipment-3']);
  });

  it('does not submit an invalid form', async () => {
    const fixture = TestBed.createComponent(NewShipmentPage);
    await fixture.whenStable();
    (await expectRequest(backend, '/api/v1/customers/me')).flush(aProfile());
    await fixture.whenStable();

    const element: HTMLElement = fixture.nativeElement;
    type(element, 'pf-parcel-weight', '45');
    element.querySelector<HTMLButtonElement>('button[type=submit]')!.click();
    await fixture.whenStable();

    expect(element.textContent).toContain('validation.max');
    expect(element.textContent).toContain('validation.required');
    await settle();
    backend.expectNone('/api/v1/shipments');
  });
});
