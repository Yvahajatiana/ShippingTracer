import { HttpTestingController } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { settle, warehouse } from '../testing/fixtures';
import { ScanPanelComponent } from './scan-panel.component';
import { WarehouseDetailStore } from './warehouses.store';

@Component({
  selector: 'pf-scan-host',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ScanPanelComponent],
  providers: [WarehouseDetailStore],
  template: `<pf-scan-panel />`,
})
class ScanHostComponent {}

describe('ScanPanelComponent', () => {
  let backend: HttpTestingController;

  async function render() {
    TestBed.configureTestingModule({
      imports: [ScanHostComponent],
      providers: [provideParcelFlowTesting({ subject: 'operator-chloe' }), provideRouter([])],
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ScanHostComponent);
    const store = fixture.debugElement.children[0].injector.get(WarehouseDetailStore);
    store.load('wh-1');
    await settle();
    backend.expectOne('/api/v1/warehouses/wh-1').flush(warehouse());
    backend
      .expectOne((req) => req.url === '/api/v1/warehouses/wh-1/stock')
      .flush({ warehouseId: 'wh-1', page: 1, pageSize: 20, total: 0, items: [] });
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const input = element.querySelector<HTMLInputElement>('#scan-tracking-number')!;
    return { fixture, element, input };
  }

  afterEach(() => backend.verify());

  function type(input: HTMLInputElement, value: string) {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  it('submits on Enter, clears the field and keeps the focus', async () => {
    const { fixture, element, input } = await render();
    expect(document.activeElement).toBe(input);

    type(input, 'pf00240000fr');
    expect(input.value).toBe('PF00240000FR');
    input.form!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(input.value).toBe('');
    expect(document.activeElement).toBe(input);
    await settle();
    const request = backend.expectOne('/api/v1/warehouses/scans');
    expect(request.request.body).toMatchObject({
      trackingNumber: 'PF00240000FR',
      direction: 'Entry',
    });
    request.flush({}, { status: 409, statusText: 'Conflict' });
    await settle();
    await fixture.whenStable();

    const item = element.querySelector('[data-testid=scan-history] li')!;
    expect(item.getAttribute('data-outcome')).toBe('error');
    expect(item.textContent).toContain('warehouses.scan.errors.alreadyStored');
  });

  it('does not send an invalid tracking number', async () => {
    const { fixture, element, input } = await render();
    type(input, 'AB');
    input.form!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    await settle();
    backend.expectNone('/api/v1/warehouses/scans');
    expect(element.textContent).toContain('validation.pattern');
  });

  it('switches to exit scans', async () => {
    const { fixture, element, input } = await render();
    element.querySelector<HTMLButtonElement>('[data-testid=scan-exit]')!.click();
    await fixture.whenStable();
    type(input, 'PF00240000FR');
    input.form!.dispatchEvent(new Event('submit'));
    await settle();
    const request = backend.expectOne('/api/v1/warehouses/scans');
    expect(request.request.body.direction).toBe('Exit');
    request.flush({
      ...request.request.body,
      shipmentId: 'sh-1',
      occurredAtUtc: '2026-10-23T09:00:00Z',
    });
    await settle();
    backend
      .expectOne((req) => req.url === '/api/v1/warehouses/wh-1/stock')
      .flush({ warehouseId: 'wh-1', page: 1, pageSize: 20, total: 0, items: [] });
  });
});
