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
| `Planning:*` | Request limits, concurrency, timeouts, result count and ranking weights (see §6). |
| `Budget:*` | Optional daily food and local-transport allowance per person, and a contingency percentage. Empty means disabled. |
| `OutboundLinks:AllowedHosts` | Allow-list for every URL shown to users (exact host or subdomain, HTTPS only). |
| `Cors:AllowedOrigins` | Frontend origins (default `http://localhost:4200`). |
| `RateLimiting:*` | Per-IP limits: global, search creation and price refresh. |
| `Providers:*` | Mode, cache TTLs, HTTP resilience (attempt and total timeout, retries) and per-provider credentials. |

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
| GET | `/api/v1/trip-searches/{id}/plans?page&pageSize&sort=Rank\|TotalCost\|RemainingBudget\|DepartureDate` | Paged plan summaries |
| POST | `/api/v1/trip-searches/{id}/cancel` | Cancel a queued or running search (409 if already finished) |
| GET | `/api/v1/destinations?search&countryCode&page&pageSize` | Catalog |
| GET | `/api/v1/destinations/{id}` | Destination with airports and official transport links |
| GET | `/api/v1/airports?departureOnly=true` | Departure selector |
| GET | `/api/v1/trip-plans/{id}` | Itemized plan: cost breakdown, sources, timestamps and details |
| POST | `/api/v1/trip-plans/{id}/refresh-prices` | Re-queries the original providers, re-matches the same flight (itinerary signature) and property, then recalculates the budget. Rate-limited. |
| GET | `/api/v1/trip-plans/{id}/outbound-links` | Allow-listed booking, information and map links |
| GET | `/api/v1/client-config` | Data mode, active providers and input limits (no secrets) |
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
2. Pick destinations: the selected ones in manual mode, or the active catalog in automatic mode, ordered by popularity and capped at `MaxAutoDestinations`, excluding the origin city.
3. Search flights for each destination × date × provider, capped at **`MaxFlightSearches`** calls per search. If the cap would be exceeded, fewer dates per destination are checked, evenly spread, and the user sees a warning. Preferences (stops, max duration, departure window, checked bag) and expiry are applied.
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
