import { getResponse } from 'msw';
import { createDb } from './db';
import { createHandlers } from './handlers';

function bearer(sub: string, roles: string[]) {
  return { Authorization: `Bearer mock.${btoa(JSON.stringify({ sub, roles }))}` };
}

describe('mock API handlers', () => {
  const db = createDb();
  const handlers = createHandlers(db);
  const call = async (path: string, init?: RequestInit) => {
    const response = await getResponse(handlers, new Request(`http://localhost${path}`, init));
    if (!response) throw new Error(`No handler for ${path}`);
    return response;
  };

  it('requires a token for private endpoints', async () => {
    expect((await call('/api/v1/shipments')).status).toBe(401);
  });

  it('enforces role policies', async () => {
    const response = await call('/api/v1/reports/kpis?from=2026-01-01&to=2026-12-31', {
      headers: bearer('customer-alice', ['customer']),
    });
    expect(response.status).toBe(403);
  });

  it('only returns the caller own shipments, with integer enums like the real service', async () => {
    const response = await call('/api/v1/shipments?limit=100', {
      headers: bearer('customer-alice', ['customer']),
    });
    const items = (await response.json()) as {
      ownerSubject: string;
      zone: unknown;
      status: unknown;
    }[];
    expect(items.length).toBeGreaterThan(0);
    expect(items.every((item) => item.ownerSubject === 'customer-alice')).toBe(true);
    expect(typeof items[0].zone).toBe('number');
    expect(typeof items[0].status).toBe('number');
  });

  it('serves public tracking anonymously', async () => {
    const trackingNumber = db.shipments[0].trackingNumber;
    const response = await call(`/api/v1/tracking/${trackingNumber.toLowerCase()}`);
    expect(response.status).toBe(200);
    const body = (await response.json()) as {
      trackingNumber: string;
      history: unknown[];
      shipmentId?: string;
    };
    expect(body.trackingNumber).toBe(trackingNumber);
    expect(body.shipmentId).toBeUndefined();
    expect(body.history.length).toBeGreaterThan(0);
  });

  it('returns ProblemDetails with field errors on invalid input', async () => {
    const response = await call('/api/v1/companies', {
      method: 'POST',
      headers: { ...bearer('admin-david', ['admin']), 'Content-Type': 'application/json' },
      body: JSON.stringify({ name: '', registrationNumber: 'X', email: 'a@b.c' }),
    });
    expect(response.status).toBe(400);
    expect(response.headers.get('Content-Type')).toContain('application/problem+json');
    expect(await response.json()).toMatchObject({ errors: { Name: expect.any(Array) } });
  });

  it('makes payments idempotent by operation id', async () => {
    const invoice = db.invoices.find((item) => item.status === 'PendingPayment')!;
    const init = {
      method: 'POST',
      headers: {
        ...bearer(invoice.customerSubject, ['customer']),
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ operationId: 'op-1', outcome: 'Approved' }),
    };
    const first = await call(`/api/v1/billing/invoices/${invoice.id}/payments`, init);
    const replay = await call(`/api/v1/billing/invoices/${invoice.id}/payments`, init);
    expect(first.status).toBe(200);
    expect(replay.status).toBe(200);
    expect(invoice.status).toBe('Paid');
  });
});
