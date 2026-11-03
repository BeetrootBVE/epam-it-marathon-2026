# EPAM IT Marathon 2026 — AQA #8 · Напрям 1 / Level 1

**Напрям:** 1 (власний розгорнутий Backend + Frontend без API-інтеграції). **Підхід:** NUnit + Playwright .NET, незалежні API tests + мінімальний UI smoke, тонкий REST client / PageTest; без BDD, щоб не ускладнювати L1.

## Об'єкт тестування

- Власний AWS REST API `restaurant-backend-api` (stage `/develop`, Cognito, Lambda, DynamoDB), не mocked backend.
- Власний Angular frontend `frontend/restaurant-frontend-app`, локально `http://127.0.0.1:4200` в CI та під час локального запуску.
- Основний endpoint: `GET /guest/groups/{groupReservationId}/sessions`.
- Доступність API, авторизований HTTP 200, реальні тестові ролі, стіл та страва мають бути окремо перевірені на розгорнутому середовищі. Сама наявність handler у Git не гарантує працездатності сервісу.

## L1 coverage

| Тест | Перевірка |
|---|---|
| Owner sees guests | 2 учасники, страви/кількість/суми, submitted та pending |
| Cart changes | Загальна сума перераховується після зменшення кількості |
| Pending and empty carts | Обидва гості присутні, коректні статуси й склад |
| Empty group | `participants: []`, `total: 0` |
| Owner-only (4 cases) | Інший клієнт, офіціант, гість із session token, анонімний запит |
| Unknown group | 404 для неіснуючого group ID |
| UI minimal smoke | Відкриття `/menu`, заголовок, категорія, сортування та URL-параметри на Angular frontend |

UI smoke перевіряє елементи меню та навігацію, а не наявність страв: середовище раніше повертало порожні локації. Для повноцінної перевірки каталогу потрібно створити дані. Це обмеження задокументовано, а не приховано.

## Конфігурація

1. Встановіть **.NET SDK 10**, Node.js `24.16+` або сумісний Node.js `22.22.3+` та Playwright Chromium.
2. Скопіюйте `.env.example` у `.env` у цій папці й заповніть власні значення. НЕ комітьте `.env`.
3. У Cognito потрібні **три реальні тестові облікові записи**: Owner(Customer), інший Customer та Waiter. Паролі зберігайте тільки в `.env` або GitHub Actions secrets.
4. `AQA_TABLE_ID`: існуючий доступний стіл місткістю принаймні 4 у локації, що має доступну страву `AQA_DISH_ID`.
5. `AQA_BASE_URL`: REST API Gateway із stage `/develop`. Приклад формату — в `.env.example`, жодні реальні ключі не закодовані.

**Важливо:** `GET /locations` раніше повертав HTTP 200 і `[]`. Якщо тестових локацій, столів або страв немає, підготовка даних у власному AWS необхідна, перш ніж можна чесно назвати L1 виконаним. Автотести не будуть самовільно створювати admin-контент.

## Локальний запуск (PowerShell)

Виконайте у корені репозиторію:

```powershell
Copy-Item aqa/restaurant-aqa/.env.example aqa/restaurant-aqa/.env
# відредагуйте локальний .env без коміту
cd frontend/restaurant-frontend-app
npm ci
npm start -- --host 127.0.0.1 --port 4200
```

В іншому терміналі (корінь репозиторію):

```powershell
dotnet build aqa/restaurant-aqa/RestaurantAqa.csproj
pwsh aqa/restaurant-aqa/bin/Debug/net10.0/playwright.ps1 install chromium
dotnet test aqa/restaurant-aqa/RestaurantAqa.csproj --filter 'TestCategory=L1' --logger 'trx;LogFileName=aqa.trx' --results-directory aqa/restaurant-aqa/TestResults
```

Якщо Windows PowerShell без `pwsh`, використовуйте `powershell -File aqa/restaurant-aqa/bin/Debug/net10.0/playwright.ps1 install chromium`.

Підмножина API: `dotnet test aqa/restaurant-aqa/RestaurantAqa.csproj --filter 'TestCategory=API'`.
Підмножина UI: `dotnet test aqa/restaurant-aqa/RestaurantAqa.csproj --filter 'TestCategory=UI'`.

Підсумок NUnit відображається в терміналі; файл `.trx` — у `aqa/restaurant-aqa/TestResults/`. Для невдалого UI-тесту створюються screenshot і Playwright trace у `TestResults/artifacts/`.

## CI — GitHub Actions

Workflow `.github/workflows/aqa-tests.yml` збирає тестовий проєкт, запускає реальний Angular frontend, виконує API та UI сценарії, завантажує TRX та матеріали помилок як artifact. Невдалий тест не перешкоджає публікації звіту, але CI-job після публікації лишається **failed**, не маскує дефект.

У Settings → Secrets and variables → Actions додайте:

**Repository variables:** `AQA_BASE_URL`, `AQA_TABLE_ID`, `AQA_DISH_ID`.

**Repository secrets:** `AQA_OWNER_EMAIL`, `AQA_OWNER_PASSWORD`, `AQA_OTHER_EMAIL`, `AQA_OTHER_PASSWORD`, `AQA_WAITER_EMAIL`, `AQA_WAITER_PASSWORD`.

У workflow спеціально **не** записуються паролі, JWT, invite/session tokens або повні відповіді auth API.

## Прийняті очікування й граничні випадки

- Порожня група → **HTTP 200**, `participants=[]`, `total=0`.
- Авторизований не-Owner → **403** (власна логіка `ValidateReservationOwnerAsync`).
- Гостевий токен або відсутня авторизація → **401 чи 403**, оскільки Cognito authorizer / API Gateway може відхилити запит до Lambda.
- Неіснуюча група з owner Cognito ID Token → **404**.
- `GrandTotal` у Backend L1 включає всі сесії, **навіть якщо cart не submitted**; не плутати з правилом FE L2.
- Порядок учасників не фіксований: тест шукає їх за session ID.
- Owner session може не створюватися автоматично: тести не припускають обов'язкової наявності Owner в `participants`.
- AQA створює унікальні гостьові сесії та майбутні часові слоти. У `TearDown` намагається видалити гостьові сесії та скасувати бронювання через чинний API; запис бронювання/групи може лишатися з ознакою cancelled/closed, оскільки повноцінного hard-delete endpoint немає. Статус cleanup перевіряйте за потреби вручну.
- Набір запускається послідовно в межах класу; різні групи використовують випадкові майбутні часові слоти для уникнення конфліктів на одному тестовому столі. За строгого паралельного запуску рекомендується пул різних test table IDs.

## Статус перевірки

Вихідні файли AQA підготовлені за кодом Backend/Frontend у репозиторії, однак до першого успішного запуску на налаштованому AWS тестовому середовищі **не можна ставити Passed**. Потрібно зафіксувати фактичний TRX, GitHub Actions run та публічне посилання на repository перед здачею EPAM.

Додаткові бонуси (Report Portal + cloud-provider pipeline) для L1 не реалізовані; GitHub Actions забезпечує базовий CI.
