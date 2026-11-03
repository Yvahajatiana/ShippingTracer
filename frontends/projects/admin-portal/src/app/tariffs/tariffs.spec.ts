import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Notifier } from '@parcelflow/shared';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { settle, submitDialog, typeInto } from '../testing/fixtures';
import { TariffDialogComponent } from './tariff-dialog.component';
import { TariffsStore } from './tariffs.store';

// The Shipment service still serializes zones as integers (issue #23).
const WIRE_TARIFFS = [
  { zone: 2, basePrice: 19.9, pricePerKg: 4.5, currency: 'EUR' },
  { zone: 0, basePrice: 4.9, pricePerKg: 1.2, currency: 'EUR' },
  { zone: 1, basePrice: 9.9, pricePerKg: 2.1, currency: 'EUR' },
];

describe('tariffs', () => {
  let store: InstanceType<typeof TariffsStore>;
  let backend: HttpTestingController;
  let notifier: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    notifier = { success: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideParcelFlowTesting({ subject: 'admin-david' }),
        provideRouter([]),
        TariffsStore,
        { provide: Notifier, useValue: notifier },
      ],
    });
    store = TestBed.inject(TariffsStore);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    backend.verify();
    document.body.innerHTML = '';
  });

  async function loadTariffs() {
    store.load();
    await settle();
    backend.expectOne('/api/v1/tariffs').flush(WIRE_TARIFFS);
  }

  it('loads tariffs in zone order with normalized zone names', async () => {
    await loadTariffs();
    expect(store.ordered().map((tariff) => tariff.zone)).toEqual([
      'Domestic',
      'Europe',
      'International',
    ]);
  });

  it('updates one zone and keeps the others', async () => {
    await loadTariffs();
    const saved = store.update('Europe', { basePrice: 11, pricePerKg: 2.5 });
    expect(store.savingZone()).toBe('Europe');
    await settle();
    const request = backend.expectOne('/api/v1/tariffs/Europe');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ basePrice: 11, pricePerKg: 2.5 });
    request.flush({ zone: 1, basePrice: 11, pricePerKg: 2.5, currency: 'EUR' });
    await saved;
    expect(store.savingZone()).toBeNull();
    expect(store.ordered()[1]).toEqual({
      zone: 'Europe',
      basePrice: 11,
      pricePerKg: 2.5,
      currency: 'EUR',
    });
    expect(store.ordered()[0].basePrice).toBe(4.9);
  });

  it('edits a tariff through the dialog and validates prices', async () => {
    await loadTariffs();
    const fixture = TestBed.createComponent(TariffDialogComponent);
    fixture.componentRef.setInput('tariff', store.ordered()[0]);
    fixture.componentRef.setInput('open', true);
    await fixture.whenStable();
    expect((document.getElementById('tariff-base') as HTMLInputElement).value).toBe('4.9');

    typeInto('tariff-per-kg', '0');
    submitDialog();
    await settle();
    backend.expectNone('/api/v1/tariffs/Domestic');

    typeInto('tariff-base', '5.5');
    typeInto('tariff-per-kg', '1.25');
    submitDialog();
    await settle();
    const request = backend.expectOne('/api/v1/tariffs/Domestic');
    expect(request.request.body).toEqual({ basePrice: 5.5, pricePerKg: 1.25 });
    request.flush({ zone: 0, basePrice: 5.5, pricePerKg: 1.25, currency: 'EUR' });
    await settle();
    expect(notifier.success).toHaveBeenCalledWith('tariffs.updated');
    expect(fixture.componentInstance.open()).toBe(false);
    expect(store.ordered()[0].basePrice).toBe(5.5);
  });
});
