// Builds frontend/travel-planner-web/public/lounges/lounges.json from the research files in scripts/lounges/research:
//   cards.json        – Georgian bank cards and the lounge programs they include (with sources)
//   airports-*.json   – per airport, the lounges / restaurants / spas that take Priority Pass, LoungeKey or DragonPass
// Every entry must carry an https source; anything without one, or with an unknown program, is dropped and reported.
// Usage: node scripts/lounges/build-lounges.mjs
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const research = path.join(here, 'research');
const out = path.join(here, '../../frontend/travel-planner-web/public/lounges/lounges.json');

const PROGRAMS = new Set(['PriorityPass', 'LoungeKey', 'DragonPass', 'MastercardTravelPass', 'VisaAirportCompanion', 'BankOwn', 'Other']);
const KINDS = new Set(['lounge', 'dining', 'spa', 'sleep']);
const dropped = [];
const https = (u) => typeof u === 'string' && /^https:\/\/[^\s]+$/.test(u.trim());
const text = (v) => (typeof v === 'string' && v.trim() ? v.trim() : null);
const read = (f) => JSON.parse(fs.readFileSync(path.join(research, f), 'utf8'));

const cardsFile = read('cards.json');

// Left out on purpose, until checked again:
// - TBC Concept Mastercard World Elite: its pages mention Fast Track and dining offers but no lounge entry.
// - the Mastercard Business Lounge at Tbilisi: its Mastercard page is partly outdated (Kyiv, cards from Russia…).
const SKIP_CARDS = new Set(['tbc-concept-mastercard-world-elite']);
const SKIP_LOUNGES = /^Mastercard Business Lounge/;
// "Other" in the card data = the Visa Business Lounge at Kutaisi (tap a Georgian Visa premium card at the entrance).
const OTHER = {
  name: 'Visa Business Lounge, Kutaisi',
  summary_en: 'The Visa Business Lounge at Kutaisi airport (with a TBC Concept space): tap a Visa Platinum, Signature or Infinite card issued in Georgia at the entrance terminal; each bank sets the yearly number of visits.',
  summary_ka: 'ქუთაისის აეროპორტის Visa-ს ბიზნეს ლაუნჯი (TBC Concept-ის სივრცით): შესასვლელთან ტერმინალზე მიადე საქართველოში გაცემული Visa Platinum, Signature ან Infinite ბარათი; წლიური ვიზიტების რაოდენობას თითოეული ბანკი ადგენს.',
  url: 'https://tbcconcept.ge/en/offers/kutaisis-aeroportis-upiratesobebi',
};
for (const pr of cardsFile.programs ?? []) if (pr.id === 'Other') Object.assign(pr, OTHER);
const programs = (cardsFile.programs ?? [])
  .filter((p) => PROGRAMS.has(p.id) && https(p.url))
  .map((p) => ({ id: p.id, name: p.name, summary: { ka: text(p.summary_ka) ?? text(p.summary_en) ?? '', en: text(p.summary_en) ?? '' }, url: p.url.trim() }));

const cards = [];
for (const c of cardsFile.cards ?? []) {
  if (SKIP_CARDS.has(c.id)) { dropped.push(`card ${c.card}: lounge entry not confirmed`); continue; }
  const progs = (c.programs ?? []).filter((p) => PROGRAMS.has(p.program));
  if (!https(c.source) || !progs.length) { dropped.push(`card ${c.card ?? c.id}: ${!https(c.source) ? 'no source' : 'no known program'}`); continue; }
  cards.push({
    id: c.id,
    country: c.country ?? 'GE',
    bank: { ka: text(c.bank_ka) ?? c.bank, en: c.bank },
    card: c.card,
    network: text(c.network) ?? '',
    programs: progs.map((p) => ({
      program: p.program,
      visitsPerYear: Number.isFinite(p.visits_per_year) ? p.visits_per_year : null,
      unlimited: p.unlimited === true,
      guest: text(p.guest),
      dining: typeof p.dining === 'boolean' ? p.dining : null,
    })),
    conditions: text(c.conditions_en) || text(c.conditions_ka) ? { ka: text(c.conditions_ka) ?? text(c.conditions_en), en: text(c.conditions_en) ?? text(c.conditions_ka) } : null,
    source: c.source.trim(),
  });
}

const airports = new Map();
for (const f of fs.readdirSync(research).filter((f) => /^airports-.*\.json$/.test(f)).sort()) {
  for (const a of read(f)) {
    const iata = String(a.iata ?? '').toUpperCase();
    if (!/^[A-Z]{3}$/.test(iata)) { dropped.push(`airport without IATA in ${f}`); continue; }
    const places = [];
    for (const p of a.places ?? []) {
      const progs = (p.programs ?? []).filter((x) => PROGRAMS.has(x));
      if (!https(p.source) || !progs.length || !text(p.name)) { dropped.push(`${iata} ${p.name ?? '?'}: ${!https(p.source) ? 'no source' : 'no program'}`); continue; }
      places.push({
        name: p.name.trim(),
        kind: KINDS.has(p.kind) ? p.kind : 'lounge',
        terminal: text(p.terminal),
        airside: typeof p.airside === 'boolean' ? p.airside : null,
        schengen: ['schengen', 'non-schengen', 'both'].includes(p.schengen) ? p.schengen : null,
        location: text(p.location),
        hours: text(p.hours),
        programs: progs,
        offer: text(p.offer),
        ...(text(p.bank) ? { bank: text(p.bank) } : {}),
        source: p.source.trim(),
      });
    }
    const prev = airports.get(iata);
    airports.set(iata, {
      iata,
      airport: prev?.airport ?? text(a.airport) ?? iata,
      city: prev?.city ?? text(a.city) ?? iata,
      country: prev?.country ?? text(a.country) ?? '',
      places: [...(prev?.places ?? []), ...places],
      notes: prev?.notes ?? text(a.notes),
    });
  }
}

// Bank-owned lounges at Tbilisi (open only to that bank's cards).
// The TBC Concept space at Tbilisi opens to TBC Concept cards; the Visa lounge at Kutaisi to Georgian Visa premium cards.
const NAMES = { TBS: ['Tbilisi International Airport', 'Tbilisi'], KUT: ['Kutaisi International Airport', 'Kutaisi'], BUS: ['Batumi International Airport', 'Batumi'] };
for (const l of cardsFile.tbilisi_bank_lounges ?? []) {
  if (SKIP_LOUNGES.test(l.name ?? '')) { dropped.push(`lounge ${l.name}: source partly outdated`); continue; }
  if (!https(l.source) || !text(l.name)) { dropped.push(`lounge ${l.name ?? '?'}: missing source`); continue; }
  const iata = (l.airport ?? 'TBS').toUpperCase();
  const visa = /^Visa Business Lounge/.test(l.name);
  const a = airports.get(iata) ?? { iata, airport: NAMES[iata]?.[0] ?? iata, city: NAMES[iata]?.[1] ?? iata, country: 'GE', places: [], notes: null };
  a.places.unshift({ name: l.name.trim(), kind: 'lounge', terminal: null, airside: true, schengen: null, location: text(l.who_can_enter),
    hours: null, programs: visa ? ['VisaAirportCompanion', 'Other'] : ['BankOwn'], offer: null,
    ...(visa ? {} : { bank: 'TBC Bank' }), source: l.source.trim() });
  airports.set(iata, a);
}

const data = { checked: cardsFile.checked ?? new Date().toISOString().slice(0, 10), programs, cards, airports: [...airports.values()].sort((a, b) => a.iata.localeCompare(b.iata)) };
fs.mkdirSync(path.dirname(out), { recursive: true });
fs.writeFileSync(out, JSON.stringify(data, null, 1) + '\n');
console.log(`cards ${cards.length}, programs ${programs.length}, airports ${data.airports.length}, places ${data.airports.reduce((n, a) => n + a.places.length, 0)}`);
if (dropped.length) console.log('dropped:\n  ' + dropped.join('\n  '));
