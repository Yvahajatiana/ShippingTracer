import { HttpClient, HttpContext, HttpHeaders } from '@angular/common/http';
import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { provideParcelFlowTesting } from '../../testing';
import { ApiError } from './api-error';
import { ANONYMOUS, buildApiUrl } from './api.interceptor';

// The bearer token is resolved asynchronously before the request reaches the backend.
const settle = () => new Promise<void>((resolve) => setTimeout(resolve));

describe('apiInterceptor', () => {
  function setup(subject?: string | null) {
    TestBed.configureTestingModule({ providers: provideParcelFlowTesting({ subject }) });
    return { http: TestBed.inject(HttpClient), backend: TestBed.inject(HttpTestingController) };
  }

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('attaches the bearer token to gateway calls', async () => {
    const { http, backend } = setup();
    const result = firstValueFrom(http.get('/api/v1/shipments'));
    await settle();
    const request = backend.expectOne('/api/v1/shipments');
    expect(request.request.headers.get('Authorization')).toMatch(/^Bearer mock\./);
    request.flush([]);
    await expect(result).resolves.toEqual([]);
  });

  it('does not attach a token to anonymous calls', async () => {
    const { http, backend } = setup();
    const result = firstValueFrom(
      http.get('/api/v1/tracking/PF1', { context: new HttpContext().set(ANONYMOUS, true) }),
    );
    await settle();
    const request = backend.expectOne('/api/v1/tracking/PF1');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
    await result;
  });

  it('never sends tokens to other origins', async () => {
    const { http, backend } = setup();
    const result = firstValueFrom(http.get('i18n/fr.json'));
    const request = backend.expectOne('i18n/fr.json');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
    await result;
  });

  it('fails with 401 without calling the API when signed out', async () => {
    const { http } = setup(null);
    await expect(firstValueFrom(http.get('/api/v1/shipments'))).rejects.toMatchObject({
      status: 401,
      messageKey: 'errors.http.401',
    });
  });

  it('maps ProblemDetails into ApiError', async () => {
    const { http, backend } = setup();
    const result = firstValueFrom(http.post('/api/v1/customers', {}));
    await settle();
    backend.expectOne('/api/v1/customers').flush(
      {
        title: 'Validation failed',
        status: 400,
        traceId: 'trace-1',
        errors: { FirstName: ['Required.'] },
      },
      {
        status: 400,
        statusText: 'Bad Request',
        headers: new HttpHeaders({ 'Content-Type': 'application/problem+json' }),
      },
    );
    const error = await result.catch((caught: unknown) => caught);
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({
      status: 400,
      messageKey: 'errors.http.400',
      traceId: 'trace-1',
      fieldErrors: { firstName: ['Required.'] },
    });
  });

  it('maps server errors to a generic message', async () => {
    const { http, backend } = setup();
    const result = firstValueFrom(http.get('/api/v1/reports/kpis'));
    await settle();
    backend
      .expectOne('/api/v1/reports/kpis')
      .flush(null, { status: 503, statusText: 'Unavailable' });
    await expect(result).rejects.toMatchObject({ status: 503, messageKey: 'errors.http.5xx' });
  });
});

describe('buildApiUrl', () => {
  it('keeps API paths under the configured origin', () => {
    expect(buildApiUrl('https://api.example', '/api/v1/shipments')).toBe(
      'https://api.example/api/v1/shipments',
    );
    expect(buildApiUrl('', '/api/v1/shipments')).toBe('/api/v1/shipments');
  });

  it('rejects paths that could escape the API', () => {
    expect(() => buildApiUrl('', '//evil.example/api/v1')).toThrow();
    expect(() => buildApiUrl('', '/api/../admin')).toThrow();
    expect(() => buildApiUrl('', '/other')).toThrow();
  });
});
