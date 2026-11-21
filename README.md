# 🍱 BiteBox — *Every Bite, Anytime.*

BiteBox is a full-stack online food ordering and restaurant management system for a single restaurant.
It has a customer storefront, a role-based admin dashboard (**BiteBox Admin**), and **BiteBot**, an assistant that suggests dishes from the live menu.

| Layer | Technology |
|---|---|
| Frontend | Angular 22 (standalone components, signals, zoneless, Reactive Forms, lazy routes, guards, interceptors), TypeScript 6, SCSS |
| Backend | ASP.NET Core 9 Web API, C# 13, Clean Architecture (Domain / Application / Infrastructure / API) |
| Data | MySQL 8 with Entity Framework Core 9 (official Oracle `MySql.EntityFrameworkCore` provider), code-first migrations |
| Auth | ASP.NET Core Identity, HttpOnly cookie session, anti-forgery (CSRF) tokens, role and policy authorisation, lockout, rate limiting |
| AI | BiteBot: catalogue-grounded recommendations. Rule-based parser by default; optional Claude adapter for request understanding |
| Tests | xUnit, Moq, `WebApplicationFactory` integration tests on real MySQL, Vitest for Angular |
| DevOps | Dockerfiles, Docker Compose (MySQL 8.4 + API + nginx), GitHub Actions CI |

> **Why .NET 9 and not .NET 10?** This machine has only the .NET 9 SDK installed, and the brief said not to install anything new.
> Moving to .NET 10 / EF Core 10 means changing `TargetFramework`, `global.json` and the package versions (`MySql.EntityFrameworkCore` 10.x supports EF Core 10).
> Pomelo does **not** have an EF Core 10 release, which is why the official Oracle provider is used.

---

## Features

**Customers**
- Browse the menu by category, with search, veg-only, price, spice and stock filters and sorting. Filters live in the URL, so a filtered view can be shared.
- Dish pages show a veg/non-veg mark, spice level, prep time, servings, calories, rating, "only N left" and related dishes.
- A guest cart is kept in the browser and merged into the account cart on sign-in. Signed-in carts are stored on the server.
- Checkout:
  - delivery or pickup, saved addresses, coupons
  - **server-calculated** price breakdown (5% GST, delivery fee free above ₹499)
  - cash on delivery, or a **simulated** online payment
- Order tracking with a live status timeline (auto-refresh), cancel while not yet confirmed, reorder, and in-app notifications.
- Favourites, address book, profile and password change, and reviews (only for dishes you actually received).
- **BiteBot:** "spicy veg dinner for two under ₹600". It returns real dishes, a combo that fits the budget, and add-to-cart buttons.

**BiteBox Admin** (role-based)
- **Dashboard:** revenue, average order value, active orders, completed and cancelled counts, new customers, a revenue-per-day chart (with table view), top dishes, orders by status, low stock and latest orders, over any date range.
- **Orders:** a live kitchen queue plus filters and search. Action buttons are generated from the state machine, so each role sees only the moves it is allowed to make. Admins can cancel (stock is released) and record refunds.
- **Menu items:** create, edit, soft-delete, photo upload with signature validation, featured and availability flags.
- **Also:** categories, inventory (restock, wastage, correction with a full movement history), promotions (percentage or flat, caps, limits, validity windows), customers and staff (roles, block/unblock, create staff), and reports with CSV export.

**Roles:** `Customer`, `Kitchen`, `Delivery`, `Admin`. Every rule is enforced by the API. The Angular guards only shape the UI.

---

## Quick start (local development)

Prerequisites (already on the development machine): .NET SDK 9, Node 24, Angular CLI 22, MySQL 8.

### 1. Create the database user (once)

Run this in a terminal and enter your MySQL root password when prompted:

```sql
-- docs/setup-local-db.sql (choose your own password)
CREATE DATABASE IF NOT EXISTS bitebox_db CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
CREATE DATABASE IF NOT EXISTS bitebox_test CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
CREATE USER IF NOT EXISTS 'bitebox_app'@'localhost' IDENTIFIED BY '<your-password>';
GRANT ALL PRIVILEGES ON `bitebox\_%`.* TO 'bitebox_app'@'localhost';
```

```bash
mysql -u root -p < docs/setup-local-db.sql
```

### 2. Store the connection string as a user-secret (it never goes in the repo)

```bash
cd backend
dotnet user-secrets set "ConnectionStrings:BiteBox" "Server=127.0.0.1;Port=3306;Database=bitebox_db;User=bitebox_app;Password=<your-password>;" --project src/BiteBox.API
```

### 3. Run the API

```bash
cd backend
dotnet run --project src/BiteBox.API
```

In Development, startup applies EF Core migrations and seeds demo data: 45 dishes, 10 categories, 6 customers, about 45 days of orders, reviews and coupons.

- API: http://localhost:5080
- Swagger: http://localhost:5080/swagger
- Health: http://localhost:5080/health/ready

### 4. Run the Angular app

```bash
cd frontend/bitebox-web
npm install
npm start          # http://localhost:4200 (proxies /api to :5080)
```

### Demo accounts (password `BiteBox@2026`, set in `appsettings.Development.json`)

| Role | Email |
|---|---|
| Admin | `admin@bitebox.dev` |
| Kitchen | `kitchen@bitebox.dev` |
| Delivery | `delivery@bitebox.dev` |
| Customer | `priya@example.com` (also `rahul@`, `ananya@`, `arjun@`, `meera@`, `vikram@example.com`) |

Coupons: `WELCOME50`, `BITE20` (`WEEKEND15` is seeded as expired, to demonstrate validation).

---

## Run everything with Docker

```bash
cp .env.example .env      # set the passwords
docker compose up --build
# open http://localhost:8080   (Swagger at http://localhost:8080/swagger)
```

Compose starts MySQL 8.4 (on host port 3307), the API, and nginx serving the Angular build. nginx reverse-proxies `/api` on the same origin, so cookies and CSRF tokens work without CORS.

> ⚠️ The Docker setup was written but **not run** on the development machine: Docker Desktop was stopped and images could not be pulled.
> Verify it once with `docker compose up --build` before relying on it in a demo.

---

## Tests

```bash
cd backend && dotnet test                                     # unit + Moq + integration
cd frontend/bitebox-web && npx ng test --watch=false          # Vitest
```

The integration tests boot the real API against a real MySQL database called `bitebox_test`, which is recreated on every run. They cover:
- security boundaries: 401, 403 and CSRF
- checkout pricing and idempotency
- a **concurrent overselling race**
- payment failure releasing stock
- BiteBot grounding

They read the connection from the API user-secret, swapping in the `bitebox_test` database, or from `BITEBOX_TEST_CONNECTION`. If MySQL is unreachable they **skip** instead of failing. See [docs/TESTING.md](docs/TESTING.md) for the latest recorded results.

---

## Enabling Claude for BiteBot (optional, paid API)

A Claude Pro or Max subscription does **not** include API credits. BiteBot works fully offline with its rule-based parser.

To try Claude for request understanding:

```bash
dotnet user-secrets set "ANTHROPIC_API_KEY" "<key>" --project src/BiteBox.API
dotnet user-secrets set "BiteBox:AI:Provider" "Claude" --project src/BiteBox.API
```

Claude only turns text into filters. Every dish, price and stock flag still comes from MySQL. See [docs/AI-BITEBOT.md](docs/AI-BITEBOT.md).

---

## Repository layout

```text
BiteBox/
├── backend/
│   ├── BiteBox.sln, global.json, nuget.config, Directory.Build.props, Dockerfile
│   ├── src/
│   │   ├── BiteBox.Domain/          entities, Order state machine, pricing & discount strategies, domain events
│   │   ├── BiteBox.Application/     use-case services, DTOs, FluentValidation, ports (interfaces), BiteBot engine
│   │   ├── BiteBox.Infrastructure/  EF Core + MySQL, Identity adapter, migrations, seeder, payment simulator, Claude adapter
│   │   └── BiteBox.API/             controllers, auth/CSRF/rate-limit config, exception → ProblemDetails, Swagger
│   └── tests/BiteBox.Tests/         Domain, Application (Moq), Integration (WebApplicationFactory + MySQL)
├── frontend/bitebox-web/            Angular 22 app (core/ shared/ layout/ features/), Dockerfile, nginx.conf
├── docs/                            architecture, API, patterns, AI, testing, presentation, viva prep
├── docker-compose.yml, .env.example, .github/workflows/ci.yml
```

## Documentation

- **[Software Requirements Specification & full system design](docs/BiteBox-SRS.md)**: start here; one document that explains the whole project
- [Architecture and diagrams](docs/ARCHITECTURE.md): system, components, request flow, ER diagram, order state machine, deployment
- [API reference](docs/API.md)
- [Design patterns and SOLID](docs/DESIGN-PATTERNS.md)
- [BiteBot AI architecture](docs/AI-BITEBOT.md)
- [Design system](docs/DESIGN-SYSTEM.md)
- [Testing and results](docs/TESTING.md)
- [College presentation material](docs/PRESENTATION.md)
- [Viva and interview questions](docs/VIVA-QUESTIONS.md)

Food photography is from [Unsplash](https://unsplash.com) (Unsplash License). All people and orders in the seed data are fictional.
