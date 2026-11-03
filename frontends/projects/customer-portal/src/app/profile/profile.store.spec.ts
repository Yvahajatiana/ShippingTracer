import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { aProfile, expectRequest, flushProblem } from '../../testing/fixtures';
import { ProfileStore } from './profile.store';

const NEW_ADDRESS = {
  label: 'Bureau',
  line1: '5 place Bellecour',
  line2: null,
  postalCode: '69002',
  city: 'Lyon',
  countryCode: 'FR',
};

describe('ProfileStore', () => {
  let store: InstanceType<typeof ProfileStore>;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideParcelFlowTesting({ subject: 'customer-alice' }), ProfileStore],
    });
    store = TestBed.inject(ProfileStore);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  async function loadProfile() {
    store.load();
    (await expectRequest(backend, '/api/v1/customers/me')).flush(aProfile());
  }

  it('loads the profile of the signed-in customer', async () => {
    await loadProfile();
    expect(store.profile()?.id).toBe('profile-1');
    expect(store.fullName()).toBe('Alice Martin');
    expect(store.addresses()).toHaveLength(1);
    expect(store.missing()).toBe(false);
  });

  it('switches to onboarding when the profile does not exist yet', async () => {
    store.load();
    flushProblem(await expectRequest(backend, '/api/v1/customers/me'), 404);
    expect(store.missing()).toBe(true);
    expect(store.error()).toBeNull();
    expect(store.loaded()).toBe(true);
  });

  it('reports other load failures as errors', async () => {
    store.load();
    flushProblem(await expectRequest(backend, '/api/v1/customers/me'), 500);
    expect(store.missing()).toBe(false);
    expect(store.error()?.status).toBe(500);
  });

  it('creates the profile and leaves onboarding', async () => {
    store.load();
    flushProblem(await expectRequest(backend, '/api/v1/customers/me'), 404);

    const body = { firstName: 'Bob', lastName: 'Durand', email: 'bob@example.test', phone: null };
    const pending = store.create(body);
    expect(store.saving()).toBe(true);
    const request = await expectRequest(backend, '/api/v1/customers');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(body);
    request.flush(aProfile({ firstName: 'Bob', addresses: [] }), {
      status: 201,
      statusText: 'Created',
    });

    const result = await pending;
    expect(result.ok).toBe(true);
    expect(store.saving()).toBe(false);
    expect(store.missing()).toBe(false);
    expect(store.profile()?.firstName).toBe('Bob');
  });

  it('returns server validation errors to the caller', async () => {
    await loadProfile();
    const pending = store.update({ firstName: '', lastName: 'Martin', email: 'x', phone: null });
    const request = await expectRequest(backend, '/api/v1/customers/profile-1');
    expect(request.request.method).toBe('PUT');
    flushProblem(request, 400, { FirstName: ['Required.'] });

    const result = await pending;
    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.error.fieldErrors).toEqual({ firstName: ['Required.'] });
    }
    // The loaded profile is untouched.
    expect(store.profile()?.firstName).toBe('Alice');
  });

  it('adds, updates and deletes addresses', async () => {
    await loadProfile();

    const adding = store.addAddress(NEW_ADDRESS);
    (await expectRequest(backend, '/api/v1/customers/profile-1/addresses')).flush(
      { id: 'address-2', ...NEW_ADDRESS },
      { status: 201, statusText: 'Created' },
    );
    expect((await adding).ok).toBe(true);
    expect(store.addresses().map((item) => item.id)).toEqual(['address-1', 'address-2']);

    const updating = store.updateAddress('address-2', { ...NEW_ADDRESS, label: 'Atelier' });
    const put = await expectRequest(backend, '/api/v1/customers/profile-1/addresses/address-2');
    expect(put.request.method).toBe('PUT');
    put.flush({ id: 'address-2', ...NEW_ADDRESS, label: 'Atelier' });
    await updating;
    expect(store.addresses()[1].label).toBe('Atelier');

    const deleting = store.deleteAddress('address-1');
    const del = await expectRequest(backend, '/api/v1/customers/profile-1/addresses/address-1');
    expect(del.request.method).toBe('DELETE');
    del.flush(null, { status: 204, statusText: 'No Content' });
    expect((await deleting).ok).toBe(true);
    expect(store.addresses().map((item) => item.id)).toEqual(['address-2']);
  });
});
