# Testing

## How to run
```bash
cd backend && dotnet test                                 # unit + Moq + integration (real MySQL)
cd frontend/bitebox-web && npx ng test --watch=false      # Vitest
```

The integration tests recreate the `bitebox_test` database on every run. They take the connection from `BITEBOX_TEST_CONNECTION`, or from the API user-secret with the database name swapped. If MySQL is unreachable they skip instead of failing.

## Recorded results (2026-11-20, development machine: Windows 11, MySQL 8.0.43, .NET SDK 9.0.321, Node 24.19)

| Suite | Result |
|---|---|
| Backend: Domain unit tests (order state machine, lifecycle, refunds, pricing, coupons, strategies) | ✅ passed |
| Backend: Application unit tests (BiteBot parser and sanitiser; `AdminUserService` with **Moq**) | ✅ passed |
| Backend: Integration tests (`WebApplicationFactory` + real MySQL) | ✅ **14/14 passed** |
| **Backend total** | ✅ **61 passed, 0 failed, 0 skipped** (≈ 8 s) |
| Frontend (Vitest): app shell, 4 guard scenarios, guest cart, cart merge after sign-in, password rule, open-redirect check, INR formatting, error mapping | ✅ **11 passed, 0 failed** |
| Builds | `dotnet build`: 0 warnings, 0 errors · `ng build` (production): clean, 108.9 kB initial transfer |

### Integration scenarios covered
1. The menu is public and seeded (≥ 40 dishes)
2. The cart needs authentication → **401**
3. POST without an `X-XSRF-TOKEN` → **400** (`csrf_invalid`)
4. Customer → admin endpoints → **403**; admin → dashboard → 200 with real revenue
5. Kitchen can see orders but not product management (**403**)
6. A wrong password gives a generic 401 (no account enumeration)
7. Registration validation (400) and successful registration as `Customer`
8. Checkout prices come from the server (subtotal, 5% GST), the order is **idempotent** (same key → same order), stock is reserved and then returned on cancel
9. **4 concurrent buyers for 1 portion → exactly 1 `201`, 3 `409`, final stock 0** (no overselling)
10. A failed online payment → `PaymentFailed` and stock released
11. A customer can't open another customer's order (**404**)
12. Kitchen can't mark an order Delivered (**403**)
13. BiteBot only returns real, in-stock, vegetarian dishes under the budget, with database prices
14. BiteBot's combo fits the budget and its total equals the sum of its items

## Manual end-to-end run (API + Angular dev server, through the `/api` proxy)

| Step | Observed |
|---|---|
| Priya signs in, adds 2 × Paneer Butter Masala | cart 2 items, ₹538 |
| Quote with `BITE20`, delivery | subtotal 538, discount 107.60, delivery 40, GST 21.52, **total 491.92** |
| Place order (online) → simulated payment succeeds | `AwaitingPayment` → `Placed`, payment `Paid` |
| Kitchen: Confirm → Preparing → Ready | ✅ |
| Kitchen tries "Delivered" | **403**: "As Kitchen you cannot move this order from Ready to Delivered." |
| Delivery: Out for delivery → Delivered | ✅ |
| Customer view | `Delivered`, full timeline, `canReview = true`, 6 unread notifications |
| BiteBot: "spicy veg dinner for two under 700" | 4 real veg dishes; combo of Veg Thali + Penne Arrabbiata + Pav Bhaji = **₹697** |
| Customer → `/admin/dashboard` | **403** · anonymous → `/cart` **401** |
| Admin dashboard (30 days) | revenue ₹56,409.62 over 110 orders, 30 daily points, top dish, low-stock list |
| Sales CSV export | downloads with per-day rows |
| SPA routes `/`, `/menu`, `/menu/masala-dosa`, `/admin`, `/account/orders` | all served (200) |

## Defects found by these runs and fixed
| Defect | Fix |
|---|---|
| Every request returned 500: `AutoValidateAntiforgeryTokenAttribute` needs Razor view services that `AddControllers()` doesn't register | Added an explicit `AntiforgeryValidationFilter` (API project) that returns a ProblemDetails `csrf_invalid` response. The Angular interceptor retries once on that code. |
| Integration tests silently used the main database: settings read in `Program.cs` before `Build()` ignore `WebApplicationFactory.ConfigureAppConfiguration` | The test factory sets environment variables, which `CreateBuilder` reads |
| The test suite tripped the auth rate limit (10 sign-ins/min/IP) | Limits are now configurable (`BiteBox:RateLimits:*`). Production defaults are unchanged; tests raise them. |
| Expected 4xx business errors were logged as `fail` with stack traces | Silenced the duplicate middleware log. `GlobalExceptionHandler` still logs genuine 500s. |

## Not yet verified
- **Visual browser walkthrough:** the Claude-in-Chrome extension wasn't connected, so pages were checked by HTTP status and API behaviour, not by screenshots. Open http://localhost:4200 and look through the pages yourself, and take screenshots for the report.
- **Docker Compose:** Docker Desktop was stopped, so no images were pulled.
- **Claude adapter** against the live API (no API key; the rule-based engine is the tested default).
