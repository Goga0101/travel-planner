// Step 3 of the world-news update (see .claude/agents/world-news-editor.md).
// Validates the agent's draft (scripts/news/.cache/draft.json), merges it with the stories already published,
// drops expired ones, adds city names and airports from the destination catalog, and writes the file the
// "World news" page reads: frontend/travel-planner-web/public/news/world-news.json.
//
// Usage: node scripts/news/build-news.mjs            merge the draft and publish
//        node scripts/news/build-news.mjs --check    only validate the published file
//        node scripts/news/build-news.mjs --refresh  rewrite the published file without a draft (drops expired
//                                                    stories, refreshes catalog data such as the lookup)
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, '..', '..');
const draftPath = join(here, '.cache', 'draft.json');
const outPath = join(root, 'frontend', 'travel-planner-web', 'public', 'news', 'world-news.json');
const checkOnly = process.argv.includes('--check');
const refreshOnly = process.argv.includes('--refresh');

export const CATEGORIES = ['safety', 'transport', 'events', 'weather', 'rules', 'tourism'];
export const IMPORTANCE = ['alert', 'notice', 'info'];
const LANGS = ['ka', 'en'];
const LIMITS = { title: 140, summary: 650, tip: 260 };
const MAX_AGE_DAYS = 45;
const MAX_LIFETIME_DAYS = 60;
const DEFAULT_LIFETIME_DAYS = { alert: 10, notice: 21, info: 14 };
const MAX_PER_COUNTRY = 10;
const DAY = 86_400_000;

const sources = JSON.parse(readFileSync(join(here, 'sources.json'), 'utf8'));
const catalog = JSON.parse(readFileSync(join(root, 'src', 'TravelPlanner.API', 'catalog', 'destinations.json'), 'utf8'));

const sourceById = new Map();
for (const s of sources.global) sourceById.set(s.id, s);
for (const s of Object.values(sources.countries)) sourceById.set(s.id, s);

const destinations = new Map(catalog.destinations.map((d) => [d.code, d]));
const countries = {};
for (const d of catalog.destinations) {
  countries[d.countryCode] ??= {
    name: { en: d.country, ka: d.translations?.ka?.country ?? d.country },
    region: sources.countries[d.countryCode]?.region ?? 'other',
  };
}

const read = (path, fallback) => (existsSync(path) ? JSON.parse(readFileSync(path, 'utf8')) : fallback);
const published = read(outPath, { items: [] });
const draft = checkOnly || refreshOnly ? { items: [] } : read(draftPath, null);
if (!draft) {
  console.error(`No draft at ${draftPath}. The agent writes it before this step.`);
  process.exit(1);
}

const hostAllowed = (url, domains) => {
  try {
    const u = new URL(url);
    return u.protocol === 'https:' && domains.some((d) => u.hostname === d || u.hostname.endsWith(`.${d}`));
  } catch {
    return false;
  }
};

/** Returns the problems with one item ([] = fine). */
function validate(item) {
  const errors = [];
  const need = (cond, msg) => { if (!cond) errors.push(msg); };
  need(typeof item.id === 'string' && /^[a-z0-9-]{6,90}$/.test(item.id), 'id: lowercase letters, digits and hyphens, 6-90 chars');
  need(countries[item.country], `country "${item.country}" is not a catalog country`);
  need(CATEGORIES.includes(item.category), `category must be one of ${CATEGORIES.join(', ')}`);
  need(IMPORTANCE.includes(item.importance), `importance must be one of ${IMPORTANCE.join(', ')}`);
  const source = sourceById.get(item.sourceId);
  need(source, `sourceId "${item.sourceId}" is not in sources.json`);
  if (source) need(hostAllowed(item.url, source.domains), `url must be https on ${source.domains.join(' / ')}`);
  need(typeof item.originalLanguage === 'string' && /^[a-z]{2}$/.test(item.originalLanguage), 'originalLanguage: two-letter code');
  const published = Date.parse(item.publishedAt);
  need(!Number.isNaN(published), 'publishedAt: ISO date');
  need(Number.isNaN(published) || published <= Date.now() + DAY, 'publishedAt is in the future');
  if (item.expiresAt !== undefined) {
    const expires = Date.parse(item.expiresAt);
    need(!Number.isNaN(expires) && expires > published, 'expiresAt: ISO date after publishedAt');
    need(Number.isNaN(expires) || expires - published <= MAX_LIFETIME_DAYS * DAY, `expiresAt: at most ${MAX_LIFETIME_DAYS} days after publishedAt`);
  }
  for (const field of ['title', 'summary', 'tip']) {
    const value = item[field];
    if (field === 'tip' && value === undefined) continue;
    for (const lang of LANGS) {
      const text = value?.[lang];
      need(typeof text === 'string' && text.trim().length > 0, `${field}.${lang} is missing`);
      if (typeof text === 'string') need(text.length <= LIMITS[field], `${field}.${lang} is longer than ${LIMITS[field]} characters`);
    }
    if (value?.ka) need(/[ა-ჰ]/.test(value.ka), `${field}.ka is not in Georgian`);
  }
  need(Array.isArray(item.destinations ?? []), 'destinations: array of catalog codes');
  for (const code of item.destinations ?? []) {
    const d = destinations.get(code);
    need(d, `destination "${code}" is not in the catalog`);
    if (d) need(d.countryCode === item.country, `destination "${code}" is not in ${item.country}`);
  }
  return errors;
}

/** Adds what the page shows next to the text: city names and the airport for the flights link. */
function enrich(item) {
  const places = (item.destinations ?? []).map((code) => {
    const d = destinations.get(code);
    const airport = d.airports.find((a) => a.isDepartureOption) ?? d.airports[0];
    return { code, city: { en: d.city, ka: d.translations?.ka?.city ?? d.city }, iata: airport?.iata ?? null };
  });
  const expiresAt = item.expiresAt ?? new Date(Date.parse(item.publishedAt) + DEFAULT_LIFETIME_DAYS[item.importance] * DAY).toISOString();
  const { destinations: _, ...rest } = item;
  return { ...rest, publishedAt: new Date(item.publishedAt).toISOString(), expiresAt: new Date(expiresAt).toISOString(), places };
}

// Published items come back in their enriched form: turn places into destination codes again.
const previous = (published.items ?? []).map(({ places, ...i }) => ({ ...i, destinations: (places ?? []).map((p) => p.code) }));
const merged = new Map(previous.map((i) => [i.id, i]));
for (const item of draft.items ?? []) merged.set(item.id, item);

const problems = [];
const now = Date.now();
let expired = 0;
const kept = [];
for (const item of merged.values()) {
  const errors = validate(item);
  if (errors.length) {
    problems.push(`${item.id ?? '(no id)'}:\n    - ${errors.join('\n    - ')}`);
    continue;
  }
  const e = enrich(item);
  if (Date.parse(e.expiresAt) <= now || now - Date.parse(e.publishedAt) > MAX_AGE_DAYS * DAY) {
    expired++;
    continue;
  }
  kept.push(e);
}

if (problems.length) {
  console.error(`${problems.length} item(s) need fixing; nothing was written:\n  ${problems.join('\n  ')}`);
  process.exit(1);
}

// Per country: the most important first, then the newest; keep the top MAX_PER_COUNTRY.
const rank = (i) => IMPORTANCE.indexOf(i.importance);
const byCountry = new Map();
for (const i of kept) (byCountry.get(i.country) ?? byCountry.set(i.country, []).get(i.country)).push(i);
const items = [...byCountry.values()]
  .flatMap((list) => list.sort((a, b) => rank(a) - rank(b) || b.publishedAt.localeCompare(a.publishedAt)).slice(0, MAX_PER_COUNTRY))
  .sort((a, b) => b.publishedAt.localeCompare(a.publishedAt));

const usedSources = Object.fromEntries(
  [...new Set(items.map((i) => i.sourceId))].map((id) => {
    const s = sourceById.get(id);
    return [id, { name: s.name, url: s.web, language: s.language, kind: s.kind ?? 'Public broadcaster' }];
  }),
);
const usedCountries = Object.fromEntries([...new Set(items.map((i) => i.country))].sort().map((cc) => [cc, countries[cc]]));

if (checkOnly) {
  console.log(`OK: ${items.length} published stories are valid (${expired} expired would be dropped on the next build).`);
  process.exit(0);
}

/**
 * Which country a catalog city or airport belongs to, so pages that only know "barcelona" or "BCN" (a plan, a
 * flight) can show that country's news.
 */
const lookup = { cities: {}, airports: {}, countryNames: {}, cityNames: {}, cityAirports: {} };
for (const d of catalog.destinations) {
  lookup.cities[d.code] = d.countryCode;
  lookup.cityNames[d.code] = { en: d.city, ka: d.translations?.ka?.city ?? d.city };
  lookup.cityAirports[d.code] = (d.airports.find((a) => a.isDepartureOption) ?? d.airports[0])?.iata ?? null;
  for (const a of d.airports) lookup.airports[a.iata] = d.countryCode;
  lookup.countryNames[d.countryCode] = countries[d.countryCode].name;
}

const output = {
  $comment: 'Written by scripts/news/build-news.mjs from the world-news-editor agent draft. Do not edit by hand.',
  generatedAt: new Date().toISOString(),
  countries: usedCountries,
  sources: usedSources,
  items,
  lookup,
};
mkdirSync(dirname(outPath), { recursive: true });
writeFileSync(outPath, JSON.stringify(output, null, 2) + '\n');
console.log(`Published ${items.length} stories from ${Object.keys(usedCountries).length} countries (${draft.items?.length ?? 0} from this draft, ${expired} expired dropped).`);
console.log(`Wrote ${outPath}`);
