# სად გავჩერდით და რა არის შემდეგი (2026-11-02)

ხვალ Claude Code-ს უბრალოდ უთხარი: **„წაიკითხე docs/NEXT-STEPS.md და გავაგრძელოთ“**.

## ✅ გაკეთებულია (ყველაფერი GitHub-ზეა)

| ფუნქცია | სად არის |
|---|---|
| **მსოფლიო ამბები** – გვერდი `/ka/news`, ფილტრები, დალაგება | `frontend/.../features/news/world-news.page.ts`, `core/world-news.ts` |
| **ამბების აგენტი** `world-news-editor` – აგროვებს საზოგადოებრივი მაუწყებლებიდან, გეგმით მუშაობს | `.claude/agents/world-news-editor.md`, `scripts/news/` (`PLAN.md` – შემდეგი რაუნდის დავალებები) |
| **ამბების ღია ბარათები + ყუთი** (Stitch-ის Orange Edition) – ჩამოდის ზედა მარჯვენა კუთხიდან, მიფრინავს header-ის ყუთში | `shared/news-drop.component.ts`, `shared/news-inbox.component.ts`, `core/news-inbox.ts` |
| **სასტუმროს შეფასებები** (Tripadvisor Terra API, ბოლო 14 დღე, ბაზაში არაფერი ინახება) | backend: `Providers/Tripadvisor/`, frontend: `features/plan/hotel-reviews.component.ts` |
| **სასტუმროების განყოფილება** – Stitch-ის ქუჩის სცენა + მარშრუტის ზოლი; ჩაკეცვისას ცარიელი ადგილი აღარ რჩება, რბილი ანიმაცია | `features/plan/stay-illustrations.component.ts`, `nearby.component.html` |
| ქართული დრო („2 საათის წინ“), header-ის გადატანა ფართო ეკრანზე | `core/format.ts`, `styles.scss` |

## 🟡 ნახევრად გაკეთებული: მოგზაურობის დასრულება + ლიდერბორდი

**Backend მზადაა და დატესტილია** (175 ტესტი გადის):
- ცხრილი `journeys` (მიგრაცია `AddJourneys`), domain: `Domain/Entities/Journey.cs`
- API: `GET/PUT/DELETE /api/v1/journeys/{planId}`, `PUT /api/v1/journeys/{planId}/days/{yyyy-MM-dd}` `{ "done": true }`, `GET /api/v1/leaderboard`
- დღის მონიშვნა მხოლოდ დამდგარ დღეზე შეიძლება; ყველა დღე მონიშნული = მოგზაურობა დასრულებულია
- ლიდერბორდზე ჩანს: სახელი + გვარის ინიციალი („ნინო მ.“), მოგზაურობები, დღეები, ქვეყნები
- **ნიმუში მოგზაურები** (10 ცალი): `src/TravelPlanner.API/catalog/leaderboard-samples.json`. საიტზე უნდა ეწეროთ „ნიმუში“; გამორთვა: `Leaderboard:IncludeSamples=false`

**დარჩენილია (frontend):**
1. გეგმის გვერდზე ბლოკი „მოგზაურობის დღიური“: ღილაკი **„✓ დავჯავშნე“** (შესვლა საჭიროა; თუ არ არის შესული – გაიხსნას შესვლის პანელი: `AuthService`-ში `signInRequested` სიგნალი + `account-menu`-ში მოსმენა, `setTimeout`-ით, რადგან document click პანელს ხურავს)
2. დღეების ჩიპები (დღე 1…N, თარიღი), მონიშვნა/მოხსნა, მომავალი დღეები გათიშული 🔒, პროგრესის ზოლი, „გაუქმება“
3. დასრულებისას: ზეიმის ბანერი 🏆 + ბმული ლიდერბორდზე
4. მთავარ გვერდზე სექცია **„მოგზაურთა ლიდერბორდი“** (`features/search/trip-search.page.html`, `#how`-მდე): ტოპ-3 კვარცხლბეკი 🥇🥈🥉 + სია 4–10, ქვეყნების კოდები, „ნიმუში“ ნიშანი, „შენ“ ნიშანი, ახსნა „როგორ მოხვდე აქ“ (3 ნაბიჯი)
5. `core/models.ts`-ში `JourneyDto`, `LeaderboardDto`; `api.service.ts`-ში მეთოდები; `core/journeys.service.ts`
6. ქართული/ინგლისური ტექსტები, ტესტები, build, ბრაუზერში შემოწმება (desktop + 390px)
7. კონფიდენციალურობის გვერდზე (`/privacy`) ერთი წინადადება: დასრულებული მოგზაურობისას სახელი + ინიციალი ჩანს ლიდერბორდზე

## 🔑 გასაღებები / გარე სერვისები
- ჩაწერილია: Travelpayouts, Geoapify, Ticketmaster, Google Client ID, JWT (`secrets.local.ps1` – git-ში არ არის)
- **აკლია**: Tripadvisor Terra API key (სასტუმროს შეფასებებისთვის) – `secrets.local.ps1`-ში ხაზი უკვე გამზადებულია; Travelpayouts Marker
- push-ს თუ SSL შეცდომა აქვს: `git -c http.sslBackend=schannel push` (ან ერთხელ `git config --global http.sslBackend schannel`)

## 💡 იდეები შემდეგისთვის
- ტელეფონზე დაყენება: ღილაკი „აპლიკაციის დაყენება“ + service worker (PWA უკვე მუშაობს Chrome-ის მენიუდან)
- API სერვერზე გადატანა + დომენი (ახლა საიტი მუშაობს მხოლოდ როცა კომპიუტერი + tunnel ჩართულია)
- ამბების აგენტის შემდეგი რაუნდი: `scripts/news/PLAN.md` → „Next run“

## ⚙️ ლოკალური გაშვება (მეხსიერება ცოტაა – 0.8 GB თავისუფალი)
- Docker Desktop ხშირად იჭედება („500 Internal Server Error“) → დახურე და თავიდან გახსენი
- API: `dotnet build src/TravelPlanner.API -nodeReuse:false -p:UseSharedCompilation=false`, მერე
  `src/TravelPlanner.API`-დან `dotnet bin/Debug/net10.0/TravelPlanner.API.dll` (`ASPNETCORE_ENVIRONMENT=Development`, `ASPNETCORE_URLS=http://localhost:5080`)
- საიტი: `ng serve`-ის ნაცვლად აწყობილი ვერსია (`npx ng build --configuration production`) პატარა სერვერით – იხ. RUNBOOK §3
