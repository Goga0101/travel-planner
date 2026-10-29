# Travel Planner MVP

Plans complete trips from a budget, a date window, a trip length and a departure airport. It finds round-trip
flights, accommodation and airport transfers, calculates the full estimated cost, and returns the 3–5 best plans
with sources, verification status and **official booking links**.

> This is a search-and-redirect planner. It never takes payments and never completes bookings: users book on the
> provider's official website.

| Layer | Tech |
|---|---|
| Backend | ASP.NET Core (.NET 10), Clean Architecture modular monolith, EF Core 10 + PostgreSQL, MediatR 12.5 (CQRS), FluentValidation, Swagger, Serilog, HttpClientFactory + Polly resilience |
| Frontend | Angular 21.2 (standalone components, signals, Reactive Forms, OnPush), plain SCSS, no UI kit. Angular 22 needs Node ≥ 24.15; this machine has 24.13. |
| Tests | xUnit v3 (Microsoft.Testing.Platform), Testcontainers PostgreSQL, WebApplicationFactory |
| Infra | Docker Compose (PostgreSQL, optional API and web containers) |

---

## 1. Quick start (demo mode, about 5 minutes)

Prerequisites: .NET SDK 10.0.4xx, Node.js 22 or 24 with npm, and Docker Desktop.

```bash
# 1. Database (PostgreSQL 17 on localhost:5433, chosen to avoid clashing with a native PostgreSQL on 5432)
docker compose up -d postgres

# 2. Backend: applies migrations and seeds the destination catalog on startup in Development
dotnet tool restore
dotnet run --project src/TravelPlanner.API --launch-profile http
#    API:     http://localhost:5080
#    Swagger: http://localhost:5080/swagger
#    Health:  http://localhost:5080/health/ready

# 3. Frontend (second terminal)
cd frontend/travel-planner-web
npm install
npm start            # ng serve on http://localhost:4200, proxying /api to :5080
```

Open http://localhost:4200 and submit the default search (TBS, 20–30 Nov, 5 days, €1,000, automatic). In demo
mode a red **DEMO MODE** banner is shown, every item carries the `Demo` verification badge, accommodation names
start with `[DEMO]`, carriers use the non-airline codes `Z1`–`Z4`, and demo offers have **no** booking links.

### Everything in containers

```bash
cp .env.example .env     # then edit the values
docker compose --profile app up -d --build
# web: http://localhost:8081   api: http://localhost:5080/swagger
```

---

## 2. Data sources: what works and what is blocked

Verified against official documentation in October 2026.

| Area | Provider | Status | What you need |
|---|---|---|---|
| Flights (live fares) | **Amadeus** Flight Offers Search v2 | Integration implemented and unit-tested with recorded payloads. **Blocked:** Amadeus closed its Self-Service portal and disabled all Self-Service keys on 2026-07-17. Only Enterprise access remains. | Amadeus Enterprise contract. Set `Providers:Amadeus:BaseUrl`, `ClientId`, `ClientSecret`. |
| Flights (cached prices + links) | **Travelpayouts / Aviasales Data API** `GET /aviasales/v3/prices_for_dates` | Implemented. Self-serve signup. Prices are **cached indications** (tickets found by Aviasales users within about 48 h) and are labelled `Cached`, never `Live`. Prices are per passenger and are multiplied by the passenger count, which is flagged. | Travelpayouts account and API token (optional affiliate `Marker`). |
| Accommodation | **Booking.com Demand API** 3.1 (`accommodations/search` + `accommodations/details`) | Implemented with defensive parsing. **Blocked:** requires Managed Affiliate Partner approval and a signed contract. City IDs in the catalog are **unverified** and must be confirmed with the `/common/locations` endpoints. | Booking.com API key and `X-Affiliate-Id` (sandbox URL in `.env.example`). |
| Accommodation (no API) | **Booking.com search link** (`Providers:BookingComLinks`) | Always on. When no priced stay is available, the plan links to Booking.com's public search results pre-filled with city, dates and guests. Nothing is scraped, and the cost stays "missing". Affiliate tracking is optional: set `AffiliateId` (`aid`), or a CJ/Awin `DeepLinkTemplate` containing `{url}` and add the network's host to `OutboundLinks:AllowedHosts`. Since mid-2025 Booking.com's affiliate programme runs through CJ (Europe) or Awin. | Nothing. Commission requires joining the affiliate programme. |
| Transfers | **Google Maps Platform Routes API** `directions/v2:computeRoutes` (TRANSIT + DRIVE) | Implemented. Self-serve (billing account). Transit fares are shown as `Estimate`. Driving results show distance and time only, **never a taxi quote**. | Server API key restricted to the Routes API. |
| Transfers (always on) | Curated catalog: official airport websites and public-transport operators | Domains checked for reachability on 2026-10-20. Shown as `Verified` information links. No taxi companies, phone numbers or tariffs were invented. Taxi tariffs are supported but left empty until you add one from an official source. | Nothing. Edit `src/TravelPlanner.API/catalog/destinations.json`. |
| Nearby places (always on) | **OpenStreetMap** via the Overpass API, as a dated snapshot in `catalog/nearby.json` | Hotels near each airport and the city centre (name, stars, address, phone, website), public-transport lines that stop at the airport, taxi ranks and taxi companies. **No prices** (OSM has none): each hotel links to a Booking.com search with the plan's dates, a Google Maps transit route from the airport and an Uber link with pickup and drop-off pre-filled. Overpass is slow and rate-limited, so the API never calls it live; run `node scripts/fetch-nearby.mjs [code ...]` to refresh. The API re-reads the file when it changes. Data © OpenStreetMap contributors (ODbL), credited on the page. | Nothing. |
| Hotel price estimates | **Xotelo** (free, unofficial feed of TripAdvisor price comparison) `api/list` + `api/rates` | Live mode only. Per destination (TripAdvisor geo id `tripAdvisorGeoKey` in the catalog), the 8 best-value hotels and their nightly rates per agency (Booking.com, Agoda, Trip.com, ...) for the plan's dates, guests and rooms. Shown as **estimates** on result cards and the plan page; never added to a plan total. Prefetched when a search completes, cached 6 h (rates) / 24 h (hotel list). Unofficial: may change or stop without notice, in which case the UI shows only the Booking.com search links. | Nothing. Disable with `Providers:Xotelo:Enabled=false`. |
| Sights | **Geoapify** (OpenStreetMap places with a Wikidata id) + **Wikidata** (ranking by number of Wikipedia editions, names) + **Wikipedia** REST summaries (description and photo, Georgian when an article exists) | Top 12 sights per destination with the nearest metro/train/tram station and bus stop (with line numbers), short opening hours, Google Maps public-transport directions. Cached 24 h and warmed at startup. | The Geoapify key. Wikimedia needs no key. |
| World airports and cities | **Travelpayouts static data** (`api.travelpayouts.com/data`, free, no key) as a dated snapshot in `catalog/world-airports.json` | 3,600+ flightable airports with city, country and continent (derived from time zones, with overrides). Seeded as departure airports and used for destinations outside the catalog. Refresh with `node scripts/fetch-world-airports.mjs`. | Nothing. |
| Visitor location | **Travelpayouts `whereami`** (free, no key) | Preselects the departure airport nearest to the visitor's IP. Behind the tunnel the address comes from `CF-Connecting-IP`; local addresses are not sent. Cached for a day. The visitor's own choice is remembered in the browser. | Nothing. |
| Airport ↔ hotel routes | **Transitous** (MOTIS 2, community-run, free with fair use; open GTFS timetables) | `GET /api/v1/trip-plans/{id}/route?lat&lon&name`: step-by-step public-transport journeys from the arrival airport to the hotel the visitor marked (the city centre otherwise) around the landing time, and back 3 h before take-off. Trips beyond the published timetables are planned on the next same weekday and labelled. Implausible journeys (long-distance legs, over 150 min) are dropped; where open data is incomplete the page falls back to Google Maps transit, driving and Uber links. | Nothing. |
| Ticket guide | `catalog/transit-guides.json`, researched from operators' and airports' official pages (2026-10-27) | Per destination: airport-to-city options, the ticket needed, adult single price, where to buy it at the airport, payment (contactless), timing, city single ticket, day pass, apps, tips and sources. Values that could not be confirmed on an official page are null. Prices change: re-check before relying on them. | Nothing. |
| Day planner | **Geoapify** places (restaurants, cafés, bars, clubs, museums, parks, beaches, shopping, theatres) + the ranked sights, photos from **Wikimedia Commons** (Wikidata P18) | `GET /api/v1/trip-plans/{id}/day-plan?interests=culture,food,bars&pace=Normal`: a day-by-day itinerary around the flight times, each slot the closest unused place of its interest to the previous stop (notable places first), with alternatives and Google Maps transit directions. No ratings exist in open data. | The Geoapify key. |
| Events | **Ticketmaster Discovery API** (optional) | Concerts, shows and matches within 15 km during the trip, with face-value price ranges and images, scheduled into the day plan when they fit the interests. Without a key the planner links to an events search. | Free key from developer.ticketmaster.com: `Providers:Ticketmaster:ApiKey`. |
| Weather (frontend) | **Open-Meteo** forecast and archive APIs (free, no key) | Forecast when the whole trip is within 16 days, otherwise last year's weather on the same dates, labelled as a reference. Hidden if the request fails. | Nothing. |
| Exchange rates | **ECB** euro reference rates (daily XML, free, no key) | Implemented and cached for 6 h. Currencies the ECB doesn't publish (e.g. GEL) are never converted. | Nothing. |
| Route map | Google Maps **Embed API** (optional) | Without a key, the UI links to the official Google Maps directions URL instead. | Browser key restricted by HTTP referrer: `ClientConfig:MapsEmbedApiKey`. |

**Modes** (`Providers:Mode`):
- `Demo` (default in `appsettings.json`) registers only the demo providers. Everything is visibly labelled demo data.
- `Live` registers only the real providers whose credentials are configured. Missing categories are **reported, not
  faked**:
  - With no flight provider, the search fails with a clear message.
  - With no accommodation provider, plans are marked `WithinBudgetIncomplete` and list `Accommodation` under missing cost categories.
  - With no Routes key, plans still show official airport and transit links.

  `GET /api/v1/client-config` and the startup log list which providers are active.

---

## 3. Configuration

All settings live in `src/TravelPlanner.API/appsettings.json` and can be overridden with environment variables
(`Section__Key`) or user secrets. **Never put secrets in appsettings.** Use one of these instead:

```bash
cd src/TravelPlanner.API
dotnet user-secrets set "Providers:Mode" "Live"
dotnet user-secrets set "Providers:Travelpayouts:Enabled" "true"
dotnet user-secrets set "Providers:Travelpayouts:ApiToken" "<token>"
dotnet user-secrets set "Providers:GoogleRoutes:Enabled" "true"
dotnet user-secrets set "Providers:GoogleRoutes:ApiKey" "<key>"
```

Or use the `.env` file with Docker Compose (see `.env.example`, which contains placeholders only).

| Section | Purpose |
|---|---|
| `ConnectionStrings:TravelPlanner` | PostgreSQL. Development defaults to the compose container on port 5433. The `travel_planner_dev` password is a local throw-away default; override it everywhere else. |
| `Database:MigrateOnStartup` | `true` in Development and Compose. For production, run migrations explicitly (below). |
| `Catalog:Path` / `SeedOnStartup` | Configurable destination catalog (JSON). Upserted by code/IATA. Removed destinations are deactivated. |
| `Catalog:NearbyPath` | OpenStreetMap snapshot of hotels, transit lines and taxis per destination (`scripts/fetch-nearby.mjs`). Optional: without it, plan pages show no nearby places. |
| `Planning:*` | Request limits, concurrency, timeouts, result count and ranking weights (see §6). |
| `Budget:*` | Optional daily food and local-transport allowance per person, and a contingency percentage. Empty means disabled. |
| `OutboundLinks:AllowedHosts` | Allow-list for every URL shown to users (exact host or subdomain, HTTPS only). |
| `Cors:AllowedOrigins` | Frontend origins (default `http://localhost:4200`). |
| `Auth:*` | Sign-in with Google and Facebook (§3a). `Auth:Jwt:Key` and `Auth:Facebook:AppSecret` are secrets; the Google client id and Facebook app id are public. |
| `RateLimiting:*` | Per-IP limits: global, search creation, price refresh and sign-in. |
| `Providers:*` | Mode, cache TTLs, HTTP resilience (attempt and total timeout, retries) and per-provider credentials. |


### 3a. Sign-in with Google and Facebook

The browser signs in with the provider's own SDK and receives a token: Google Identity Services gives an **ID token**, the
Facebook JS SDK a **user access token**. The page sends it to `POST /api/v1/auth/external`. The API verifies it with the
provider: for Google, the signature against Google's keys, the expiry and that its audience is our client id; for Facebook,
`debug_token` with the app's own token confirms it was issued to our app. Then it creates or finds the user and returns its
own signed JWT. The page keeps that JWT in `localStorage` and sends it as `Authorization: Bearer`. One person signing in with
both providers gets one account when both report the same verified email.

Nothing is enabled until it is configured. Without `Auth:Jwt:Key` all sign-in is off, and a provider without its ids is off.

**Google** ([Google Cloud Console](https://console.cloud.google.com/)):

1. Create or choose a project. Under **APIs & Services → OAuth consent screen**, choose *External* and fill in the app
   name, support email and the authorized domain `goga0101.github.io`. Use the scopes `openid`, `email` and `profile`, and
   publish the app (in *Testing*, only listed test users can sign in).
2. Under **Credentials → Create credentials → OAuth client ID**, choose *Web application*, and add these
   **Authorized JavaScript origins**: `https://goga0101.github.io`, `http://localhost:4200` and `http://localhost`. No
   redirect URI is needed because sign-in uses a popup.
3. Copy the **Client ID**, which looks like `….apps.googleusercontent.com`. The client secret is not used.

**Facebook** ([Meta for Developers](https://developers.facebook.com/apps)):

1. **Create app → Authenticate and request data from users with Facebook Login** (type *Consumer*).
2. In **App settings → Basic**, set the app domain to `goga0101.github.io`, add a privacy policy URL (required before going
   *Live*) and copy the **App ID** and the **App secret**.
3. In **Facebook Login → Settings**, turn on *Login with the JavaScript SDK* and set **Allowed domains for the JavaScript
   SDK** to `https://goga0101.github.io/` and `http://localhost:4200/`.
4. Permissions: `public_profile` and `email`. These are standard and need no review. Switch the app to **Live**; in
   *Development* only app roles (admins, testers) can sign in.

**Configure the API** (secrets stay in user-secrets locally and in environment variables elsewhere):

```bash
cd src/TravelPlanner.API
dotnet user-secrets set "Auth:Jwt:Key" "$(openssl rand -base64 48)"   # once; changing it signs everyone out
dotnet user-secrets set "Auth:Google:ClientId" "<client-id>.apps.googleusercontent.com"
dotnet user-secrets set "Auth:Facebook:AppId" "<app-id>"
dotnet user-secrets set "Auth:Facebook:AppSecret" "<app-secret>"
```

Restart the API. The ids reach the page through `/api/v1/client-config`, so the frontend needs no rebuild.

---

## 4. Database and migrations

PostgreSQL schema via EF Core migrations (`src/TravelPlanner.Infrastructure/Persistence/Migrations`).

Tables:
- `destinations`, `airports`, `destination_transport_links` (catalog)
- `trip_searches` (also the background-job record: status and progress; preferences stored as `jsonb`)
- `trip_search_destinations`
- `trip_plans`
- `trip_plan_items`: type (Flight, Accommodation, Transfer, Fee, DailyExpenseEstimate, Contingency), provider, offer id or signature, original and converted price, exchange rate with source and as-of date, booking URL, retrieved and expiry timestamps, verification status, and normalized `jsonb` details

Schema conventions: money is `numeric`, dates are `date`, and timestamps are `timestamptz` in UTC. Foreign keys, unique indexes and check constraints are included. Raw provider payloads are **not** stored.

```bash
dotnet tool restore
# fresh database
docker compose up -d postgres
dotnet ef database update -p src/TravelPlanner.Infrastructure -s src/TravelPlanner.API
# new migration after model changes
dotnet ef migrations add <Name> -p src/TravelPlanner.Infrastructure -s src/TravelPlanner.API -o Persistence/Migrations
# SQL script for production
dotnet ef migrations script -p src/TravelPlanner.Infrastructure -s src/TravelPlanner.API --idempotent -o migrate.sql
# reset the local database
docker compose down -v && docker compose up -d postgres
```

---

## 5. REST API (v1)

| Method | Route | Notes |
|---|---|---|
| POST | `/api/v1/trip-searches` | **202 Accepted** with `Location`. Runs in the background. Rate-limited. |
| GET | `/api/v1/trip-searches/{id}` | Status (`Queued`, `Running`, `Completed`, `CompletedNoMatch`, `Failed`, `Cancelled`), progress %, message and warnings |
| GET | `/api/v1/trip-searches/{id}/plans?page&pageSize&sort=Rank\|TotalCost\|TotalCostDesc\|RemainingBudget\|DepartureDate` | Paged plan summaries (flight legs carry their connection cities) |
| GET | `/api/v1/trip-searches/{id}/flights` | Every flight the search found (all destinations and dates, up to `MaxFoundFlights`), cheapest first, with separate tickets for combinations |
| POST | `/api/v1/trip-searches/{id}/cancel` | Cancel a queued or running search (409 if already finished) |
| GET | `/api/v1/destinations?search&countryCode&page&pageSize` | Catalog |
| GET | `/api/v1/destinations/{id}` | Destination with airports and official transport links |
| GET | `/api/v1/destinations/{id}/attractions` | Best-known sights with Wikipedia summary, nearest stops and lines, directions |
| GET | `/api/v1/airports?departureOnly=true&search` | Departure airports worldwide (world snapshot), best matches first; without `search` only the curated ones |
| GET | `/api/v1/airports/nearest` | Departure airport nearest to the visitor's IP (Travelpayouts `whereami`); 204 when unknown |
| GET | `/api/v1/places?search` | Cities worldwide with a flightable airport, for destinations outside the catalog |
| GET | `/api/v1/trip-plans/{id}` | Itemized plan: cost breakdown, sources, timestamps and details |
| POST | `/api/v1/trip-plans/{id}/refresh-prices` | Re-queries the original providers, re-matches the same flight (itinerary signature) and property, then recalculates the budget. Rate-limited. |
| GET | `/api/v1/trip-plans/{id}/outbound-links` | Allow-listed booking, information and map links |
| GET | `/api/v1/trip-plans/{id}/hotel-prices` | Indicative nightly hotel prices per agency for the plan's dates (204 when unavailable). Not part of the total. |
| GET | `/api/v1/trip-plans/{id}/nearby` | Hotels near the arrival airport and the centre, transit lines at the airport and taxis, with Booking.com, Google Maps and Uber links pre-filled from the plan |
| POST | `/api/v1/auth/external` | `{ provider, token }` (provider `Google` or `Facebook`): verifies the provider token, creates the user on first sign-in, returns this API's bearer token (401 when rejected). Rate-limited. |
| GET | `/api/v1/auth/me` | The signed-in user (`Authorization: Bearer …`; 401 otherwise) |
| GET | `/api/v1/client-config` | Data mode, active providers, input limits and the public sign-in ids (no secrets) |
| GET | `/health/live`, `/health/ready` | Liveness, and readiness including the database |

Errors are RFC 7807 problem details: 400 with per-field `errors`, 404, 409, 429 with `Retry-After`, and 500 with no
internal details. Swagger UI is at `/swagger`.

---

## 6. How planning works

**Trip semantics.** A trip of *N* days covers *N* calendar days including the departure and return days, which
means **N − 1 nights**. A 5-day trip departing 20 Nov returns 24 Nov, with check-in on the 20th and check-out on the 24th. Every
departure date that fits `[earliest departure, latest return]` is valid (20–26 Nov in the example: 7 combinations).

**Pipeline (bounded fan-out):**
1. Generate the date combinations. If there are more than `MaxDateCombinations`, take an evenly spaced sample that keeps the first and last date.
2. Pick destinations: the selected ones in manual mode (catalog cities and/or any city of the world list, which becomes a non-catalog destination on first use), or the active catalog in automatic mode, optionally limited to the selected **continents**, ordered by popularity and capped at `MaxAutoDestinations`, excluding the origin city.
3. Search flights. Travelpayouts prices the **whole date window** in one job per destination (one call per departure/return month pair, filtered to the exact trip length), so every departure day is checked; other providers are called per destination × sampled date, within **`MaxFlightSearches`**. Preferences (stops, max duration, departure window, checked bag) and expiry are applied.
   - **Separate tickets (self-transfer, on by default):** a second job per destination combines one-way fares, directly or through a connecting city. Connecting cities are those that are cheap both from the origin and to the destination (found with origin-only and destination-only queries), topped up with known hubs (IST, MIL, BUD, DXB, DOH, SIN, …); at most 3 per direction. A connection needs 3–24 h (5 h when the airport changes). Each ticket keeps its own booking link, and the UI warns that the connection is unprotected.
   - **Connection cities of a single ticket** come from the `t` parameter of the Aviasales link (e.g. `TBSURCTFUPEKSYD`). It is undocumented, so a route that doesn't match the ticket is ignored.
   - Every offer found is kept (`MaxFoundFlights`) for the flights list, which the UI sorts cheapest or most expensive first and filters by price, stops, ticket type and city.
4. Shortlist the cheapest affordable date combinations per destination (`MaxCombosPerDestinationForAccommodation`).
5. Search accommodation **once per unique (destination, check-in, check-out, guests)**, not once per flight. In-memory caches with short TTLs sit in front of the providers.
6. Build candidates (flight × top N stays), run the budget pre-check and a preliminary ranking.
7. Look up transfers **only for the finalists**, deduplicated per (airport, property). Providers run under a semaphore (`MaxConcurrentProviderCalls`) with per-call timeouts and Polly retries (honouring `Retry-After`) plus a circuit breaker. Every call takes a cancellation token.
8. Final budget, then filter out over-budget plans, rank, and diversify (at most `MaxPlansPerDestination` per city). Return `MaxResults` plans.
   - If nothing fits, the search completes as **`CompletedNoMatch`** and returns the closest options clearly marked `OverBudget`.
   - If even the flights exceed the budget, accommodation isn't searched at all.

Worst case for the default settings: 40 flight calls, at most 24 accommodation calls (12 destinations × 2 date options) and
at most 10 × 2 route calls.

**Trade-offs:**
- Sampling the dates can miss the single cheapest day. Raise `MaxFlightSearches` or use manual mode with fewer cities for exhaustive date coverage.
- Ranking is relative within one search, so scores aren't comparable across searches.

**Ranking (transparent, stored per plan and shown in the UI):** the score runs 0–100 and combines:
- cost (40%)
- flight duration plus 2 h per stop (25%)
- accommodation stars and review score (15%)
- transfer time: ≤ 30 min is best, ≥ 90 min is worst (10%)
- budget remaining (10%)

Plans missing a required cost category are multiplied by 0.85. All weights are configurable.

**Budget rules (`BudgetCalculator`):**
- Amounts are `decimal`, rounded to cents.
- A line in another currency is converted **only** with an ECB rate, and the rate, source and date are stored on the item. Without a rate, the line is excluded, its category is reported as missing, and a warning is added.
- Fees the provider says are already included in its price are shown as informational lines (`includedInTotal=false`) and **never counted twice**. Fees that aren't included are added. For Booking.com, `total` already contains charges payable at the property, so the difference to `book` is shown, not added.
- Required categories are Flights, Accommodation, Airport transfers, and any mandatory fees not included in another price. If any of these has an unknown cost, the plan is `WithinBudgetIncomplete`, never `WithinBudget`.
- Optional daily estimates and contingency are added only when they are configured **and** the user opts in.
- Fit statuses are `WithinBudget`, `WithinBudgetIncomplete`, `OverBudget` and `Unknown`.

**Verification labels:**

| Label | Meaning |
|---|---|
| `Live` | Priced from the provider during this search; has an expiry |
| `Cached` | Provider-side cached indication (Aviasales) |
| `Estimate` | Route fare, configured tariff or configured allowance |
| `Demo` | Generated demo data |
| `Verified` | Curated official link |
| `Unavailable` | No data could be obtained |

Expired items are flagged in the API and UI and can be refreshed.

---

## 6a. Languages (Georgian / English)

- **Frontend:** URLs carry the language: `/ka/...` (default) and `/en/...`. A ქართ / ENG switcher keeps the current page. The choice is stored in localStorage, and old unprefixed links redirect. Dictionaries are in `frontend/travel-planner-web/src/app/core/i18n/` (`en.ts`, `ka.ts`; key parity is enforced at compile time and by tests).
- **Backend:** the language comes from `Accept-Language` (or `?culture=ka`). Validation errors, problem details, catalog names (cities, countries, airports, transit links), provider notes and client-config are returned in that language. Texts are in `src/TravelPlanner.Application/Common/Localization.cs`.
- **Search language:** a search stores the language it was created in (`trip_searches.Language`). Its progress messages, warnings and plan item titles are generated in that language, including dates such as "ხუთ, 26 ნოე".
- **Catalog translations:** `translations` / `nameTranslations` / `cityTranslations` in `catalog/destinations.json`. Missing translations fall back to English.

## 7. Security and reliability

- Secrets come only from environment variables or user secrets, and `.env` is git-ignored. Provider keys are sent in headers (not query strings) and never logged or returned. `HttpClient` logging is set to Warning.
- FluentValidation runs through a MediatR pipeline on every command and query, mirrored by client-side validation.
- **Outbound links:**
  - Every URL shown to users must be absolute HTTPS on an allow-listed host, with no credentials and no custom port.
  - Users can't supply URLs, and the server never fetches provider URLs. Provider base URLs come from configuration only (no SSRF).
  - The UI opens links with `rel="noopener noreferrer"`.
- Rate limiting is per IP (global, search creation and price refresh). CORS is restricted to configured origins and methods.
- Responses carry security headers: `nosniff`, `X-Frame-Options: DENY` and `Referrer-Policy: no-referrer`.
- Problem details never include stack traces or exception messages for 500 errors.
- TLS validation is never disabled.
- Background jobs survive restarts: on startup, searches that were `Running` are marked failed ("interrupted") and `Queued` searches are re-queued.
- There's no authentication. Searches and plans are anonymous and addressable by unguessable GUIDs, and there's no user-specific saved data (see §10).

---

## 8. Tests

```bash
dotnet test --project tests/TravelPlanner.UnitTests          # 72 tests, no Docker needed
dotnet test --project tests/TravelPlanner.IntegrationTests   # 18 tests, needs Docker (Testcontainers PostgreSQL)
dotnet test                                                  # everything (MTP runner, see global.json)
```

- **Unit tests** cover:
  - date combinations and 5-day/4-night semantics
  - budget totals, currency mismatch with and without rates, rounding, fee double-counting, contingency, incomplete and unknown fits
  - flight preference filtering and expiry
  - ranking, the outbound-link allow-list (SSRF and look-alike hosts)
  - transfer cost selection
  - the planning engine with mocked providers: automatic and manual modes, request caps, accommodation deduplication, over-budget fallback, missing providers, provider failures, currency conversion
  - provider mappers (Amadeus, Travelpayouts, Booking.com, Google Routes, ECB) using recorded payloads
- **Integration tests** run the real API against a throw-away PostgreSQL container in demo mode, with no paid APIs. They cover:
  - catalog seeding
  - the full 202 → poll → plans → detail → links → refresh workflow
  - manual mode persistence
  - no-match results and Live mode without providers
  - validation (400), 404s, malformed JSON, paging, rate limiting (429), health, security headers and CORS

  Without Docker they are **skipped** with a reason, not passed.
- **Frontend:** `cd frontend/travel-planner-web && npx ng test --watch=false` runs 30 tests with Vitest + jsdom, no browser needed. They cover TimeSpan parsing and formatting, the safe-link component, cross-field date validators, the map URL builder and the demo banner. `npx ng build` produces `dist/travel-planner-web/browser`.

---

## 9. Project layout

```
src/
  TravelPlanner.Domain/          entities, enums, TravelDates (no EF / provider dependencies)
  TravelPlanner.Application/     use cases (MediatR), validators, DTOs, provider abstractions,
                                 planning engine, budget calculator, ranking, link policy
  TravelPlanner.Infrastructure/  EF Core + migrations, catalog seeder, background job queue/worker,
                                 providers: Amadeus, Travelpayouts, Booking.com, Google Routes, ECB, Demo
  TravelPlanner.API/             controllers, problem details, rate limiting, CORS, health, Swagger,
                                 catalog/destinations.json, Dockerfile
tests/
  TravelPlanner.UnitTests/
  TravelPlanner.IntegrationTests/
frontend/travel-planner-web/     Angular app (+ nginx Dockerfile)
docker-compose.yml, .env.example
```

Provider abstractions live in Application (`IFlightSearchProvider`, `IFlightOfferMapper<T>`,
`IAccommodationSearchProvider`, `ITransportSearchProvider`, `IExchangeRateProvider`). Adding a provider means one
class in Infrastructure plus one registration in `Infrastructure/DependencyInjection.cs`.

---

## 10. Known limitations and next steps

- **No live accommodation or flight fares without partner access.** Amadeus Self-Service no longer exists and Booking.com requires an affiliate contract. Travelpayouts (cached prices) and Google Routes can be enabled today.
- The Booking.com integration follows the published 3.1 request and response structure, but it **has not been exercised against the real API**. Validate it in the Booking.com sandbox, and verify the catalog's city IDs.
- Amadeus has no consumer booking URL, so its flights show "book with the airline". Travelpayouts provides Aviasales links.
- Transfer cost is an estimate (transit fare from Google, or a configured tariff). No legitimate self-serve taxi-quote API was found, and no taxi operators were invented.
- Transfer routes use the airport → accommodation route for both directions and the current time, not the actual arrival time.
- Background jobs are in-process (`Channel`). For multiple API instances, move to a durable queue (e.g. PostgreSQL `SKIP LOCKED` or a message broker) and a distributed cache.
- There's no authentication or saved user trips. Add ASP.NET Core Identity or OIDC plus ownership checks before storing personal data.
- Next steps:
  - provider-specific rate-limit budgets
  - nearby-airport support for multi-airport cities
  - a cheap-price pre-filter (Travelpayouts) before calling expensive live APIs in automatic mode
  - an observability exporter (OpenTelemetry)

MediatR is pinned to **12.5.0**, the last Apache-2.0 release; 13 and later require a commercial license key.
