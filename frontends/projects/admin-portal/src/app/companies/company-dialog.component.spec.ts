import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Notifier } from '@parcelflow/shared';
import { provideParcelFlowTesting } from '@parcelflow/shared/testing';
import { CompaniesStore } from './companies.store';
import { CompanyDialogComponent } from './company-dialog.component';
import { settle, submitDialog as submit, typeInto as type } from '../testing/fixtures';

describe('CompanyDialogComponent', () => {
  let backend: HttpTestingController;
  let notifier: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

  async function open(company: unknown = null) {
    const fixture = TestBed.createComponent(CompanyDialogComponent);
    fixture.componentRef.setInput('company', company);
    fixture.componentRef.setInput('open', true);
    await fixture.whenStable();
    return fixture;
  }

  beforeEach(() => {
    notifier = { success: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideParcelFlowTesting({ subject: 'admin-david' }),
        provideRouter([]),
        CompaniesStore,
        { provide: Notifier, useValue: notifier },
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    backend.verify();
    document.body.innerHTML = '';
  });

  it('renders the create form and blocks invalid submissions', async () => {
    const fixture = await open();
    expect(document.querySelector('[hlmDialogTitle]')?.textContent).toContain(
      'companies.createTitle',
    );
    submit();
    await fixture.whenStable();
    backend.expectNone('/api/v1/companies');
    expect(document.querySelectorAll('pf-field-error small').length).toBe(3);
  });

  it('applies 400 field errors returned by the server on create', async () => {
    const fixture = await open();
    type('company-name', 'Acme');
    type('company-registration', 'RCS-1');
    type('company-email', 'contact@acme.test');
    submit();
    await settle();
    const request = backend.expectOne('/api/v1/companies');
    expect(request.request.body).toEqual({
      name: 'Acme',
      registrationNumber: 'RCS-1',
      email: 'contact@acme.test',
    });
    request.flush(
      { title: 'Validation failed', errors: { RegistrationNumber: ['Already registered.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();
    await fixture.whenStable();
    expect(document.body.textContent).toContain('Already registered.');
    expect(notifier.error).not.toHaveBeenCalled();
    expect(fixture.componentInstance.open()).toBe(true);
  });

  it('updates an existing company and closes', async () => {
    const fixture = await open({
      id: 'c1',
      name: 'Acme',
      registrationNumber: 'RCS-1',
      email: 'a@acme.test',
    });
    expect((document.getElementById('company-name') as HTMLInputElement).value).toBe('Acme');
    type('company-name', 'Acme SA');
    submit();
    await settle();
    const request = backend.expectOne('/api/v1/companies/c1');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body.name).toBe('Acme SA');
    request.flush({ id: 'c1', name: 'Acme SA', registrationNumber: 'RCS-1', email: 'a@acme.test' });
    await settle();
    expect(notifier.success).toHaveBeenCalledWith('common.saved');
    expect(fixture.componentInstance.open()).toBe(false);
  });

  it('reports non-validation failures with a toast', async () => {
    const fixture = await open();
    type('company-name', 'Acme');
    type('company-registration', 'RCS-1');
    type('company-email', 'contact@acme.test');
    submit();
    await settle();
    backend
      .expectOne('/api/v1/companies')
      .flush({ title: 'Down' }, { status: 503, statusText: 'Unavailable' });
    await settle();
    expect(notifier.error).toHaveBeenCalledWith(expect.objectContaining({ status: 503 }));
    expect(fixture.componentInstance.open()).toBe(true);
  });
});
