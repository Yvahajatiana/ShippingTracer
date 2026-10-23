import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ShipmentBody } from '@parcelflow/shared';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { Subject } from 'rxjs';
import { aWireShipment, anAddress, expectRequest, flushProblem } from '../../testing/fixtures';
import { NewShipmentStore, QUOTE_DEBOUNCE_MS } from './new-shipment.store';

const body = (weightKg: number): ShipmentBody => ({
  sender: anAddress(),
  recipient: anAddress({ name: 'Jonas Weber', city: 'Berlin', countryCode: 'DE' }),
  parcel: { weightKg, lengthCm: 30, widthCm: 20, heightCm: 15 },
});

const isQuote = (req: { url: string }) => req.url === '/api/v1/shipments/quote';

describe('NewShipmentStore', () => {
  let store: InstanceType<typeof NewShipmentStore>;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideParcelFlowTesting({ subject: 'customer-alice' }),
        { provide: QUOTE_DEBOUNCE_MS, useValue: 300 },
        NewShipmentStore,
      ],
    });
    store = TestBed.inject(NewShipmentStore);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.useRealTimers();
    backend.verify();
  });

  describe('live quote', () => {
    beforeEach(() => vi.useFakeTimers());

    it('debounces bursts of changes into a single request', async () => {
      const changes = new Subject<ShipmentBody | null>();
      store.requestQuote(changes);

      changes.next(body(1));
      await vi.advanceTimersByTimeAsync(100);
      changes.next(body(2));
      await vi.advanceTimersByTimeAsync(100);
      changes.next(body(3));
      await vi.advanceTimersByTimeAsync(299);
      backend.expectNone(isQuote);

      await vi.advanceTimersByTimeAsync(1);
      expect(store.quoting()).toBe(true);
      const request = backend.expectOne(isQuote);
      expect(request.request.body.parcel.weightKg).toBe(3);
      // Shipment sends DeliveryZone as an integer; the client normalizes it.
      request.flush({ price: 19.7, currency: 'EUR', zone: 1, billableWeightKg: 3 });

      expect(store.quoting()).toBe(false);
      expect(store.quote()).toEqual({
        price: 19.7,
        currency: 'EUR',
        zone: 'Europe',
        billableWeightKg: 3,
      });
    });

    it('skips identical bodies and clears the quote when the form becomes invalid', async () => {
      const changes = new Subject<ShipmentBody | null>();
      store.requestQuote(changes);

      changes.next(body(1));
      await vi.advanceTimersByTimeAsync(300);
      backend
        .expectOne(isQuote)
        .flush({ price: 10, currency: 'EUR', zone: 0, billableWeightKg: 1 });

      changes.next(body(1));
      await vi.advanceTimersByTimeAsync(300);
      backend.expectNone(isQuote);

      changes.next(null);
      await vi.advanceTimersByTimeAsync(300);
      expect(store.quote()).toBeNull();
      backend.expectNone(isQuote);
    });

    it('cancels an in-flight quote when the parcel changes again', async () => {
      const changes = new Subject<ShipmentBody | null>();
      store.requestQuote(changes);

      changes.next(body(1));
      await vi.advanceTimersByTimeAsync(300);
      const first = backend.expectOne(isQuote);

      changes.next(body(4));
      await vi.advanceTimersByTimeAsync(300);
      expect(first.cancelled).toBe(true);
      backend
        .expectOne(isQuote)
        .flush({ price: 22, currency: 'EUR', zone: 1, billableWeightKg: 4 });
      expect(store.quote()?.billableWeightKg).toBe(4);
    });

    it('exposes quote failures', async () => {
      const changes = new Subject<ShipmentBody | null>();
      store.requestQuote(changes);
      changes.next(body(1));
      await vi.advanceTimersByTimeAsync(300);
      flushProblem(backend.expectOne(isQuote), 400, { 'Parcel.WeightKg': ['Too heavy.'] });

      expect(store.quote()).toBeNull();
      expect(store.quoteError()?.status).toBe(400);
      expect(store.quoting()).toBe(false);
    });
  });

  it('creates the shipment', async () => {
    const pending = store.create(body(1.5));
    expect(store.creating()).toBe(true);
    const request = await expectRequest(backend, '/api/v1/shipments');
    expect(request.request.method).toBe('POST');
    request.flush(aWireShipment(7), { status: 201, statusText: 'Created' });

    const result = await pending;
    expect(store.creating()).toBe(false);
    expect(result.ok && result.value).toMatchObject({
      id: 'shipment-7',
      status: 'AwaitingPayment',
    });
  });

  it('returns creation failures', async () => {
    const pending = store.create(body(1.5));
    flushProblem(await expectRequest(backend, '/api/v1/shipments'), 400, {
      'Recipient.City': ['Required.'],
    });
    const result = await pending;
    expect(result.ok).toBe(false);
    expect(!result.ok && result.error.fieldErrors).toEqual({ 'recipient.City': ['Required.'] });
  });
});
