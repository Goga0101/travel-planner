// Builds frontend/travel-planner-web/public/airlines.json from the Travelpayouts static data (free, no key): airline names by
// IATA code, so the site can say "Georgian Airways" where flight data only has "A9". Loaded by the browser on first use.
//
// Usage: node scripts/fetch-airlines.mjs

import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const outPath = join(root, 'frontend/travel-planner-web/public/airlines.json');

const res = await fetch('https://api.travelpayouts.com/data/en/airlines.json');
if (!res.ok) throw new Error(`airlines.json: HTTP ${res.status}`);
const airlines = await res.json();

const names = {};
const lowcost = [];
for (const a of airlines) {
  const code = a.code?.trim().toUpperCase();
  const name = (a.name_translations?.en ?? a.name)?.trim();
  if (!code || !/^[A-Z0-9]{2}$/.test(code) || !name || names[code]) continue;
  names[code] = name;
  if (a.is_lowcost) lowcost.push(code);
}

const sorted = Object.fromEntries(Object.entries(names).sort(([a], [b]) => a.localeCompare(b)));
writeFileSync(outPath, JSON.stringify({ generatedOn: new Date().toISOString().slice(0, 10), names: sorted, lowcost: lowcost.sort() }));
console.log(`${Object.keys(sorted).length} airlines (${lowcost.length} low-cost) → ${outPath}`);
