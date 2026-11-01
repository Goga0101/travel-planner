// Step 1 of the world-news update (see .claude/agents/world-news-editor.md).
// Reads the RSS/Atom feeds in scripts/news/sources.json and writes the recent headlines per country to
// scripts/news/.cache/feeds/<CC>.json (plus GLOBAL.json), with the catalog cities of that country, so the agent
// can pick travel-relevant stories. Also writes .cache/report.json: which feeds worked and which countries the
// agent has to read from the broadcaster's website instead.
//
// Usage: node scripts/news/fetch-feeds.mjs [--days 7] [--max 40]
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, '..', '..');
const cacheDir = join(here, '.cache', 'feeds');
const arg = (name, fallback) => {
  const i = process.argv.indexOf(`--${name}`);
  return i > 0 ? Number(process.argv[i + 1]) : fallback;
};
const DAYS = arg('days', 7);
const MAX_ITEMS = arg('max', 40);
const CONCURRENCY = 4;
const since = Date.now() - DAYS * 86_400_000;

const sources = JSON.parse(readFileSync(join(here, 'sources.json'), 'utf8'));
const catalog = JSON.parse(readFileSync(join(root, 'src', 'TravelPlanner.API', 'catalog', 'destinations.json'), 'utf8'));

/** Catalog cities per country: the agent tags stories with these codes. */
const citiesByCountry = {};
for (const d of catalog.destinations) {
  (citiesByCountry[d.countryCode] ??= []).push({ code: d.code, city: d.city, cityKa: d.translations?.ka?.city ?? null });
}

const decode = (s) =>
  s.replace(/<!\[CDATA\[([\s\S]*?)\]\]>/g, '$1')
    .replace(/<[^>]+>/g, ' ')
    .replace(/&#(\d+);/g, (_, n) => String.fromCodePoint(Number(n)))
    .replace(/&#x([0-9a-f]+);/gi, (_, n) => String.fromCodePoint(parseInt(n, 16)))
    .replace(/&nbsp;/g, ' ').replace(/&quot;/g, '"').replace(/&apos;|&#39;/g, "'")
    .replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&')
    .replace(/\s+/g, ' ').trim();

const tag = (block, names) => {
  for (const name of names) {
    const m = block.match(new RegExp(`<${name}(?:\\s[^>]*)?>([\\s\\S]*?)</${name}>`, 'i'));
    if (m) return decode(m[1]);
  }
  return '';
};

/** Minimal RSS 2.0 / RDF / Atom reader: title, link, date and a short description per item. */
function parseFeed(xml) {
  const blocks = xml.match(/<item[\s>][\s\S]*?<\/item>/gi) ?? xml.match(/<entry[\s>][\s\S]*?<\/entry>/gi) ?? [];
  return blocks.map((b) => {
    const atomLink = b.match(/<link[^>]*rel="alternate"[^>]*href="([^"]+)"/i) ?? b.match(/<link[^>]*href="([^"]+)"/i);
    const link = tag(b, ['link']) || atomLink?.[1] || tag(b, ['guid']);
    const date = tag(b, ['pubDate', 'dc:date', 'published', 'updated']);
    const time = Date.parse(date);
    return {
      title: tag(b, ['title']),
      // Some feeds (GDACS, Bernama) still give http:// links; their sites serve the same pages over https.
      link: link.trim().replace(/^http:\/\//i, 'https://'),
      publishedAt: Number.isNaN(time) ? null : new Date(time).toISOString(),
      description: tag(b, ['description', 'summary', 'content:encoded', 'content']).slice(0, 400),
    };
  });
}

async function fetchFeed(url) {
  let lastError;
  for (let attempt = 0; attempt < 2; attempt++) {
    try {
      const r = await fetch(url, {
        headers: { 'user-agent': 'Mozilla/5.0 (compatible; TriporaNewsBot/1.0; +https://goga0101.github.io/travel-planner/)' },
        signal: AbortSignal.timeout(20_000),
      });
      if (!r.ok) throw new Error(`HTTP ${r.status}`);
      return parseFeed(await r.text());
    } catch (e) {
      lastError = e;
    }
  }
  throw lastError;
}

const jobs = [
  ...sources.global.map((s) => ({ key: 'GLOBAL', source: s })),
  ...Object.entries(sources.countries).map(([cc, s]) => ({ key: cc, source: s })),
];
const results = {};
const report = { fetchedAt: new Date().toISOString(), days: DAYS, ok: [], empty: [], failed: [], webOnly: [] };

async function run(job) {
  const { key, source } = job;
  const bucket = (results[key] ??= { country: key, sources: [], items: [] });
  bucket.sources.push({ id: source.id, name: source.name, language: source.language, web: source.web, domains: source.domains });
  if (!source.feeds.length) {
    report.webOnly.push({ country: key, source: source.id, web: source.web });
    return;
  }
  for (const url of source.feeds) {
    try {
      const all = await fetchFeed(url);
      const recent = all
        .filter((i) => i.title && i.link && (!i.publishedAt || Date.parse(i.publishedAt) >= since))
        .sort((a, b) => (b.publishedAt ?? '').localeCompare(a.publishedAt ?? ''))
        .slice(0, key === 'GLOBAL' ? 80 : MAX_ITEMS)
        .map((i) => ({ ...i, sourceId: source.id }));
      bucket.items.push(...recent);
      (recent.length ? report.ok : report.empty).push({ country: key, source: source.id, url, items: recent.length });
    } catch (e) {
      report.failed.push({ country: key, source: source.id, url, error: String(e.cause?.code ?? e.message ?? e), web: source.web });
    }
  }
}

const queue = [...jobs];
await Promise.all(Array.from({ length: CONCURRENCY }, async () => {
  while (queue.length) await run(queue.shift());
}));

mkdirSync(cacheDir, { recursive: true });
for (const [key, bucket] of Object.entries(results)) {
  writeFileSync(join(cacheDir, `${key}.json`), JSON.stringify({ ...bucket, cities: citiesByCountry[key] ?? [] }, null, 2));
}
writeFileSync(join(here, '.cache', 'report.json'), JSON.stringify(report, null, 2));

console.log(`Feeds: ${report.ok.length} ok, ${report.empty.length} with no stories in the last ${DAYS} days, ${report.failed.length} failed.`);
for (const f of report.failed) console.log(`  failed  ${f.country} ${f.source}: ${f.error} -> read ${f.web}`);
for (const w of report.webOnly) console.log(`  web     ${w.country} ${w.source}: no feed -> read ${w.web}`);
console.log(`Headlines written to ${join('scripts', 'news', '.cache', 'feeds')}`);
