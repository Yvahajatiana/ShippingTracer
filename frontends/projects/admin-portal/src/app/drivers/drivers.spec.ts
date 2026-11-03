import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Notifier } from '@parcelflow/shared';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { driver, page, settle, submitDialog, typeInto } from '../testing/fixtures';
import { DriverDialogComponent } from './driver-dialog.component';
import { DriversStore } from './drivers.store';

const FIRST_PAGE = '/api/v1/delivery/drivers?page=1&pageSize=20';

describe('drivers', () => {
  let store: InstanceType<typeof DriversStore>;
  let backend: HttpTestingController;
  let notifier: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    notifier = { success: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideParcelFlowTesting({
          subject: 'admin-david',
          translations: { drivers: { subjectTaken: 'Subject already registered' } },
        }),
        provideRouter([]),
        DriversStore,
        { provide: Notifier, useValue: notifier },
      ],
    });
    store = TestBed.inject(DriversStore);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    backend.verify();
    document.body.innerHTML = '';
  });

  it('loads drivers and exposes errors', async () => {
    store.loadPage(1);
    await settle();
    backend.expectOne(FIRST_PAGE).flush(page([driver('driver-emma')]));
    expect(store.items()[0].subject).toBe('driver-emma');

    store.reload();
    await settle();
    backend.expectOne(FIRST_PAGE).flush({}, { status: 500, statusText: 'Error' });
    expect(store.error()).toMatchObject({ status: 500 });
  });

  it('creates a driver and reloads the page', async () => {
    const created = store.create('driver-new', 'New Driver');
    await settle();
    const request = backend.expectOne('/api/v1/delivery/drivers');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ subject: 'driver-new', displayName: 'New Driver' });
    request.flush(driver('driver-new', { displayName: 'New Driver' }));
    await created;
    await settle();
    backend.expectOne(FIRST_PAGE).flush(page([driver('driver-new')]));
    expect(store.items().length).toBe(1);
  });

  it('updates display name and activity', async () => {
    store.loadPage(1);
    await settle();
    backend.expectOne(FIRST_PAGE).flush(page([driver('driver-emma')]));
    const updated = store.update('driver-emma', 'Emma R.', false);
    await settle();
    const request = backend.expectOne('/api/v1/delivery/drivers/driver-emma');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ displayName: 'Emma R.', isActive: false });
    request.flush(driver('driver-emma', { displayName: 'Emma R.', isActive: false }));
    await updated;
    expect(store.items()[0]).toMatchObject({ displayName: 'Emma R.', isActive: false });
  });

  describe('DriverDialogComponent', () => {
    async function open(existing: ReturnType<typeof driver> | null = null) {
      const fixture = TestBed.createComponent(DriverDialogComponent);
      fixture.componentRef.setInput('driver', existing);
      fixture.componentRef.setInput('open', true);
      await fixture.whenStable();
      return fixture;
    }

    it('creates a driver from the dialog', async () => {
      const fixture = await open();
      typeInto('driver-subject', '  driver-zoe ');
      typeInto('driver-name', 'Zoé Martin');
      submitDialog();
      await settle();
      const request = backend.expectOne('/api/v1/delivery/drivers');
      expect(request.request.body).toEqual({ subject: 'driver-zoe', displayName: 'Zoé Martin' });
      request.flush(driver('driver-zoe'));
      await settle();
      backend.expectOne(FIRST_PAGE).flush(page([driver('driver-zoe')]));
      expect(notifier.success).toHaveBeenCalledWith('drivers.created');
      expect(fixture.componentInstance.open()).toBe(false);
    });

    it('flags an already registered subject (409) on the subject field', async () => {
      const fixture = await open();
      typeInto('driver-subject', 'driver-emma');
      typeInto('driver-name', 'Emma');
      submitDialog();
      await settle();
      backend
        .expectOne('/api/v1/delivery/drivers')
        .flush({ title: 'Driver already exists' }, { status: 409, statusText: 'Conflict' });
      await settle();
      await fixture.whenStable();
      expect(document.body.textContent).toContain('Subject already registered');
      expect(fixture.componentInstance.open()).toBe(true);
    });

    it('edits a driver with the subject locked', async () => {
      const fixture = await open(driver('driver-gael', { isActive: false }));
      expect((document.getElementById('driver-subject') as HTMLInputElement).disabled).toBe(true);
      typeInto('driver-name', 'Gaël M.');
      (document.getElementById('driver-active') as HTMLElement).click();
      await fixture.whenStable();
      submitDialog();
      await settle();
      const request = backend.expectOne('/api/v1/delivery/drivers/driver-gael');
      expect(request.request.body).toEqual({ displayName: 'Gaël M.', isActive: true });
      request.flush(driver('driver-gael', { displayName: 'Gaël M.' }));
      await settle();
      expect(notifier.success).toHaveBeenCalledWith('common.saved');
    });
  });
});
