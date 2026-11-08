// End-to-end business journey against the assembled docker-compose stack (issue #75).
// Every business call goes through the gateway with real Keycloak tokens obtained by the
// authorization code + PKCE flow of the portal clients. Node.js built-ins only.
//
// Usage (repository root, stack started with `docker compose up -d --build --wait`):
//   node tests/e2e/business-journey.mjs
//
// Settings come from the environment, then from `.env` at the repository root:
//   GATEWAY_URL               gateway base URL (default http://localhost:5100)
//   KEYCLOAK_URL              public Keycloak URL (default http://localhost:${KEYCLOAK_PORT:-8080})
//   KEYCLOAK_ADMIN_PASSWORD   bootstrap administrator, used to create the throwaway customer account
//   DEMO_USER_PASSWORD        password of the seeded demo accounts (operator camille.martin)
//   E2E_TIMEOUT_SECONDS       bound of every asynchronous wait (default 90)
//   E2E_KEEP_USER             set to 1 to keep the throwaway Keycloak customer account
import { createHash, randomBytes, randomUUID } from 'node:crypto';
import { readFileSync, existsSync } from 'node:fs';
import { deflateSync } from 'node:zlib';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');

function readDotEnv(file) {
  if (!existsSync(file)) return {};
  const values = {};
  for (const line of readFileSync(file, 'utf8').split(/\r?\n/)) {
    const match = /^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*?)\s*$/.exec(line);
    if (!match || line.trimStart().startsWith('#')) continue;
    values[match[1]] = match[2].replace(/^(['"])(.*)\1$/, '$2');
  }
  return values;
}

const dotEnv = readDotEnv(path.join(root, '.env'));
const setting = (name, fallback) => process.env[name] ?? dotEnv[name] ?? fallback;
const config = {
  gateway: setting('GATEWAY_URL', 'http://localhost:5100').replace(/\/+$/, ''),
  keycloak: setting('KEYCLOAK_URL', `http://localhost:${setting('KEYCLOAK_PORT', '8080')}`).replace(/\/+$/, ''),
  realm: 'parcelflow',
  adminPassword: setting('KEYCLOAK_ADMIN_PASSWORD'),
  demoPassword: setting('DEMO_USER_PASSWORD'),
  timeoutMs: Number(setting('E2E_TIMEOUT_SECONDS', '90')) * 1000,
  keepUser: setting('E2E_KEEP_USER', '0') === '1',
};
for (const [name, value] of [['KEYCLOAK_ADMIN_PASSWORD', config.adminPassword], ['DEMO_USER_PASSWORD', config.demoPassword]])
  if (!value) { console.error(`Missing ${name} (environment or .env)`); process.exit(2); }

// One correlation identifier for the whole run makes the service logs easy to filter.
const correlationId = randomUUID();
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
const base64Url = buffer => buffer.toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
const today = () => new Date().toISOString().slice(0, 10);
const addDays = (isoDate, days) => new Date(Date.parse(`${isoDate}T00:00:00Z`) + days * 86400000).toISOString().slice(0, 10);

class JourneyError extends Error {}
function check(condition, message, details) {
  if (!condition) throw new JourneyError(details === undefined ? message : `${message}\n    ${JSON.stringify(details)}`);
}

// Some enums are serialized by name and others by ordinal: compare on the name.
const enumName = (value, names) => typeof value === 'number' ? names[value] : value;
const SHIPMENT_STATUSES = ['AwaitingPayment', 'ReadyForPickup', 'PaymentFailed', 'PickedUp'];
const ZONES = ['Domestic', 'Europe', 'International'];

// ---------------------------------------------------------------------------------------------
// Keycloak: authorization code + PKCE login through the real login form.
// ---------------------------------------------------------------------------------------------

class CookieJar {
  #cookies = new Map();
  store(response) {
    for (const header of response.headers.getSetCookie()) {
      const [pair, ...attributes] = header.split(';');
      const index = pair.indexOf('=');
      const name = pair.slice(0, index).trim();
      const expired = attributes.some(attribute => /^\s*max-age=0\s*$/i.test(attribute));
      if (expired) this.#cookies.delete(name); else this.#cookies.set(name, pair.slice(index + 1).trim());
    }
  }
  header() { return [...this.#cookies].map(([name, value]) => `${name}=${value}`).join('; '); }
}

const decodeHtml = value => value.replace(/&amp;/g, '&').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>');

async function postForm(url, fields, headers = {}) {
  return fetch(url, {
    method: 'POST', redirect: 'manual',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded', ...headers },
    body: new URLSearchParams(fields),
  });
}

class TokenSession {
  constructor(label, clientId, tokens) {
    this.label = label;
    this.clientId = clientId;
    this.update(tokens);
  }
  update(tokens) {
    this.tokens = tokens;
    this.expiresAt = Date.now() + tokens.expires_in * 1000;
    this.claims = JSON.parse(Buffer.from(tokens.access_token.split('.')[1], 'base64url').toString('utf8'));
  }
  // Access tokens live five minutes: refresh them when they are about to expire.
  async accessToken() {
    if (this.expiresAt - Date.now() < 30000) {
      const response = await postForm(`${config.keycloak}/realms/${config.realm}/protocol/openid-connect/token`,
        { grant_type: 'refresh_token', client_id: this.clientId, refresh_token: this.tokens.refresh_token });
      check(response.status === 200, `${this.label}: token refresh failed with HTTP ${response.status}`, await response.text());
      this.update(await response.json());
    }
    return this.tokens.access_token;
  }
}

async function loginWithPkce(label, clientId, redirectUri, username, password) {
  const jar = new CookieJar();
  const verifier = base64Url(randomBytes(32));
  const challenge = base64Url(createHash('sha256').update(verifier).digest());
  const state = base64Url(randomBytes(16));
  const authorize = new URL(`${config.keycloak}/realms/${config.realm}/protocol/openid-connect/auth`);
  authorize.search = new URLSearchParams({
    client_id: clientId, redirect_uri: redirectUri, response_type: 'code', scope: 'openid',
    state, nonce: base64Url(randomBytes(16)), code_challenge: challenge, code_challenge_method: 'S256',
  }).toString();
  const loginPage = await fetch(authorize, { redirect: 'manual' });
  jar.store(loginPage);
  const html = await loginPage.text();
  check(loginPage.status === 200, `${label}: login page answered HTTP ${loginPage.status}`, html.slice(0, 300));
  const form = /<form[^>]*id="kc-form-login"[^>]*>/i.exec(html)?.[0];
  const action = form && /action="([^"]+)"/i.exec(form)?.[1];
  check(action, `${label}: Keycloak login form not found`);
  const submitted = await postForm(decodeHtml(action), { username, password, credentialId: '' }, { Cookie: jar.header() });
  const location = submitted.headers.get('location') ?? '';
  if (submitted.status !== 302 || !location.startsWith(redirectUri)) {
    const body = await submitted.text();
    const feedback = /id="input-error[^"]*"[^>]*>([^<]+)</i.exec(body)?.[1] ?? /kc-feedback-text">([^<]+)</i.exec(body)?.[1];
    throw new JourneyError(`${label}: login refused (HTTP ${submitted.status}) ${feedback?.trim() ?? location}`);
  }
  const callback = new URL(location);
  check(callback.searchParams.get('state') === state, `${label}: OAuth state mismatch`);
  const code = callback.searchParams.get('code');
  check(code, `${label}: no authorization code in the redirect`, location);
  const tokenResponse = await postForm(`${config.keycloak}/realms/${config.realm}/protocol/openid-connect/token`,
    { grant_type: 'authorization_code', client_id: clientId, code, redirect_uri: redirectUri, code_verifier: verifier });
  check(tokenResponse.status === 200, `${label}: code exchange failed with HTTP ${tokenResponse.status}`, await tokenResponse.clone().text());
  return new TokenSession(label, clientId, await tokenResponse.json());
}

// The throwaway customer account is created with the Keycloak admin REST API of the running
// container; the realm file is left untouched.
async function adminToken() {
  const response = await postForm(`${config.keycloak}/realms/master/protocol/openid-connect/token`,
    { grant_type: 'password', client_id: 'admin-cli', username: 'admin', password: config.adminPassword });
  check(response.status === 200, `Keycloak admin login failed with HTTP ${response.status}`, await response.clone().text());
  return (await response.json()).access_token;
}

async function keycloakAdmin(method, resource, body) {
  const response = await fetch(`${config.keycloak}/admin/realms/${config.realm}${resource}`, {
    method,
    headers: { Authorization: `Bearer ${await adminToken()}`, ...(body ? { 'Content-Type': 'application/json' } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await response.text();
  check(response.ok, `Keycloak admin ${method} ${resource} answered HTTP ${response.status}`, text.slice(0, 300));
  return { response, body: text ? JSON.parse(text) : undefined };
}

async function createCustomerAccount(suffix) {
  const username = `e2e-${suffix}`;
  const email = `${username}@example.test`;
  // Satisfies the realm policy: length, upper, lower, digit, special, not the username or email.
  const generatedPassword = `Journey-${randomBytes(6).toString('hex')}-Aa1!`;
  const { response } = await keycloakAdmin('POST', '/users', {
    username, email, firstName: 'Journey', lastName: `Tester ${suffix}`, enabled: true, emailVerified: true,
    requiredActions: [], credentials: [{ type: 'password', value: generatedPassword, temporary: false }],
  });
  const id = response.headers.get('location').split('/').pop();
  const { body: role } = await keycloakAdmin('GET', '/roles/customer');
  await keycloakAdmin('POST', `/users/${id}/role-mappings/realm`, [role]);
  return { id, username, email, password: generatedPassword };
}

// ---------------------------------------------------------------------------------------------
// Gateway client.
// ---------------------------------------------------------------------------------------------

async function call(session, method, resource, { json, accept = 'application/json' } = {}) {
  for (let attempt = 0; ; attempt++) {
    const headers = { Accept: accept, 'X-Correlation-Id': correlationId };
    if (session) headers.Authorization = `Bearer ${await session.accessToken()}`;
    if (json !== undefined) headers['Content-Type'] = 'application/json';
    const response = await fetch(`${config.gateway}${resource}`, { method, headers, body: json === undefined ? undefined : JSON.stringify(json) });
    // The gateway rejects throttled requests before forwarding them: they are safe to resend.
    if (response.status === 429 && attempt < 5) {
      await pause(Math.min(Number(response.headers.get('retry-after') ?? '2'), 30) * 1000);
      continue;
    }
    const type = response.headers.get('content-type') ?? '';
    const bytes = Buffer.from(await response.arrayBuffer());
    let body = bytes;
    if (type.includes('json')) body = bytes.length ? JSON.parse(bytes.toString('utf8')) : undefined;
    else if (type.startsWith('text/')) body = bytes.toString('utf8');
    return { status: response.status, headers: response.headers, body, bytes };
  }
}

async function expectCall(session, method, resource, expected, options) {
  const result = await call(session, method, resource, options);
  const accepted = Array.isArray(expected) ? expected : [expected];
  check(accepted.includes(result.status), `${method} ${resource}: expected HTTP ${accepted.join('/')}, got ${result.status}`,
    Buffer.isBuffer(result.body) ? result.bytes.toString('utf8', 0, 300) : result.body);
  return result.body;
}

// Bounded polling: `probe` returns a value when the condition holds, undefined otherwise.
async function waitFor(description, probe, { intervalMs = 1500 } = {}) {
  const deadline = Date.now() + config.timeoutMs;
  let last;
  for (;;) {
    try {
      const value = await probe(observed => { last = observed; });
      if (value !== undefined) return value;
    } catch (error) {
      if (!(error instanceof JourneyError)) throw error;
      last = error.message;
    }
    if (Date.now() > deadline)
      throw new JourneyError(`Timed out after ${config.timeoutMs / 1000}s waiting for ${description}\n    last observation: ${JSON.stringify(last)?.slice(0, 600)}`);
    await pause(intervalMs);
  }
}

// ---------------------------------------------------------------------------------------------
// Delivery proof: a minimal valid 1x1 PNG (chunks and CRC are checked by Delivery).
// ---------------------------------------------------------------------------------------------

const crcTable = Array.from({ length: 256 }, (_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  return c >>> 0;
});
const crc32 = buffer => {
  let crc = 0xffffffff;
  for (const byte of buffer) crc = crcTable[(crc ^ byte) & 0xff] ^ (crc >>> 8);
  return (crc ^ 0xffffffff) >>> 0;
};
function pngChunk(type, data) {
  const length = Buffer.alloc(4); length.writeUInt32BE(data.length);
  const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(body));
  return Buffer.concat([length, body, crc]);
}
function signaturePng() {
  const header = Buffer.alloc(13);
  header.writeUInt32BE(1, 0); header.writeUInt32BE(1, 4);
  header[8] = 8; header[9] = 6; // 8-bit RGBA, default compression, filter and interlace
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    pngChunk('IHDR', header),
    pngChunk('IDAT', deflateSync(Buffer.from([0, 20, 40, 160, 255]))),
    pngChunk('IEND', Buffer.alloc(0)),
  ]);
}

// ---------------------------------------------------------------------------------------------
// Journey.
// ---------------------------------------------------------------------------------------------

const steps = [];
let stepNumber = 0;
async function step(title, action) {
  stepNumber++;
  const started = Date.now();
  process.stdout.write(`[${String(stepNumber).padStart(2, '0')}] ${title} ... `);
  try {
    const detail = await action();
    const elapsed = ((Date.now() - started) / 1000).toFixed(1);
    console.log(`ok (${elapsed}s)${detail ? ` - ${detail}` : ''}`);
    steps.push({ title, ok: true });
  } catch (error) {
    console.log('FAILED');
    steps.push({ title, ok: false });
    throw error;
  }
}

const suffix = `${Date.now().toString(36)}${randomBytes(2).toString('hex')}`;
const journey = {};
let account;

async function run() {
  console.log(`ParcelFlow business journey - gateway ${config.gateway}, Keycloak ${config.keycloak}`);
  console.log(`Correlation id ${correlationId}, run ${suffix}\n`);

  // 1. Identities -------------------------------------------------------------------------------
  let customer, operator;
  await step('Keycloak: create a throwaway customer account (admin REST API)', async () => {
    account = await createCustomerAccount(suffix);
    return account.username;
  });
  await step('Keycloak: customer login on parcelflow-customer (code + PKCE)', async () => {
    customer = await loginWithPkce('customer', 'parcelflow-customer', 'http://localhost:4201/', account.username, account.password);
    check(customer.claims.sub === account.id, 'Customer token subject differs from the Keycloak user id', customer.claims.sub);
    check(customer.claims.realm_access?.roles?.includes('customer'), 'Customer token lacks the customer role', customer.claims.realm_access);
    return `aud ${[].concat(customer.claims.aud).join(',')}`;
  });
  await step('Keycloak: operator/driver camille.martin login on parcelflow-operations (code + PKCE)', async () => {
    operator = await loginWithPkce('operator', 'parcelflow-operations', 'http://localhost:4202/', 'camille.martin', config.demoPassword);
    check(operator.claims.realm_access?.roles?.includes('operator'), 'Operator token lacks the operator role', operator.claims.realm_access);
    return `sub ${operator.claims.sub}`;
  });
  await step('Gateway: anonymous and wrong-audience requests are refused', async () => {
    await expectCall(null, 'GET', '/api/v1/shipments', 401);
    await expectCall(customer, 'GET', `/api/v1/reports/kpis?from=${today()}&to=${addDays(today(), 1)}`, [401, 403]);
    await expectCall(customer, 'GET', '/api/v1/delivery/parcels/available', [401, 403]);
    await expectCall(operator, 'GET', '/api/v1/billing/invoices', [401, 403]);
  });

  // 2. Customer profile, quote, shipment, label -----------------------------------------------------
  await step('Customer: create the profile and an address', async () => {
    await expectCall(customer, 'GET', '/api/v1/customers/me', 404);
    const profile = await expectCall(customer, 'POST', '/api/v1/customers', 201, {
      json: { firstName: 'Journey', lastName: `Tester ${suffix}`, email: account.email, phone: '+33 4 72 00 00 00' },
    });
    check(profile.keycloakSubject === account.id, 'Profile subject is not the token subject', profile);
    const address = await expectCall(customer, 'POST', `/api/v1/customers/${profile.id}/addresses`, 201, {
      json: { label: 'Home', line1: '12 rue de la République', line2: null, postalCode: '69002', city: 'Lyon', countryCode: 'FR' },
    });
    const me = await expectCall(customer, 'GET', '/api/v1/customers/me', 200);
    check(me.id === profile.id && me.addresses.some(item => item.id === address.id), 'GET /customers/me does not return the new profile and address', me);
    journey.profile = me;
    return `profile ${profile.id}`;
  });

  const shipmentBody = () => {
    const home = journey.profile.addresses[0];
    return {
      sender: {
        name: `${journey.profile.firstName} ${journey.profile.lastName}`, email: journey.profile.email, phone: journey.profile.phone,
        line1: home.line1, line2: home.line2, postalCode: home.postalCode, city: home.city, countryCode: home.countryCode,
      },
      recipient: {
        name: 'Recipient Journey', email: `recipient-${suffix}@example.test`, phone: '+33 1 40 00 00 00',
        line1: '8 boulevard Haussmann', line2: null, postalCode: '75009', city: 'Paris', countryCode: 'FR',
      },
      parcel: { weightKg: 2.5, lengthCm: 30, widthCm: 20, heightCm: 10 },
    };
  };

  await step('Customer: quote a domestic parcel', async () => {
    const tariffs = await expectCall(customer, 'GET', '/api/v1/tariffs', 200);
    const domestic = tariffs.find(item => enumName(item.zone, ZONES) === 'Domestic');
    check(domestic, 'No Domestic tariff', tariffs);
    const quote = await expectCall(customer, 'POST', '/api/v1/shipments/quote', 200, { json: shipmentBody() });
    // Billable weight: max(2.5, 30*20*10/5000 = 1.2) = 2.5 kg, charged per started kilogram.
    const expected = Math.round((domestic.basePrice + 3 * domestic.pricePerKg) * 100) / 100;
    check(enumName(quote.zone, ZONES) === 'Domestic' && quote.price === expected && quote.currency === 'EUR',
      `Unexpected quote (expected ${expected} EUR Domestic)`, quote);
    journey.quote = quote;
    return `${quote.price} ${quote.currency}`;
  });

  await step('Customer: create the shipment', async () => {
    const shipment = await expectCall(customer, 'POST', '/api/v1/shipments', 201, { json: shipmentBody() });
    check(/^PF[0-9A-F]{24}$/.test(shipment.trackingNumber), 'Malformed tracking number', shipment.trackingNumber);
    check(shipment.ownerSubject === account.id, 'Shipment owner is not the customer', shipment);
    check(shipment.price === journey.quote.price, 'Shipment price differs from the quote', shipment);
    check(enumName(shipment.status, SHIPMENT_STATUSES) === 'AwaitingPayment', 'New shipment is not AwaitingPayment', shipment);
    journey.shipment = shipment;
    return `${shipment.trackingNumber} (${shipment.id})`;
  });

  await step('Customer: download the PDF label', async () => {
    const result = await call(customer, 'GET', `/api/v1/shipments/${journey.shipment.id}/label`, { accept: 'application/pdf' });
    check(result.status === 200, `Label answered HTTP ${result.status}`);
    check(result.headers.get('content-type')?.startsWith('application/pdf'), 'Label is not a PDF', result.headers.get('content-type'));
    check(result.bytes.subarray(0, 5).toString('latin1') === '%PDF-' && result.bytes.includes('%%EOF'), 'Label body is not a complete PDF document');
    check(result.headers.get('content-disposition')?.includes(`${journey.shipment.trackingNumber}.pdf`), 'Unexpected label file name', result.headers.get('content-disposition'));
    return `${result.bytes.length} bytes`;
  });

  // 3. Billing and saga -------------------------------------------------------------------------------
  const shipmentId = () => journey.shipment.id;
  await step('Billing: invoice issued by the saga (PaymentRequested)', async () => {
    const invoice = await waitFor('the PendingPayment invoice', async observe => {
      const result = await call(customer, 'GET', `/api/v1/billing/invoices/${shipmentId()}`);
      observe(result.status === 200 ? result.body : result.status);
      return result.status === 200 ? result.body : undefined;
    });
    check(invoice.status === 'PendingPayment' && invoice.amount === journey.shipment.price && invoice.currency === 'EUR'
      && invoice.customerSubject === account.id, 'Unexpected invoice', invoice);
    journey.invoice = invoice;
    return `${invoice.number} ${invoice.amount} ${invoice.currency}`;
  });

  await step('Billing: simulated payment Approved', async () => {
    const operationId = randomUUID();
    const receipt = await expectCall(customer, 'POST', `/api/v1/billing/invoices/${shipmentId()}/payments`, 200,
      { json: { operationId, outcome: 'Approved' } });
    check(receipt.outcome === 'Approved' && receipt.amount === journey.invoice.amount, 'Unexpected payment receipt', receipt);
    const replay = await expectCall(customer, 'POST', `/api/v1/billing/invoices/${shipmentId()}/payments`, 200,
      { json: { operationId, outcome: 'Approved' } });
    check(replay.occurredAtUtc === receipt.occurredAtUtc, 'Payment replay did not return the original receipt', replay);
    const invoice = await expectCall(customer, 'GET', `/api/v1/billing/invoices/${shipmentId()}`, 200);
    check(invoice.status === 'Paid', 'Invoice is not Paid after the payment', invoice);
    await expectCall(customer, 'GET', `/api/v1/billing/invoices/${shipmentId()}/payment`, 200);
    return `operation ${operationId}`;
  });

  await step('Shipment: saga moves the shipment to ReadyForPickup', async () => {
    const shipment = await waitFor('shipment status ReadyForPickup', async observe => {
      const value = await expectCall(customer, 'GET', `/api/v1/shipments/${shipmentId()}`, 200);
      observe(value.status);
      return enumName(value.status, SHIPMENT_STATUSES) === 'ReadyForPickup' ? value : undefined;
    });
    return `status ${enumName(shipment.status, SHIPMENT_STATUSES)} (serialized as ${JSON.stringify(shipment.status)})`;
  });

  // 4. Operations: warehouse scans, route, assignment, proof of delivery --------------------------------
  let lyon, paris;
  await step('Warehouse: locate the seeded sites FR-LYON-01 and FR-PARIS-01', async () => {
    const page = await expectCall(operator, 'GET', '/api/v1/warehouses?page=1&pageSize=100', 200);
    lyon = page.items.find(item => item.code === 'FR-LYON-01');
    paris = page.items.find(item => item.code === 'FR-PARIS-01');
    check(lyon?.isActive && lyon.kind === 'Warehouse', 'Seeded warehouse FR-LYON-01 missing or inactive', lyon);
    check(paris?.isActive && paris.kind === 'SortingCenter', 'Seeded sorting center FR-PARIS-01 missing or inactive', paris);
  });

  const scan = async (site, direction) => {
    const scanId = randomUUID();
    const body = { scanId, warehouseId: site.id, trackingNumber: journey.shipment.trackingNumber, direction };
    // 409 until Warehouse has consumed ShipmentReadyForPickup: the same scan id is retried.
    return waitFor(`the ${direction} scan at ${site.code}`, async observe => {
      const result = await call(operator, 'POST', '/api/v1/warehouses/scans', { json: body });
      observe({ status: result.status, body: result.body });
      if (result.status === 200) return result.body;
      check(result.status === 409, `Scan ${direction} at ${site.code} answered HTTP ${result.status}`, result.body);
      return undefined;
    });
  };
  const inStock = async site => {
    const stock = await expectCall(operator, 'GET', `/api/v1/warehouses/${site.id}/stock?page=1&pageSize=100`, 200);
    return stock.items.some(item => item.shipmentId === shipmentId());
  };

  await step('Warehouse: entry scan at FR-LYON-01 (pick-up)', async () => {
    const receipt = await scan(lyon, 'Entry');
    check(receipt.shipmentId === shipmentId(), 'Scan receipt refers to another shipment', receipt);
    check(await inStock(lyon), 'Parcel missing from the FR-LYON-01 stock');
    const shipment = await waitFor('shipment status PickedUp', async observe => {
      const value = await expectCall(customer, 'GET', `/api/v1/shipments/${shipmentId()}`, 200);
      observe(value.status);
      return enumName(value.status, SHIPMENT_STATUSES) === 'PickedUp' ? value : undefined;
    });
    return `shipment ${enumName(shipment.status, SHIPMENT_STATUSES)} (serialized as ${JSON.stringify(shipment.status)})`;
  });
  await step('Warehouse: exit scan at FR-LYON-01', async () => {
    await scan(lyon, 'Exit');
    check(!(await inStock(lyon)), 'Parcel still in the FR-LYON-01 stock after the exit scan');
  });
  await step('Warehouse: entry then exit scans at sorting center FR-PARIS-01', async () => {
    await scan(paris, 'Entry');
    check(await inStock(paris), 'Parcel missing from the FR-PARIS-01 stock');
    await scan(paris, 'Exit');
    check(!(await inStock(paris)), 'Parcel still in the FR-PARIS-01 stock after the exit scan');
  });

  const driverSubject = () => operator.claims.sub;
  await step('Delivery: parcel available for assignment', async () => {
    const drivers = await expectCall(operator, 'GET', '/api/v1/delivery/drivers?page=1&pageSize=100', 200);
    check(drivers.items.some(item => item.subject === driverSubject() && item.isActive), `No active driver profile for ${driverSubject()}`, drivers.items);
    await waitFor('the parcel in /delivery/parcels/available', async observe => {
      for (let page = 1; ; page++) {
        const result = await expectCall(operator, 'GET', `/api/v1/delivery/parcels/available?page=${page}&pageSize=100`, 200);
        observe({ total: result.total });
        if (result.items.some(item => item.shipmentId === shipmentId() && item.trackingNumber === journey.shipment.trackingNumber)) return true;
        if (page * result.pageSize >= result.total) return undefined;
      }
    });
  });

  await step('Delivery: create a route, assign the driver and the parcel', async () => {
    const route = await expectCall(operator, 'POST', '/api/v1/delivery/routes', 201, { json: { name: `E2E ${suffix}`, serviceDate: today() } });
    check(route.status === 'Planned', 'New route is not Planned', route);
    await expectCall(operator, 'PUT', `/api/v1/delivery/routes/${route.id}/driver`, 200, { json: { driverSubject: driverSubject() } });
    await expectCall(operator, 'POST', `/api/v1/delivery/routes/${route.id}/stops`, 200, { json: { shipmentId: shipmentId() } });
    const planned = await expectCall(operator, 'GET', `/api/v1/delivery/routes/${route.id}`, 200);
    check(planned.driverSubject === driverSubject() && planned.stops.length === 1 && planned.stops[0].shipmentId === shipmentId(),
      'Route does not hold the driver and the stop', planned);
    journey.route = planned;
    return `route ${route.id}`;
  });

  await step('Delivery: driver starts the route (out for delivery)', async () => {
    const started = await expectCall(operator, 'POST', `/api/v1/delivery/routes/${journey.route.id}/start`, 200);
    check(started.status === 'InProgress', 'Route is not InProgress after the start', started);
  });

  await step('Delivery: proof of delivery with recipient name and signature', async () => {
    const png = signaturePng();
    const attempt = await expectCall(operator, 'POST', `/api/v1/delivery/routes/${journey.route.id}/stops/${shipmentId()}/delivery`, 200, {
      json: { operationId: randomUUID(), recipientName: 'Recipient Journey', signaturePng: png.toString('base64'), photo: null },
    });
    check(attempt.outcome === 'Delivered' && attempt.hasSignature && !attempt.hasPhoto, 'Unexpected delivery attempt', attempt);
    journey.deliveredAt = attempt.occurredAtUtc;
    const proof = await expectCall(operator, 'GET', `/api/v1/delivery/routes/${journey.route.id}/stops/${shipmentId()}/proof`, 200);
    check(proof.recipientName === 'Recipient Journey' && proof.hasSignature, 'Unexpected proof receipt', proof);
    const image = await call(operator, 'GET', `/api/v1/delivery/routes/${journey.route.id}/stops/${shipmentId()}/proof/Signature`, { accept: 'image/png' });
    check(image.status === 200 && image.headers.get('content-type') === 'image/png' && image.bytes.equals(png), 'Signature download differs from the upload',
      { status: image.status, type: image.headers.get('content-type'), length: image.bytes.length });
    const route = await expectCall(operator, 'GET', `/api/v1/delivery/routes/${journey.route.id}`, 200);
    check(route.status === 'Completed' && route.stops[0].status === 'Delivered', 'Route is not Completed after its only delivery', route);
  });

  // 5. Public tracking ----------------------------------------------------------------------------
  const expectedHistory = ['Created', 'PickedUp', 'InTransit', 'AtSortingCenter', 'InTransit', 'OutForDelivery', 'Delivered'];
  await step('Tracking: anonymous public history up to Delivered', async () => {
    // Lower case and spaces are accepted and normalized by the public endpoint.
    const number = ` ${journey.shipment.trackingNumber.toLowerCase()} `;
    const tracking = await waitFor('public tracking status Delivered with the full history', async observe => {
      const value = await expectCall(null, 'GET', `/api/v1/tracking/${encodeURIComponent(number)}`, [200, 404]);
      observe(value?.history?.map(entry => entry.status) ?? value);
      return value?.currentStatus === 'Delivered' && value.history.length === expectedHistory.length ? value : undefined;
    }, { intervalMs: 3000 });
    const statuses = tracking.history.map(entry => entry.status);
    check(JSON.stringify(statuses) === JSON.stringify(expectedHistory), 'Unexpected public history', statuses);
    check(tracking.currentStatusLabel === 'Livré' && tracking.trackingNumber === journey.shipment.trackingNumber, 'Unexpected public tracking header', tracking);
    check(tracking.history.find(entry => entry.status === 'AtSortingCenter').facilityCode === 'FR-PARIS-01', 'AtSortingCenter is not attached to FR-PARIS-01', tracking.history);
    const raw = JSON.stringify(tracking);
    for (const secret of [shipmentId(), account.id, account.email, journey.profile.addresses[0].line1])
      check(!raw.includes(secret), 'Public tracking leaks private data', secret);
    const internal = await expectCall(operator, 'GET', `/api/v1/tracking/shipments/${shipmentId()}`, 200);
    check(internal.currentStatus === 'Delivered' && internal.trackingNumber === journey.shipment.trackingNumber, 'Unexpected internal tracking', internal);
    return statuses.join(' > ');
  });

  // 6. Notifications and reporting ---------------------------------------------------------------------
  // ShipmentCreated, InvoiceIssued, PaymentCompleted and the six tracking facts after Created.
  const expectedSources = 3 + expectedHistory.length - 1;
  await step('Notification: Email and Sms receipts recorded for every fact', async () => {
    const page = await waitFor(`${expectedSources * 2} notification receipts`, async observe => {
      const value = await expectCall(customer, 'GET', `/api/v1/notifications?shipmentId=${shipmentId()}&page=1&pageSize=100`, 200);
      observe({ total: value.total, subjects: value.items.map(item => item.subject) });
      return value.total >= expectedSources * 2 ? value : undefined;
    });
    const bySource = Map.groupBy(page.items, item => item.sourceEventId);
    check(bySource.size === expectedSources, `Expected ${expectedSources} notified facts, got ${bySource.size}`, page.items.map(item => item.subject));
    for (const [source, items] of bySource)
      check(items.map(item => item.channel).sort().join() === 'Email,Sms', `Fact ${source} lacks an Email or Sms receipt`, items);
    for (const item of page.items) {
      check(item.customerSubject === account.id && item.trackingNumber === journey.shipment.trackingNumber, 'Receipt for another owner or parcel', item);
      check(item.deliveryStatus === 'MockSent', 'Receipt is not MockSent', item);
      if (item.channel === 'Email') check(item.destination === account.email, 'Email receipt sent to an unexpected destination', item);
    }
    return `${page.total} receipts for ${bySource.size} facts`;
  });

  await step('Reporting: KPIs count the delivered shipment', async () => {
    const from = journey.shipment.createdAtUtc.slice(0, 10);
    const to = addDays(from, 1);
    // The KPI report is cached for at most thirty seconds: poll until it reflects the delivery.
    const report = await waitFor('KPIs including the delivered shipment', async observe => {
      const value = await expectCall(operator, 'GET', `/api/v1/reports/kpis?from=${from}&to=${to}`, 200);
      const delivered = value.statusVolumes.find(item => item.status === 'Delivered')?.shipments ?? 0;
      observe({ total: value.totalShipments, delivered: value.deliveredShipments, generatedAtUtc: value.generatedAtUtc });
      return value.deliveredShipments >= 1 && delivered === value.deliveredShipments && value.deliveryDurationSamples >= 1
        && value.transitDurationSamples >= 1 && Date.parse(value.generatedAtUtc) >= Date.parse(journey.deliveredAt) ? value : undefined;
    }, { intervalMs: 5000 });
    const day = report.dailyVolumes.find(item => item.date === from);
    check(day && day.shipments >= 1 && report.totalShipments >= report.deliveredShipments, 'Daily volume does not count the shipment', report.dailyVolumes);
    return `total ${report.totalShipments}, delivered ${report.deliveredShipments}, rate ${report.deliveryRatePercent}%`;
  });
}

async function cleanup() {
  if (!account || config.keepUser) return;
  try { await keycloakAdmin('DELETE', `/users/${account.id}`); }
  catch (error) { console.warn(`Warning: throwaway account ${account.username} not deleted: ${error.message}`); }
}

const started = Date.now();
let exitCode = 0;
try {
  await run();
  console.log(`\nJourney passed: ${steps.length} steps in ${((Date.now() - started) / 1000).toFixed(1)}s`);
} catch (error) {
  exitCode = 1;
  console.error(`\n${error instanceof JourneyError ? error.message : error.stack}`);
  if (error.cause) console.error(`Caused by: ${error.cause.message ?? error.cause}`);
  if (journey.shipment) console.error(`Shipment ${journey.shipment.id} / ${journey.shipment.trackingNumber}`);
  console.error(`Correlation id ${correlationId}`);
  console.error(`Journey failed at step ${stepNumber} after ${((Date.now() - started) / 1000).toFixed(1)}s`);
} finally {
  await cleanup();
}
process.exit(exitCode);
