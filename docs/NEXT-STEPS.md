# სად გავჩერდით და რა არის შემდეგი (2026-11-02, საღამო)

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
| **ჩემი მოგზაურობა** – გეგმის „მიმოხილვაში“ 3 ნაბიჯი: შენახვა დრაფტად (არჩეული სასტუმროთი) → „ბილეთები და სასტუმრო შევიძინე“ → დღეების აღნიშვნა 🔒, დასრულებისას 🏆. სია: ანგარიშის მენიუ → „ჩემი მოგზაურობები“ (დრაფტი/შეძენილი/დასრულებული) | `features/plan/trip-diary.component.ts`, `shared/account-menu.component.ts` |
| **შეხსენების წერილები** (Brevo): გაფრენის დღეს 06:00-ზე, ან წინა საღამოს 20:00-ზე, თუ ფრენა 09:00-მდეა (მშვიდობიანი ფრენა + მადლობა); მოგზაურობის მე-2 დღეს 08:00-ზე (დღეების აღნიშვნა, შთაბეჭდილებები, ლიდერბორდი). დრო: თბილისის, ყოველი წერილი ერთხელ | `Application/Features/Journeys/TripReminders.cs`, `Infrastructure/Journeys/TripReminderEmails.cs` |
| **მოგზაურთა ლიდერბორდი** – მთავარ გვერდზე (`#leaderboard`): კვარცხლბეკი 🥇🥈🥉, სია 4–10, „ნიმუში“/„შენ“, „როგორ მოხვდე აქ“ | `features/search/leaderboard.component.ts` |
| journeys backend (`/api/v1/journeys`, `/api/v1/leaderboard`), ნიმუში მოგზაურები: `catalog/leaderboard-samples.json` (გამორთვა: `Leaderboard:IncludeSamples=false`) | `Features/Journeys/`, მიგრაცია `AddJourneys` |

## 🟡 შესამოწმებელი
- **Brevo-ს გასაღები აკლია**: სანამ არ ჩაიწერება (`Email:BrevoApiKey`, `Email:FromAddress`, იხ. RUNBOOK §2.3), წერილები არ იგზავნება. შეძენილი მოგზაურობის წერილები ლოდინში რჩება და გასაღების ჩაწერის შემდეგ გაიგზავნება, თუ მათი დრო ჯერ არ გასულა. გასაღების შემდეგ ერთი სატესტო მოგზაურობით შეამოწმე რეალური წერილი.
- დასრულების 🏆 ბანერი რეალურ დასრულებულ მოგზაურობაზე ჯერ ბრაუზერში არ მინახავს (backend-ის ტესტები ფარავს); პირველი დასრულებული მოგზაურობისას შეამოწმე.

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
