# Viva and interview preparation

Each answer starts simple (🟢), then goes deeper (🔵 for follow-up questions, 🟣 for senior-level depth). Answers point to BiteBox code wherever possible.

---

## Architecture

**Q1. What is Clean Architecture and why did you use it?**
🟢 The code is split into layers, and the business rules don't depend on the database or the web framework.
🔵 BiteBox has four projects: Domain (entities, state machine, pricing), Application (use cases and interfaces), Infrastructure (EF Core, MySQL, Identity, external services) and API (HTTP). Dependencies point inwards only.
🟣 The benefit is testability and replaceability. `OrderTests` run without a database, and swapping MySQL or the payment provider touches only Infrastructure. The trade-off is more projects and some mapping code, which is why it is a *modular monolith* and not microservices: one deployable, with no network hops or distributed transactions.

**Q2. Why not microservices?**
🟢 It's one restaurant and one team; microservices would add complexity without benefit.
🟣 Order placement needs an atomic update across stock, coupons and orders. In one database that's a single transaction. Across services it would need sagas and outbox patterns. The module boundaries (Catalog, Orders, Admin, AI) mean the system could be split later if scale demanded it.

**Q3. Trace a request from Angular to MySQL.**
`CheckoutPage.place()` → `OrdersApi.place()` → HttpClient (adds `X-XSRF-TOKEN`) → interceptor → Kestrel → middleware (exception handler, auth cookie → `ClaimsPrincipal`, authorisation, rate limiter) → `OrdersController.Place` → `ValidationFilter` (FluentValidation) and the antiforgery filter → `OrderService.PlaceOrderAsync` → domain (`PriceCalculator`, `Coupon`, `Order.Create`) → `BiteBoxDbContext` (transaction, conditional `UPDATE`s, `INSERT`s) → domain events → notifications → `201 Created` → the component navigates to the tracker. The sequence diagram is in ARCHITECTURE.md §4.

## SOLID and design patterns

**Q4. Give one example of each SOLID principle in your code.** See the table in DESIGN-PATTERNS.md (e.g. Open/Closed: add a new `IFulfillmentFeeStrategy` without editing the checkout).

**Q5. Why didn't you write a generic repository?**
🟢 EF Core's DbContext already is a repository and a unit of work.
🟣 A generic `IRepository<T>` hides `IQueryable` projection and `ExecuteUpdate`, which BiteBox needs for atomic stock updates and efficient reads. It adds a layer with no behaviour. Where a meaningful abstraction exists, it gets its own name (`StockLedger`).

**Q6. Explain the Strategy and Factory patterns in BiteBox.** Discounts (`PercentageDiscountStrategy`, `FlatDiscountStrategy`) are chosen by `DiscountStrategyFactory`. Delivery fees are injected strategies. `Order.Create` is a factory that guarantees valid orders.

**Q7. What are domain events, and why use them?** `Order` raises `OrderStatusChangedEvent`. The DbContext dispatches it after saving, and `OrderNotificationHandler` creates a notification. Orders stay decoupled from notifications (the Observer pattern), and adding email means adding a handler.

## ASP.NET Core

**Q8. What is middleware? How does it compare to Spring?**
🟢 Components that every request passes through, in order, like servlet filters.
🔵 BiteBox's order is: `UseExceptionHandler` → security headers → static files → CORS → authentication → authorisation → rate limiter → endpoints. Order matters: authentication must run before authorisation.

**Q9. Dependency injection lifetimes?** Singleton (one per app: `TimeProvider`, fee strategies, the payment simulator), scoped (one per request: `DbContext` and services that use it), transient (new each time). A singleton must never capture a scoped DbContext. That's why `ClaudeFoodIntentExtractor` (a singleton) has no DbContext.

**Q10. How do you handle errors?** Exceptions such as `NotFoundException`, `ConflictException` and `DomainException` are mapped centrally by `GlobalExceptionHandler` to RFC 7807 ProblemDetails with a `code`. Unexpected errors are logged and return a generic 500. Stack traces never reach clients.

**Q11. Where is validation done?** In two places. The Angular Reactive Forms validators are for UX. FluentValidation on the server is the authority, run by `ValidationFilter` before every action. Domain invariants (price > 0, rating 1–5, legal transitions) are a third line of defence. MySQL CHECK constraints are the last.

## Security

**Q12. How does authentication work? Why cookies and not JWT?**
🟢 ASP.NET Core Identity checks the PBKDF2 password hash and issues an encrypted HttpOnly cookie.
🟣 JavaScript can't read an HttpOnly cookie, so an XSS bug can't steal the session. A JWT kept in localStorage can be stolen. Cookies are sent automatically, which creates CSRF risk; BiteBox counters it with an anti-forgery token: the server issues `XSRF-TOKEN`, Angular echoes it in a header, and a global filter validates every unsafe request. `SameSite` adds defence in depth.

**Q13. Authentication vs authorisation?** Authentication proves *who you are* (cookie, Identity). Authorisation decides *what you may do* (`[Authorize(Policy="AdminOnly")]`, role checks, and per-transition rules in `OrderStateMachine`).

**Q14. Is hiding admin buttons in Angular enough?** No. Anyone can call the API directly. Every rule is enforced on the server, and integration tests assert 401 and 403 responses. Angular guards only improve UX.

**Q15. Why never trust prices from the client?** A user can edit the request in DevTools. BiteBox accepts only product IDs, quantities, a coupon code and choices. It reloads prices from MySQL, recalculates through `PriceCalculator`, and stores snapshots.

**Q16. List other security controls.** Account lockout after 5 failures, generic login errors (no account enumeration), rate limiting, upload checks by file signature with random names, a size cap, security headers, an open-redirect check on `returnUrl`, 404 instead of 403 for other customers' orders, secrets in user-secrets or environment variables, and parameterised queries throughout (EF Core) with escaped LIKE patterns.

## Database and EF Core

**Q17. Why DECIMAL for money?** Floating point can't represent 0.1 exactly, so rounding errors accumulate. `DECIMAL(10,2)` is exact. Rounding is centralised in `Money.Round` (MidpointRounding.AwayFromZero).

**Q18. What indexes did you add, and why?** Each matches a hot query: menu filters `(CategoryId, IsDeleted, IsAvailable)`, the kitchen queue `(Status, PlacedAt)`, order history `(CustomerId, PlacedAt)`, unique slugs and codes, and `(CustomerId, IdempotencyKey)` for duplicate protection. Indexes speed up reads but slow writes, so only queried columns are indexed.

**Q19. Explain normalisation and your deliberate denormalisations.** The schema is in 3NF. The exceptions are `order_items` price and name snapshots (history must not change) and `products.AverageRating` (to avoid aggregating reviews on every menu view, recalculated on each new review).

**Q20. What is a transaction? What isolation level?** All-or-nothing execution of several statements (ACID). MySQL InnoDB defaults to REPEATABLE READ. BiteBox doesn't rely on the isolation level for stock. The conditional `UPDATE` takes a row lock and re-checks `stock >= qty` atomically.

**Q21. How do you prevent overselling under concurrency?** See ARCHITECTURE.md §6. One `UPDATE … WHERE stock >= qty` per line; 0 rows affected means sold out, so the transaction rolls back. A read-then-write approach would race. Proven by an integration test with 4 parallel buyers and 1 portion.

**Q22. Optimistic vs pessimistic concurrency?** Optimistic: a version column, then retry on conflict. Pessimistic: lock rows (`SELECT … FOR UPDATE`). BiteBox's conditional update is a lock-free "compare-and-decrement" that behaves like a short pessimistic lock inside InnoDB, which is ideal for counters.

**Q23. Code-first migrations?** C# entities plus fluent configuration → `dotnet ef migrations add` generates a migration → it is applied with `Database.Migrate()` (dev and Docker) or a reviewed SQL script (production).

**Q24. Why the Oracle MySQL provider and not Pomelo?** It's official and free, and it has EF Core 8, 9 and 10 releases. Pomelo had no EF Core 10 release at the time (its 9.0 pins EF 9). BiteBox avoids provider-specific translation risk by keeping queries simple, and verifies them with real-MySQL integration tests.

## Angular

**Q25. What are signals and why use them?** Reactive values (`signal`, `computed`, `effect`) that tell Angular exactly what changed. With zoneless change detection, only components that read a changed signal re-render. `CartStore.count` is a `computed` used by the header badge.

**Q26. Signals vs RxJS?** Signals hold *state*; RxJS models *events and streams over time*. BiteBox uses signals for the cart, auth and page state, and RxJS for debounced search, polling the order tracker, router events and HTTP.

**Q27. What do the interceptor and guards do?** The interceptor retries a request once after refreshing a stale CSRF token, and resets the session on 401. Guards (`authGuard`, `roleGuard`) redirect users who shouldn't see a page. They wait until the session check finishes.

**Q28. Why lazy loading?** Each feature is a separate JavaScript chunk, so the first visit downloads about 109 kB, and customers never download the admin code.

**Q29. Reactive vs template-driven forms?** Reactive forms are defined in TypeScript, so they are typed, testable and easy to validate (`strongPassword`, the phone regex). Template-driven `ngModel` is used only for simple filters.

## AI

**Q30. How does BiteBot avoid hallucinations?** The LLM only fills a strict JSON schema of *filters*. Dishes, prices and stock always come from MySQL, and the reply is built from templates using database facts. The output is sanitised, and unknown categories are dropped.

**Q31. What happens if the AI provider is down?** A timeout or exception falls back to the rule-based parser. The response field `engine` shows which one answered. The feature never breaks the page.

**Q32. Does your Claude subscription pay for this?** No. Consumer subscriptions don't include API credits. The default engine is free and offline; Claude is opt-in with an API key.

## Testing

**Q33. Unit vs integration tests in your project?** Unit tests cover the state machine, pricing, coupons, the BiteBot parser, and Moq-based service rules, with no I/O. Integration tests use `WebApplicationFactory` against a real MySQL: auth boundaries, CSRF, checkout, idempotency, the concurrency race, and payment failure. Frontend tests (Vitest) cover guards, the guest cart and merge, validators and the error mapper.

**Q34. What is Moq for?** It creates fake implementations of interfaces, so a service is tested in isolation. `AdminUserServiceTests` verifies that `IIdentityService.SetActiveAsync` is *never* called when an admin tries to block themselves.

**Q35. Why test against real MySQL instead of InMemory?** The EF InMemory provider doesn't support transactions, `ExecuteUpdate` or SQL semantics, so the overselling protection could pass in InMemory and fail in production.

## DevOps and scalability

**Q36. What does Docker Compose start?** `mysql:8.4` (volume, healthcheck), `api` (migrates and seeds on start) and `web` (nginx serving Angular and proxying `/api`), all with one command.

**Q37. How would you scale BiteBox?** Run several API instances behind a load balancer. The app is stateless apart from cookies; share Data Protection keys between instances so cookies stay valid. Cache the menu in Redis, serve images from object storage and a CDN, add MySQL read replicas for reports, and use SignalR with a backplane for live updates. Split modules into services only when one has very different scaling needs.

**Q38. What would you improve next?** A real payment gateway adapter, SignalR push instead of polling, an outbox for reliable notifications, product variants, and observability.
