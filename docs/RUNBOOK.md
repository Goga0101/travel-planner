# Tripora (travel-planner): გაშვების სახელმძღვანელო

ეს დოკუმენტი აღწერს, როგორ მუშაობს პროექტი, როგორ გაუშვა ის საკუთარ კომპიუტერზე ხელით და როგორ გამოჩნდეს
საჯარო საიტზე. ბრძანებები Windows-ისთვისაა (PowerShell), პროექტის ძირითადი საქაღალდიდან.

- რეპოზიტორია: https://github.com/Goga0101/travel-planner
- საჯარო საიტი: https://goga0101.github.io/travel-planner/ka

---

## 1. როგორ არის აწყობილი

```
ბრაუზერი ──► GitHub Pages (Angular საიტი, სტატიკური ფაილები)
                 │  config.json-ში წერია API-ს მისამართი (Cloudflare tunnel)
                 ▼
        Cloudflare quick tunnel  https://<შემთხვევითი>.trycloudflare.com
                 │
                 ▼
        შენს კომპიუტერზე: .NET API  http://localhost:5080
                 │
                 ▼
        Docker: PostgreSQL 17  localhost:5433
```

| ნაწილი | ტექნოლოგია | სად არის |
|---|---|---|
| Backend (API) | .NET 10, ASP.NET Core, MediatR, EF Core, Clean Architecture | `src/` (Domain, Application, Infrastructure, API) |
| ბაზა | PostgreSQL 17, Docker-ში | `docker-compose.yml`, სერვისი `postgres` |
| Frontend | Angular (standalone, signals), ქართული/ინგლისური | `frontend/travel-planner-web` |
| საჯარო საიტი | GitHub Pages, ავტომატური deploy GitHub Actions-ით | `.github/workflows/deploy-frontend.yml` |
| API-ს გარეთ გამოტანა | Cloudflare quick tunnel | `scripts/start-public-api.ps1` |
| ტესტები | xUnit (backend), Vitest (frontend) | `tests/`, `*.spec.ts` |

**მთავარი:** საჯარო საიტი მხოლოდ ფრონტია. ძიება, ფრენები, ტრანსპორტი და შესვლა მუშაობს მაშინ, როცა შენს კომპიუტერზე
ჩართულია API და tunnel. კომპიუტერის გამორთვისას საიტი იხსნება, მაგრამ შედეგებს აღარ აჩვენებს.

---

## 2. ერთჯერადი მომზადება (ახალ კომპიუტერზე)

### 2.1 პროგრამები

```powershell
winget install Git.Git
winget install Microsoft.DotNet.SDK.10
winget install OpenJS.NodeJS.LTS          # Node 22 ან 24
winget install Docker.DockerDesktop
winget install Cloudflare.cloudflared
winget install GitHub.cli                  # არასავალდებულო: deploy-ის სანახავად
```

ინსტალაციის შემდეგ გადატვირთე კომპიუტერი (Docker-ს ეს სჭირდება) და ერთხელ გახსენი Docker Desktop.

### 2.2 კოდის ჩამოტვირთვა

```powershell
cd $HOME\source\repos
git clone https://github.com/Goga0101/travel-planner.git TravelPlanner
cd TravelPlanner
dotnet tool restore
cd frontend\travel-planner-web; npm install; cd ..\..
```

`git push`-ს GitHub-ის ავტორიზაცია სჭირდება, რადგან tunnel-ის სკრიპტი ახალ მისამართს თვითონ აგზავნის. პირველ push-ზე
ბრაუზერი გაიხსნება და შეხვალ; ან გაუშვი `gh auth login`.

### 2.3 საიდუმლო გასაღებები (user-secrets)

გასაღებები **არ** ინახება კოდში. ისინი კომპიუტერზეა, `dotnet user-secrets`-ში. ახალ კომპიუტერზე ხელახლა უნდა ჩაიწეროს:

```powershell
$api = "src/TravelPlanner.API"

# რეალური მონაცემები (დემო რეჟიმის ნაცვლად)
dotnet user-secrets set "Providers:Mode" "Live" --project $api
dotnet user-secrets set "Providers:Travelpayouts:Enabled" "true" --project $api
dotnet user-secrets set "Providers:Travelpayouts:ApiToken" "<Travelpayouts API token>" --project $api
dotnet user-secrets set "Providers:Travelpayouts:Marker" "<Travelpayouts marker>" --project $api
dotnet user-secrets set "Providers:Geoapify:ApiKey" "<Geoapify key>" --project $api
dotnet user-secrets set "Providers:Ticketmaster:ApiKey" "<Ticketmaster key>" --project $api

# შესვლა (Google / Facebook)
dotnet user-secrets set "Auth:Google:ClientId" "872910671859-o81bdgaqf2jjksmv7u6t1q41r802u273.apps.googleusercontent.com" --project $api
# Facebook, როცა აპი შეიქმნება:
# dotnet user-secrets set "Auth:Facebook:AppId" "<App ID>" --project $api
# dotnet user-secrets set "Auth:Facebook:AppSecret" "<App Secret>" --project $api

# ჩვენი შესვლის ბილეთის (JWT) ხელმოწერის გასაღები: შემთხვევითი, ერთხელ
$b = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
dotnet user-secrets set "Auth:Jwt:Key" ([Convert]::ToBase64String($b)) --project $api

dotnet user-secrets list --project $api    # შემოწმება (მნიშვნელობებს აჩვენებს, ფრთხილად!)
```

| გასაღები | საიდან |
|---|---|
| Travelpayouts token და marker | travelpayouts.com → Profile → API token; marker: Partner ID |
| Geoapify | myprojects.geoapify.com → API key (სასტუმროები/ადგილები რუკაზე) |
| Ticketmaster | developer.ticketmaster.com → My Apps → Consumer Key (ღონისძიებები) |
| Google Client ID | console.cloud.google.com → პროექტი `tripora` → Google Auth Platform → Clients |
| Facebook App ID / Secret | developers.facebook.com/apps → აპი → Settings → Basic |

Client ID და App ID საჯაროა. Token-ები, App Secret და JWT Key საიდუმლოა: არ ჩაწერო კოდში, ჩატში ან screenshot-ში.

`Providers:Mode` თუ არ არის `Live`, საიტი **დემო რეჟიმში** იმუშავებს (გამოგონილი მონაცემებით, წითელი DEMO ზოლით).

---

## 3. ყოველდღიური გაშვება (4 ნაბიჯი)

ყველა ბრძანება პროექტის საქაღალდიდან: `cd $HOME\source\repos\TravelPlanner`

### ნაბიჯი 1: Docker Desktop და ბაზა

გახსენი Docker Desktop და დაელოდე, სანამ „Engine running" გამოჩნდება. შემდეგ:

```powershell
docker compose up -d postgres
docker ps            # travel-planner-postgres-1 ... (healthy)
```

### ნაბიჯი 2: Backend (API): ცალკე ფანჯარაში, ღიად დატოვე

```powershell
dotnet run --project src/TravelPlanner.API --launch-profile http
```

როცა ჩანს `Now listening on: http://localhost:5080`, მზადაა. პირველ გაშვებაზე ბაზის ცხრილები (migrations) და
მიმართულებების კატალოგი თავისით იქმნება. შემოწმება: http://localhost:5080/swagger

### ნაბიჯი 3: Tunnel (საჯარო საიტისთვის): ცალკე ფანჯარაში, ღიად დატოვე

```powershell
powershell -ExecutionPolicy Bypass -File scripts/start-public-api.ps1
```

სკრიპტი:
1. ამოწმებს, რომ API ჩართულია;
2. რთავს Cloudflare tunnel-ს და იღებს ახალ მისამართს (`https://….trycloudflare.com`, **ყოველ ჯერზე სხვაა**);
3. თუ მისამართი შეიცვალა, წერს მას `frontend/travel-planner-web/gh-pages-api-url.txt`-ში, აკეთებს commit-ს და push-ს;
4. push-ის შემდეგ GitHub Actions 1–2 წუთში ხელახლა აქვეყნებს საიტს ახალი მისამართით.

შემდეგ გახსენი https://goga0101.github.io/travel-planner/ka (Ctrl+F5).

### ნაბიჯი 4 (არასავალდებულო): Frontend ლოკალურად, დეველოპმენტისთვის

მხოლოდ კოდის შეცვლისას გჭირდება, საჯარო საიტისთვის საჭირო არ არის:

```powershell
cd frontend\travel-planner-web
npm start            # http://localhost:4200, /api მიდის localhost:5080-ზე
```

### გაჩერება

API-სა და tunnel-ის ფანჯრებში დააჭირე Ctrl+C. ბაზა: `docker compose stop postgres` (მონაცემები რჩება).

---

## 4. კოდის შეცვლა და გამოქვეყნება

```powershell
# backend ტესტები (dotnet test ზოგჯერ „0 tests"-ს წერს, ამიტომ exe პირდაპირ):
dotnet build
.\tests\TravelPlanner.UnitTests\bin\Debug\net10.0\TravelPlanner.UnitTests.exe

# frontend
cd frontend\travel-planner-web
npx tsc --noEmit -p tsconfig.app.json
npx ng test --watch=false
npx ng build --configuration production

# გამოქვეყნება: push main-ზე → GitHub Actions აწყობს და აქვეყნებს საიტს
git add -A; git commit -m "აღწერა"; git push
```

Backend-ის ცვლილებები საჯარო საიტზე მაშინ ჩანს, როცა API-ს **თავიდან გაუშვებ** (Ctrl+C და ისევ `dotnet run`).
Tunnel-ის თავიდან გაშვება ამისთვის საჭირო არ არის: tunnel უბრალოდ localhost:5080-ს ატარებს.

ბაზის სტრუქტურის შეცვლისას (ახალი ცხრილი/ველი):

```powershell
dotnet ef migrations add <Name> --project src/TravelPlanner.Infrastructure --startup-project src/TravelPlanner.API --output-dir Persistence/Migrations
```

Development-ში migration API-ს გაშვებისას თავისით ეშვება.

---

## 5. შესვლა Google-ით და Facebook-ით

**როგორ მუშაობს:**
1. ბრაუზერი პროვაიდერის ოფიციალური ღილაკით იღებს token-ს: Google-ისგან ID token-ს, Facebook-ისგან access token-ს.
2. საიტი მას აგზავნის: `POST /api/v1/auth/external` `{ provider, token }`.
3. API ამოწმებს token-ს პროვაიდერთან:
   - Google: ხელმოწერა Google-ის გასაღებით, ვადა და `aud` = ჩვენი Client ID;
   - Facebook: `debug_token` ჩვენი App ID-ით, შემდეგ პროფილი `appsecret_proof`-ით.
4. პირველ შესვლაზე ქმნის მომხმარებელს (ცხრილები `users`, `user_logins`). ერთი და იგივე დადასტურებული ელფოსტა
   Google-სა და Facebook-ს ერთ ანგარიშად აერთიანებს.
5. აბრუნებს **ჩვენს** JWT-ს (30 დღე). ბრაუზერი მას `localStorage`-ში ინახავს და აგზავნის `Authorization: Bearer`-ით.
6. `GET /api/v1/auth/me` აბრუნებს მომხმარებელს, `DELETE /api/v1/auth/me` ანგარიშს შლის.

**სად არის კოდი:**

| ფაილი | რა აკეთებს |
|---|---|
| `src/TravelPlanner.Domain/Entities/User.cs` | User, UserLogin |
| `src/TravelPlanner.Application/Features/Auth/ExternalLogin.cs` | შესვლის ლოგიკა, მომხმარებლის შექმნა/დაკავშირება, წაშლა |
| `src/TravelPlanner.Infrastructure/Auth/` | Google/Facebook შემოწმება, JWT-ს გაცემა, პარამეტრები |
| `src/TravelPlanner.API/Controllers/AuthController.cs` | `/auth/external`, `/auth/me` |
| `frontend/.../core/auth.service.ts` | SDK-ების ჩატვირთვა, token-ის შენახვა, Authorization header |
| `frontend/.../shared/account-menu.component.ts` | „შესვლა" ღილაკი და ფანჯარა ზედა მარჯვენა კუთხეში |
| `frontend/.../features/legal/privacy.page.ts` | კონფიდენციალურობის პოლიტიკა (`/ka/privacy`) |

**Google (უკვე გაკეთებულია):** პროექტი `tripora`, Google Auth Platform:
- Branding: App name Tripora, home page `https://goga0101.github.io/travel-planner/`, privacy
  `https://goga0101.github.io/travel-planner/en/privacy/`, authorized domain `goga0101.github.io`;
- Clients → Web application → JavaScript origins: `https://goga0101.github.io`, `http://localhost:4200`, `http://localhost`;
- Audience → **In production**.

**Facebook (დარჩენილი):** developers.facebook.com/apps → Create app → „Authenticate and request data from users
with Facebook Login":
1. Settings → Basic: App domain `goga0101.github.io`, Privacy Policy URL
   `https://goga0101.github.io/travel-planner/en/privacy/`, User data deletion: Instructions URL
   `https://goga0101.github.io/travel-planner/en/privacy/`. აქედან აიღე App ID და App Secret.
2. Facebook Login → Settings: ჩართე „Login with the JavaScript SDK"; Allowed domains:
   `https://goga0101.github.io/`, `http://localhost:4200/`.
3. Permissions: `public_profile`, `email`.
4. App Mode → **Live**.
5. ჩაწერე App ID და App Secret user-secrets-ში (§2.3) და თავიდან გაუშვი API.

ID-ები საიტამდე `/api/v1/client-config`-ით მიდის, ამიტომ frontend-ის ხელახლა აწყობა არ სჭირდება.

---

## 6. პრობლემები და გამოსავალი

| სიმპტომი | მიზეზი / გამოსავალი |
|---|---|
| საიტი იხსნება, მაგრამ ძიება/შესვლა არ მუშაობს | API ან tunnel გამორთულია → ნაბიჯები 2 და 3 |
| tunnel-ის სკრიპტი: „The API is not running" | ჯერ ჩართე API (ნაბიჯი 2) |
| tunnel-ის შემდეგ საიტი ისევ ძველ მისამართს იძახებს | დაელოდე 1–2 წუთს deploy-ს (GitHub → Actions), მერე Ctrl+F5 |
| `docker compose` ერორი | Docker Desktop არ არის ჩართული |
| `dotnet build`: „file is locked by TravelPlanner.API" | API ჩართულია; ჯერ გააჩერე (Ctrl+C) |
| port 5080 დაკავებულია | ძველი API ჯერ კიდევ მუშაობს: `Get-Process TravelPlanner.API \| Stop-Process` |
| `npm start` წითელ TS შეცდომას აჩვენებს, `tsc` კი სუფთაა | dev server-ის ქეში: გააჩერე და თავიდან `npm start` |
| საიტზე წითელი „DEMO" ზოლი | `Providers:Mode` არ არის `Live` (§2.3) |
| Google: „origin_mismatch" | Google Cloud → Clients → JavaScript origins-ში მისამართი აკლია |
| შესვლის ფანჯარაში „შესვლა მალე ჩაირთვება" | API-ში არ არის `Auth:Jwt:Key` ან პროვაიდერის ID |

---

## 7. სასარგებლო მისამართები

| რა | სად |
|---|---|
| Swagger (API-ს ყველა endpoint) | http://localhost:5080/swagger |
| API-ს ჯანმრთელობა | http://localhost:5080/health/ready |
| ლოკალური საიტი | http://localhost:4200/ka |
| საჯარო საიტი | https://goga0101.github.io/travel-planner/ka |
| Deploy-ის სტატუსი | https://github.com/Goga0101/travel-planner/actions |
| დეტალური ტექნიკური აღწერა | `README.md` (კონფიგურაცია §3, შესვლა §3a, API §5) |
