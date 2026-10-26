// Builds src/TravelPlanner.API/catalog/nearby.json from OpenStreetMap (Overpass API).
// For every catalog destination: named hotels near each airport and near the city centre, public-transport lines that
// stop at the airport, and taxi ranks at the airport. Overpass is slow and rate-limited, so the result is a dated
// snapshot that the API serves; re-run this script to refresh it. Data © OpenStreetMap contributors (ODbL).
//
// Usage: node scripts/fetch-nearby.mjs [destination-code ...]

import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const catalogPath = join(root, 'src/TravelPlanner.API/catalog/destinations.json');
const outPath = join(root, 'src/TravelPlanner.API/catalog/nearby.json');

// Mirrors are optional: set OVERPASS_ENDPOINTS=url1,url2 to use others (some networks cannot reach them).
const ENDPOINTS = (process.env.OVERPASS_ENDPOINTS ?? 'https://overpass-api.de/api/interpreter').split(',').map((s) => s.trim()).filter(Boolean);
const USER_AGENT = 'TravelPlanner/1.0 (https://github.com/Goga0101/travel-planner)';
const AIRPORT_HOTEL_RADIUS_M = 5000;
const CENTRE_HOTEL_RADIUS_M = 1200;
const AIRPORT_STOP_RADIUS_M = 1500;
const TAXI_RADIUS_M = 2500;
const TAXI_COMPANY_RADIUS_M = 8000;
const MAX_HOTELS = 12;
const MAX_LINES = 15;
const HOTEL_TYPES = 'hotel|hostel|guest_house|motel|apartment';
const ROUTE_TYPES = ['bus', 'trolleybus', 'train', 'subway', 'tram', 'light_rail', 'monorail'];

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function distanceM(lat1, lon1, lat2, lon2) {
  const rad = Math.PI / 180;
  const a = Math.sin(((lat2 - lat1) * rad) / 2) ** 2 + Math.cos(lat1 * rad) * Math.cos(lat2 * rad) * Math.sin(((lon2 - lon1) * rad) / 2) ** 2;
  return Math.round(2 * 6371000 * Math.asin(Math.sqrt(a)));
}

async function overpass(query, attempts = 8) {
  for (let attempt = 0; attempt < attempts; attempt++) {
    const endpoint = ENDPOINTS[attempt % ENDPOINTS.length];
    try {
      const res = await fetch(endpoint, {
        method: 'POST',
        headers: { 'User-Agent': USER_AGENT, 'Content-Type': 'application/x-www-form-urlencoded' },
        body: 'data=' + encodeURIComponent(query),
        signal: AbortSignal.timeout(120_000),
      });
      const text = await res.text();
      if (res.ok && text.trimStart().startsWith('{')) return JSON.parse(text);
      console.warn(`  ${endpoint} -> HTTP ${res.status}, retrying`);
    } catch (e) {
      console.warn(`  ${endpoint} -> ${e.name}: ${e.message}, retrying`);
    }
    await sleep(Math.min(60_000, 10_000 * (attempt + 1)));
  }
  throw new Error('Overpass unavailable');
}

const point = (e) => (e.type === 'node' ? { lat: e.lat, lon: e.lon } : e.center ? { lat: e.center.lat, lon: e.center.lon } : null);
const https = (url) => (url && /^https:\/\//i.test(url.trim()) ? url.trim() : url && /^www\./i.test(url.trim()) ? 'https://' + url.trim() : null);
const clean = (s) => (s ? String(s).trim().slice(0, 160) : null);

function hotel(e, anchor) {
  const p = point(e);
  const t = e.tags;
  const stars = Number.parseInt(t.stars, 10);
  const street = [t['addr:street'], t['addr:housenumber']].filter(Boolean).join(' ');
  return {
    osmId: `${e.type}/${e.id}`,
    name: clean(t.name),
    nameEn: clean(t['name:en']),
    kind: t.tourism,
    stars: stars >= 1 && stars <= 5 ? stars : null,
    latitude: p.lat,
    longitude: p.lon,
    distanceMeters: distanceM(anchor.lat, anchor.lon, p.lat, p.lon),
    address: clean([street, t['addr:postcode'], t['addr:city']].filter(Boolean).join(', ')) || null,
    phone: clean(t.phone ?? t['contact:phone']),
    website: https(t.website ?? t['contact:website']),
  };
}

// Prefer well-described hotels, then the closest ones.
const hotelRank = (h) => (h.kind === 'hotel' ? 0 : 1) * 3 - (h.stars ? 1 : 0) - (h.phone || h.website ? 1 : 0) + h.distanceMeters / 5000;

async function fetchDestination(d) {
  const airports = d.airports.map((a) => ({ iata: a.iata, lat: a.latitude, lon: a.longitude }));
  const centre = { lat: d.latitude, lon: d.longitude };
  const result = { code: d.code, airports: [], centre: { hotels: [] }, taxiCompanies: [] };

  const centreData = await overpass(
    `[out:json][timeout:90];nwr["tourism"~"^(${HOTEL_TYPES})$"]["name"](around:${CENTRE_HOTEL_RADIUS_M},${centre.lat},${centre.lon});out center tags 80;`,
  );
  await sleep(4000);
  // Taxi companies with a phone number or website, so travellers can call or book one. Optional: skipped if Overpass is struggling.
  let taxiData = { elements: [] };
  try {
    taxiData = await overpass(
      `[out:json][timeout:60];(node["office"="taxi"](around:${TAXI_COMPANY_RADIUS_M},${centre.lat},${centre.lon});
        node["amenity"="taxi"]["phone"](around:${TAXI_COMPANY_RADIUS_M},${centre.lat},${centre.lon}););out tags 40;`,
      3,
    );
  } catch {
    console.warn('  taxi companies skipped');
  }
  const companies = new Map();
  for (const e of taxiData.elements) {
    const t = e.tags;
    const phone = clean(t.phone ?? t['contact:phone']);
    const website = https(t.website ?? t['contact:website']);
    const name = clean(t.name ?? t.operator);
    if (!name || (!phone && !website) || companies.has(name)) continue;
    companies.set(name, { name, phone, website });
  }
  result.taxiCompanies = [...companies.values()].sort((a, b) => (b.phone ? 1 : 0) - (a.phone ? 1 : 0)).slice(0, 6);
  result.centre.hotels = centreData.elements.filter((e) => e.tags?.tourism && point(e)).map((e) => hotel(e, centre))
    .sort((a, b) => hotelRank(a) - hotelRank(b)).slice(0, MAX_HOTELS);
  await sleep(4000);

  for (const a of airports) {
    const q = `[out:json][timeout:120];
      nwr["tourism"~"^(${HOTEL_TYPES})$"]["name"](around:${AIRPORT_HOTEL_RADIUS_M},${a.lat},${a.lon});out center tags 80;
      (nwr["public_transport"~"^(platform|stop_position)$"](around:${AIRPORT_STOP_RADIUS_M},${a.lat},${a.lon});
       nwr["railway"~"^(station|halt)$"](around:${AIRPORT_STOP_RADIUS_M},${a.lat},${a.lon});)->.stops;
      rel(bn.stops)["route"~"^(${ROUTE_TYPES.join('|')})$"];out tags;
      nwr["amenity"="taxi"](around:${TAXI_RADIUS_M},${a.lat},${a.lon});out center tags 20;`;
    const data = await overpass(q);
    const hotels = [];
    const lines = new Map();
    const taxis = [];
    for (const e of data.elements) {
      const t = e.tags ?? {};
      if (e.type === 'relation' && t.route) {
        const key = `${t.route}|${t.ref ?? t.name}`;
        if (lines.has(key)) continue;
        lines.set(key, {
          mode: t.route,
          ref: clean(t.ref),
          name: clean(t['name:en'] ?? t.name),
          from: clean(t.from),
          to: clean(t.to),
          operator: clean(t.operator),
          network: clean(t.network),
          website: https(t.website ?? t.url),
        });
      } else if (t.amenity === 'taxi' && point(e)) {
        const p = point(e);
        taxis.push({
          name: clean(t.name),
          operator: clean(t.operator),
          phone: clean(t.phone ?? t['contact:phone']),
          latitude: p.lat,
          longitude: p.lon,
          distanceMeters: distanceM(a.lat, a.lon, p.lat, p.lon),
        });
      } else if (t.tourism && t.name && point(e)) {
        hotels.push(hotel(e, a));
      }
    }
    const modeOrder = (m) => ['train', 'subway', 'light_rail', 'monorail', 'tram', 'bus', 'trolleybus'].indexOf(m);
    result.airports.push({
      iata: a.iata,
      hotels: hotels.sort((x, y) => hotelRank(x) - hotelRank(y)).slice(0, MAX_HOTELS),
      transitLines: [...lines.values()]
        .sort((x, y) => modeOrder(x.mode) - modeOrder(y.mode) || String(x.ref ?? '').localeCompare(String(y.ref ?? ''), undefined, { numeric: true }))
        .slice(0, MAX_LINES),
      taxiRanks: taxis.sort((x, y) => x.distanceMeters - y.distanceMeters).slice(0, 5),
    });
    await sleep(5000);
  }
  return result;
}

const catalog = JSON.parse(readFileSync(catalogPath, 'utf8'));
const only = process.argv.slice(2);
const existing = existsSync(outPath) ? JSON.parse(readFileSync(outPath, 'utf8')) : { destinations: [] };
const byCode = new Map(existing.destinations.map((d) => [d.code, d]));

for (const d of catalog.destinations) {
  if (only.length && !only.includes(d.code)) continue;
  console.log(`${d.code}…`);
  try {
    const r = await fetchDestination(d);
    r.retrievedOn = new Date().toISOString().slice(0, 10);
    byCode.set(d.code, r);
    console.log(`  centre hotels ${r.centre.hotels.length}, taxi companies ${r.taxiCompanies.length}; ` + r.airports.map((a) => `${a.iata}: hotels ${a.hotels.length}, lines ${a.transitLines.length}, taxi ${a.taxiRanks.length}`).join('; '));
  } catch (e) {
    console.error(`  failed: ${e.message} (kept previous data, if any)`);
  }
  // Save after every destination so an interrupted run keeps its progress.
  const out = {
    $comment: 'Generated by scripts/fetch-nearby.mjs from OpenStreetMap via the Overpass API. Data © OpenStreetMap contributors, ODbL 1.0. Do not edit by hand; re-run the script to refresh.',
    attribution: '© OpenStreetMap contributors',
    license: 'ODbL-1.0',
    destinations: catalog.destinations.map((x) => byCode.get(x.code)).filter(Boolean),
  };
  writeFileSync(outPath, JSON.stringify(out, null, 1) + '\n');
}
