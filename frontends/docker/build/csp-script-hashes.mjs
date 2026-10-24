// Computes the CSP source list for the inline scripts the Angular build writes into index.html.
//
// With critical CSS inlining (production default), Angular/Beasties appends a small inline
// <script> that switches the deferred stylesheet from media="print" to media="all". Its hash is
// computed here, at image build time, so `script-src` can stay strict without 'unsafe-inline'.
//
// Usage: node csp-script-hashes.mjs <index.html> <output-file>
// The output file receives a space-separated list such as "'sha256-…'" (empty when no inline script).
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';

const [indexPath, outputPath] = process.argv.slice(2);
if (!indexPath || !outputPath) {
  console.error('Usage: node csp-script-hashes.mjs <index.html> <output-file>');
  process.exit(2);
}

const html = readFileSync(indexPath, 'utf8');

// Inline event handler attributes would need 'unsafe-hashes'; refuse them instead of weakening the CSP.
const handler = html.match(/<[a-z][^>]*\son[a-z]+\s*=/i);
if (handler) {
  console.error(`Inline event handler found in ${indexPath}: ${handler[0]}`);
  process.exit(1);
}

const hashes = [];
for (const match of html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi)) {
  const [, attributes, body] = match;
  if (/\ssrc\s*=/i.test(attributes) || body.length === 0) {
    continue;
  }
  const digest = createHash('sha256').update(body, 'utf8').digest('base64');
  hashes.push(`'sha256-${digest}'`);
}

const sources = [...new Set(hashes)].join(' ');
writeFileSync(outputPath, sources);
console.log(`Inline script hashes for CSP: ${sources || '(none)'}`);
