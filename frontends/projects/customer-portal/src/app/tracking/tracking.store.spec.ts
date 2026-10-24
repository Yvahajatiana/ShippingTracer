import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { aTracking, expectRequest, flushProblem } from '../../testing/fixtures';
import { TrackingStore } from './tracking.store';

describe('TrackingStore', () => {
  let store: InstanceType<typeof TrackingStore>;
  let backend: HttpTestingController;

  beforeEach(() => {
    // Anonymous visitor: public tracking must work without a session.
    TestBed.configureTestingModule({
      providers: [provideParcelFlowTesting({ subject: null }), TrackingStore],
    });
    store = TestBed.inject(TrackingStore);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('loads the public tracking anonymously, newest event first', async () => {
    store.load(' pf00240000fr ');
    expect(store.loading()).toBe(true);

    const request = await expectRequest(backend, '/api/v1/tracking/PF00240000FR');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush(aTracking());

    expect(store.loading()).toBe(false);
    expect(store.tracking()?.currentStatus).toBe('InTransit');
    expect(store.timeline().map((entry) => entry.status)).toEqual(['InTransit', 'Created']);
  });

  it('flags an unknown tracking number', async () => {
    store.load('PF99999999FR');
    flushProblem(await expectRequest(backend, '/api/v1/tracking/PF99999999FR'), 404);
    expect(store.notFound()).toBe(true);
    expect(store.invalidNumber()).toBe(false);
    expect(store.tracking()).toBeNull();
  });

  it('flags an invalid tracking number', async () => {
    store.load('ABC');
    flushProblem(await expectRequest(backend, '/api/v1/tracking/ABC'), 400, {
      TrackingNumber: ['Invalid tracking number.'],
    });
    expect(store.invalidNumber()).toBe(true);
    expect(store.notFound()).toBe(false);
  });

  it('cancels a superseded search', async () => {
    store.load('PF00000001FR');
    await new Promise<void>((resolve) => setTimeout(resolve));
    store.load('PF00000002FR');
    await new Promise<void>((resolve) => setTimeout(resolve));
    const first = backend.expectOne('/api/v1/tracking/PF00000001FR');
    expect(first.cancelled).toBe(true);
    backend
      .expectOne('/api/v1/tracking/PF00000002FR')
      .flush(aTracking({ trackingNumber: 'PF00000002FR' }));
    expect(store.tracking()?.trackingNumber).toBe('PF00000002FR');
  });
});
