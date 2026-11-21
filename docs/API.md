# BiteBox REST API (v1)

Interactive docs: **`/swagger`**. Swagger UI copies the `XSRF-TOKEN` cookie into the header for you. Call `GET /api/v1/auth/csrf` first.

## Conventions

- Base path `/api/v1`. JSON throughout. Enums are strings (`"Delivery"`, `"OutForDelivery"`).
- **Authentication:** the `bitebox.auth` HttpOnly cookie, set by `/auth/login` or `/auth/register`.
- **CSRF:** every `POST`/`PUT`/`DELETE` must send `X-XSRF-TOKEN` matching the `XSRF-TOKEN` cookie. Refresh it after sign-in and sign-out.
- **Pagination:** `?page=1&pageSize=20` → `{ items, page, pageSize, totalCount, totalPages }`. `pageSize` is capped at 100.
- **Errors:** RFC 7807 ProblemDetails with a stable `code`:

```json
{ "status": 409, "title": "Conflict", "detail": "Only 2 portions of Masala Dosa are left.", "code": "cart.insufficient_stock", "traceId": "…" }
```

| Status | When |
|---|---|
| 400 | Validation failed (`errors` has per-field messages), bad request, missing or invalid CSRF token |
| 401 | Not signed in, or wrong credentials |
| 403 | Signed in but the role or transition is not allowed, or the account is blocked or locked |
| 404 | Not found. Also used for other customers' orders, so order numbers can't be probed. |
| 409 | Stock conflict, duplicate, coupon exhausted, order can no longer be cancelled |
| 422 | Domain rule violated (e.g. coupon minimum order, invalid state transition) |
| 429 | Rate limit exceeded |

## Auth `/auth`

| Method & path | Auth | Body → Response |
|---|---|---|
| `GET /auth/csrf` | – | → 204 + `XSRF-TOKEN` cookie |
| `POST /auth/register` | – (10/min) | `{fullName, email, phone, password}` → 201 `MeDto` (signed in) |
| `POST /auth/login` | – (10/min) | `{email, password, rememberMe}` → `MeDto` |
| `POST /auth/logout` | – | → 204 |
| `GET /auth/me` | user | → `{id, fullName, email, phone, roles[]}` |
| `PUT /auth/me` | user | `{fullName, phone}` → `MeDto` |
| `POST /auth/me/password` | user | `{currentPassword, newPassword}` → 204 |

Validation: phone must match `^[6-9]\d{9}$`. Password needs 8+ characters with an uppercase letter, a lowercase letter and a digit. 5 failed logins lock the account for 5 minutes.

## Catalogue (public)

| Method & path | Notes |
|---|---|
| `GET /categories` | Active categories with dish counts |
| `GET /products` | `search, category (slug), vegetarian, minPrice, maxPrice, maxSpice, featuredOnly, inStockOnly, sort (Popular\|PriceLowToHigh\|PriceHighToLow\|Rating\|Newest), page, pageSize` |
| `GET /products/{slug}` | Detail. Includes `isFavorite` when signed in. |
| `GET /products/{slug}/related` | Up to 4 from the same category |
| `GET /products/{id}/reviews` | `{items, averageRating, reviewCount, canReview, hasReviewed}` |
| `POST /products/{id}/reviews` | user. `{rating 1-5, comment?}`. Only after a delivered or picked-up order containing the dish; one review per dish. |
| `GET /promotions` | Currently valid coupons for banners |

## Cart `/cart` (user)

| Method & path | Body |
|---|---|
| `GET /cart` | → `{items[], itemCount, subtotal, hasUnavailableItems}` (current prices) |
| `POST /cart/items` | `{productId, quantity 1-20}` |
| `PUT /cart/items/{productId}` | `{quantity}` (0 removes) |
| `DELETE /cart/items/{productId}` · `DELETE /cart` | |
| `POST /cart/merge` | `[{productId, quantity}]` from the guest cart |

## Orders `/orders` (user)

| Method & path | Notes |
|---|---|
| `POST /orders/quote` | `{fulfillmentType, couponCode?}` → server price breakdown, coupon message, `problems[]` |
| `POST /orders` | `{fulfillmentType, addressId?, paymentMethod (CashOnDelivery\|OnlineSimulated), couponCode?, notes?, idempotencyKey}` → 201 `OrderDetailDto`. The same key returns the same order. |
| `GET /orders` | My orders (paged) |
| `GET /orders/{orderNumber}` | Detail with timeline, `allowedNextStatuses`, `canCancel`, `canReview` |
| `POST /orders/{orderNumber}/cancel` | `{reason?}`. Allowed only while `AwaitingPayment` or `Placed`. |
| `POST /orders/{orderNumber}/reorder` | Adds available items back to the cart → `{added}` |
| `POST /orders/{orderNumber}/payment` | `{success: bool}`. Simulated gateway. Failure → `PaymentFailed` and stock released. |

## Account `/account` (user)

`GET|POST /addresses`, `PUT|DELETE /addresses/{id}` · `GET /favorites`, `GET /favorites/ids`, `PUT|DELETE /favorites/{productId}` · `GET /notifications`, `POST /notifications/read-all` · `GET /reviews`

## BiteBot `/ai`

| `POST /ai/bitebot` | public, 15/min. `{message ≤ 300 chars}` → `{reply, suggestions[{product, reason}], combo?{items, foodTotal, summary}, understoodAs[], followUps[], engine ("rules"\|"claude")}` |
|---|---|

## Admin `/admin`

| Area | Endpoints | Role |
|---|---|---|
| Dashboard | `GET /admin/dashboard?from&to` (local dates, max 366 days) | Admin |
| Reports | `GET /admin/reports/sales`, `/reports/products`, `/reports/sales.csv`, `/reports/products.csv` | Admin |
| Products | `GET/POST /admin/products`, `GET/PUT/DELETE /admin/products/{id}`, `POST /admin/products/{id}/image` (multipart, ≤ 2 MB, JPG/PNG/WEBP verified by signature) | Admin |
| Categories | `GET/POST /admin/categories`, `PUT/DELETE /admin/categories/{id}` (delete only when empty) | Admin |
| Inventory | `GET /admin/inventory?lowStockOnly`, `POST /admin/inventory/{productId}/adjust {quantityChange, reason (Restock\|Wastage\|ManualAdjustment), note}`, `GET /admin/inventory/transactions` | Admin |
| Coupons | `GET/POST /admin/coupons`, `PUT/DELETE /admin/coupons/{id}` (used coupons are deactivated, not deleted) | Admin |
| Orders | `GET /admin/orders?status&search&from&to&fulfillment&activeOnly`, `GET /admin/orders/{no}`, `POST /admin/orders/{no}/status {status, note}` | Admin, Kitchen, Delivery (transition rules apply) |
| Refund | `POST /admin/orders/{no}/refund` | Admin |
| Users | `GET /admin/users?search&role`, `GET /admin/users/{id}/orders`, `POST /admin/users/staff`, `PUT /admin/users/{id}/active`, `PUT /admin/users/{id}/role` | Admin (cannot block or demote themselves) |

## Health

`GET /health/live` (process up), `GET /health/ready` (MySQL reachable).
