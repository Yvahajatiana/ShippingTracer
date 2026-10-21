import http from 'node:http';
import net from 'node:net';
import { spawn } from 'node:child_process';
import { once } from 'node:events';

const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
let cancelledUpstream = false;
const upstream = http.createServer((req, res) => {
  req.on('error', () => {});
  res.on('error', () => {});
  if (req.url.includes('/cancel')) {
    const timer = setInterval(() => res.write(Buffer.alloc(2048, 42)), 20);
    res.on('close', () => { clearInterval(timer); cancelledUpstream = !res.writableEnded; });
    return;
  }
  if (req.url.includes('/binary')) {
    res.setHeader('Content-Type', 'application/octet-stream');
    res.end(Buffer.alloc(65536, 42));
    return;
  }
  req.resume();
  req.on('end', () => res.end('upstream'));
});
upstream.listen(0, '127.0.0.1');
await once(upstream, 'listening');
const reserve = http.createServer();
reserve.listen(0, '127.0.0.1');
await once(reserve, 'listening');
const port = reserve.address().port;
await new Promise(resolve => reserve.close(resolve));
const env = { ...process.env, ASPNETCORE_URLS: `http://127.0.0.1:${port}`, ASPNETCORE_ENVIRONMENT: 'Testing', Gateway__MaximumRequestBodyBytes: '1024', Keycloak__Authority: 'https://issuer.gateway.test' };
for (const service of ['Customer', 'Shipment', 'Tracking', 'Warehouse', 'Delivery', 'Billing', 'Notification', 'Reporting'])
  env[`Gateway__Destinations__${service}`] = `http://127.0.0.1:${upstream.address().port}/`;
// Run from the repository root after building the gateway; override for a local SDK installation.
const app = spawn(process.env.PARCELFLOW_DOTNET ?? 'dotnet', ['src/Gateway/ParcelFlow.Gateway/bin/Debug/net8.0/ParcelFlow.Gateway.dll'], { env, windowsHide: true });
let logs = '';
let launchError;
app.on('error', error => { launchError = error; });
app.stdout.on('data', chunk => logs += chunk);
app.stderr.on('data', chunk => logs += chunk);
const request = (path, chunks = null) => new Promise((resolve, reject) => {
  const req = http.request({ host: '127.0.0.1', port, path, method: 'GET', headers: chunks ? { 'Transfer-Encoding': 'chunked' } : {} }, res => {
    const bytes = [];
    res.on('data', chunk => bytes.push(chunk));
    res.on('end', () => resolve({ status: res.statusCode, type: res.headers['content-type'], bytes: Buffer.concat(bytes) }));
  });
  req.on('error', reject);
  if (chunks) for (const chunk of chunks) req.write(chunk);
  req.end();
});
try {
  let ready = false;
  for (let attempt = 0; attempt < 80; attempt++) {
    if (launchError) throw launchError;
    try { ready = (await request('/health/live')).status === 200; } catch {}
    if (ready) break;
    await pause(250);
  }
  if (!ready) throw new Error('Gateway did not start');
  const rawStatus = body => new Promise((resolve, reject) => {
    const socket = net.createConnection({ host: '127.0.0.1', port });
    let raw = '';
    socket.setTimeout(15000, () => { socket.destroy(); reject(new Error('Raw request timed out')); });
    socket.on('error', reject);
    socket.on('connect', () => socket.write('GET /api/v1/tracking/body HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\nTransfer-Encoding: chunked\r\n\r\n' + body));
    socket.on('data', bytes => raw += bytes);
    socket.on('end', () => resolve(raw));
  });
  const malformed = await rawStatus('invalid-size\r\n');
  if (!malformed.startsWith('HTTP/1.1 400 ') || !malformed.toLowerCase().includes('application/problem+json'))
    throw new Error(`Malformed framing expected JSON400, got ${malformed.slice(0, 200)}`);
  const slow = await rawStatus('100\r\na');
  if (!slow.startsWith('HTTP/1.1 408 ') || !slow.toLowerCase().includes('application/problem+json'))
    throw new Error(`Slow body expected JSON408, got ${slow.slice(0, 200)}`);
  const oversized = await request('/api/v1/tracking/body', [Buffer.alloc(700), Buffer.alloc(700)]);
  if (oversized.status !== 413 || !oversized.type?.startsWith('application/problem+json'))
    throw new Error(`Chunked body expected JSON413, got ${oversized.status} ${oversized.type}`);
  const binary = await request('/api/v1/tracking/binary');
  if (binary.status !== 200 || binary.bytes.length !== 65536 || binary.bytes.some(byte => byte !== 42))
    throw new Error('Binary stream changed');
  await new Promise((resolve, reject) => {
    const req = http.get({ host: '127.0.0.1', port, path: '/api/v1/tracking/cancel' }, res => {
      res.once('data', () => { req.destroy(); resolve(); });
    });
    req.on('error', error => { if (error.code !== 'ECONNRESET') reject(error); });
  });
  for (let attempt = 0; attempt < 40 && !cancelledUpstream; attempt++) await pause(50);
  if (!cancelledUpstream) throw new Error('Client cancellation did not close upstream');
  console.log('PASS: real Kestrel malformed/slow/oversized bodies JSON400/408/413, 64KiB binary fidelity, cancellation propagated upstream');
} catch (error) {
  console.error(logs);
  throw error;
} finally {
  if (app.pid && app.exitCode === null && app.signalCode === null) {
    app.kill();
    await once(app, 'exit');
  }
  upstream.closeAllConnections();
  await new Promise(resolve => upstream.close(resolve));
}
