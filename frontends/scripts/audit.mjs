// Runs `npm audit` and fails on high or critical advisories, except the ones
// listed in audit-allowlist.json. An allowlist entry stops applying after its
// expiry date so that every exception gets reviewed again.
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';

const blockingSeverities = new Set(['high', 'critical']);
const today = new Date().toISOString().slice(0, 10);

const allowlist = JSON.parse(
  readFileSync(new URL('../audit-allowlist.json', import.meta.url), 'utf8'),
);
const allowed = new Map(
  allowlist.filter((entry) => entry.expires >= today).map((entry) => [entry.id, entry]),
);
for (const entry of allowlist.filter((entry) => entry.expires < today)) {
  console.log(`Allowlist entry ${entry.id} expired on ${entry.expires}, it no longer applies.`);
}

let output;
try {
  // Reuse the npm CLI that launched this script so no shell is needed on Windows.
  const npm = process.env.npm_execpath
    ? [process.execPath, [process.env.npm_execpath]]
    : ['npm', []];
  output = execFileSync(npm[0], [...npm[1], 'audit', '--json'], {
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
  });
} catch (error) {
  // npm audit exits with 1 when it finds vulnerabilities; the JSON is still on stdout.
  output = error.stdout;
}
const report = JSON.parse(output);
if (report.error) {
  console.error(`npm audit failed: ${report.error.summary}`);
  process.exit(1);
}

const blocking = new Map();
const ignored = new Set();
for (const vulnerability of Object.values(report.vulnerabilities ?? {})) {
  for (const advisory of vulnerability.via) {
    if (typeof advisory !== 'object' || !blockingSeverities.has(advisory.severity)) continue;
    const id = advisory.url.split('/').pop();
    if (allowed.has(id)) ignored.add(id);
    else blocking.set(id, `${advisory.severity} ${id} ${advisory.name}: ${advisory.title}`);
  }
}

for (const id of ignored)
  console.log(`Allowed until ${allowed.get(id).expires}: ${id} (${allowed.get(id).package})`);
if (blocking.size > 0) {
  console.error('Blocking npm advisories:');
  for (const line of blocking.values()) console.error(`  ${line}`);
  process.exit(1);
}
console.log('No blocking npm advisory.');
