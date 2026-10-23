import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { aProfile, expectRequest, flushProblem, settle } from '../../testing/fixtures';
import { ProfilePage } from './profile.page';

describe('ProfilePage', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideParcelFlowTesting({ subject: 'customer-alice' }), provideRouter([])],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  async function render() {
    const fixture = TestBed.createComponent(ProfilePage);
    await fixture.whenStable();
    return fixture;
  }

  it('shows the onboarding form prefilled from the identity when the profile is missing', async () => {
    const fixture = await render();
    flushProblem(await expectRequest(backend, '/api/v1/customers/me'), 404);
    await fixture.whenStable();

    const element: HTMLElement = fixture.nativeElement;
    expect(element.textContent).toContain('profile.onboarding.title');
    const firstName = element.querySelector<HTMLInputElement>('#pf-profile-first-name')!;
    const email = element.querySelector<HTMLInputElement>('#pf-profile-email')!;
    expect(firstName.value).toBe('Alice');
    expect(email.value).toBe('alice@example.test');

    element.querySelector<HTMLButtonElement>('button[type=submit]')!.click();
    const request = await expectRequest(backend, '/api/v1/customers');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toMatchObject({ firstName: 'Alice', lastName: 'Martin' });
    request.flush(aProfile(), { status: 201, statusText: 'Created' });
    await settle();
    await fixture.whenStable();

    expect(element.textContent).toContain('profile.identity');
    expect(element.textContent).toContain('Domicile');
  });

  it('maps server validation errors onto the onboarding form', async () => {
    const fixture = await render();
    flushProblem(await expectRequest(backend, '/api/v1/customers/me'), 404);
    await fixture.whenStable();

    const element: HTMLElement = fixture.nativeElement;
    element.querySelector<HTMLButtonElement>('button[type=submit]')!.click();
    flushProblem(await expectRequest(backend, '/api/v1/customers'), 400, {
      Email: ['Email already used.'],
    });
    await settle();
    await fixture.whenStable();

    expect(element.textContent).toContain('Email already used.');
  });

  it('lists saved addresses with accessible edit and delete buttons', async () => {
    const fixture = await render();
    (await expectRequest(backend, '/api/v1/customers/me')).flush(aProfile());
    await fixture.whenStable();

    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('#pf-profile-last-name')).not.toBeNull();
    const labels = [...element.querySelectorAll('button[aria-label]')].map((button) =>
      button.getAttribute('aria-label'),
    );
    expect(labels).toEqual(expect.arrayContaining(['address.editLabel', 'address.deleteLabel']));
  });
});
