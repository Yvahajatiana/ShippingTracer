import { HttpTestingController } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { BrnDialogRef } from '@spartan-ng/brain/dialog';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { route, settle, stop } from '../testing/fixtures';
import { ConfirmDeliveryFormComponent } from './confirm-delivery-form.component';
import { RouteDetailStore } from './route-detail.store';

@Component({
  selector: 'pf-confirm-host',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ConfirmDeliveryFormComponent],
  providers: [
    RouteDetailStore,
    // The form is rendered outside a dialog: a minimal dialog reference is enough.
    {
      provide: BrnDialogRef,
      useValue: {
        dialogId: 'test',
        registerDescription: () => undefined,
        unregisterDescription: () => undefined,
      },
    },
  ],
  template: `<pf-confirm-delivery-form
    shipmentId="sh-1"
    trackingNumber="PF1FR"
    (completed)="completed.set(true)"
  />`,
})
class ConfirmHostComponent {
  readonly completed = signal(false);
}

describe('ConfirmDeliveryFormComponent', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    // jsdom has no canvas implementation.
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(null);
  });

  afterEach(() => {
    backend.verify();
    vi.restoreAllMocks();
  });

  async function render() {
    TestBed.configureTestingModule({
      imports: [ConfirmHostComponent],
      providers: [provideParcelFlowTesting({ subject: 'operator-chloe' }), provideRouter([])],
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ConfirmHostComponent);
    const host = fixture.debugElement.children[0];
    const store = host.injector.get(RouteDetailStore);
    // Put the store in the in-progress state.
    const loading = store.load('route-1');
    await settle();
    backend
      .expectOne('/api/v1/delivery/routes/route-1')
      .flush(route({ status: 'InProgress', driverSubject: 'driver-emma', stops: [stop()] }));
    backend
      .expectOne((req) => req.url === '/api/v1/delivery/drivers')
      .flush({ page: 1, pageSize: 100, total: 0, items: [] });
    await loading;
    await settle();
    backend.match((req) => req.url.startsWith('/api/v1/shipments/')).forEach((r) => r.flush(null));
    await fixture.whenStable();
    const form = host.componentInstance as ConfirmDeliveryFormComponent;
    const element = fixture.nativeElement as HTMLElement;
    const name = element.querySelector<HTMLInputElement>('#delivery-recipient')!;
    return { fixture, form, element, name };
  }

  it('requires a recipient name and at least one proof', async () => {
    const { fixture, element } = await render();
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    expect(element.textContent).toContain('validation.required');
    expect(element.textContent).toContain('routes.delivery.proofRequired');
    await settle();
    backend.expectNone((req) => req.url.endsWith('/delivery'));
  });

  it('rejects photos above 10 MB', async () => {
    const { fixture, element } = await render();
    const input = element.querySelector<HTMLInputElement>('[data-testid=photo-input]')!;
    const big = new File([new Uint8Array(10 * 1024 * 1024 + 1)], 'big.jpg', { type: 'image/jpeg' });
    Object.defineProperty(input, 'files', { value: [big], configurable: true });
    input.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    expect(element.textContent).toContain('routes.delivery.photoTooLarge');
  });

  it('sends the photo as base64 and keeps one operationId across retries', async () => {
    const { fixture, form, element, name } = await render();
    name.value = 'Jeanne Martin';
    name.dispatchEvent(new Event('input'));
    const input = element.querySelector<HTMLInputElement>('[data-testid=photo-input]')!;
    const photo = new File([new Uint8Array([255, 216, 255])], 'photo.jpg', { type: 'image/jpeg' });
    Object.defineProperty(input, 'files', { value: [photo], configurable: true });
    input.dispatchEvent(new Event('change'));
    await fixture.whenStable();

    const submit = () => element.querySelector('form')!.dispatchEvent(new Event('submit'));
    const url = '/api/v1/delivery/routes/route-1/stops/sh-1/delivery';

    submit();
    const first = await vi.waitFor(() => backend.expectOne(url));
    expect(first.request.body).toEqual({
      operationId: form.operationId,
      recipientName: 'Jeanne Martin',
      signaturePng: null,
      photo: '/9j/',
    });
    first.flush({ title: 'Down' }, { status: 503, statusText: 'Unavailable' });
    await settle();
    await fixture.whenStable();

    submit();
    const second = await vi.waitFor(() => backend.expectOne(url));
    expect(second.request.body.operationId).toBe(form.operationId);
    second.flush({});
    (await vi.waitFor(() => backend.expectOne('/api/v1/delivery/routes/route-1'))).flush(
      route({ status: 'Completed', stops: [stop({ status: 'Delivered' })] }),
    );
    await vi.waitFor(() =>
      expect((fixture.componentInstance as ConfirmHostComponent).completed()).toBe(true),
    );
  });
});
