# Design patterns and SOLID in BiteBox

Each pattern is here because it solves a concrete problem. Each entry covers what it is, why it is used, where it lives, what the alternative was, and how to explain it in a viva.

## 1. Clean Architecture / Ports and Adapters
- **Problem:** business rules tangled with the database and web framework are hard to test and change.
- **Where:** four projects. `BiteBox.Application/Abstractions/Abstractions.cs` defines the *ports* (`IApplicationDbContext`, `IIdentityService`, `IPaymentGateway`, `IFileStorage`). `BiteBox.Infrastructure` provides the *adapters*.
- **Alternative rejected:** a single project. It is quicker to start, but you can't show that MySQL or Identity could be swapped, and unit tests would need a database.
- **Viva line:** "Dependencies point inwards. The Order state machine has no idea MySQL or ASP.NET exist, so I can test it with plain xUnit in milliseconds."

## 2. Repository + Unit of Work, via EF Core (deliberately *no* generic repository)
- **Problem:** isolate persistence and commit related changes atomically.
- **Where:** `IApplicationDbContext` exposes `DbSet<T>` (each one already behaves as a repository) plus `SaveChangesAsync` (the unit of work) and `InTransactionAsync` (an explicit transaction with the retry strategy).
- **Alternative rejected:** `IRepository<T>` with `GetAll/Add/Update` for every entity. It duplicates EF, hides useful features (projection, `ExecuteUpdate`) and adds no value. *Specific* abstractions exist where they mean something, e.g. `StockLedger`.
- **Viva line:** "DbContext *is* a unit of work and DbSet *is* a repository. Wrapping them again would be ceremony, not architecture."

## 3. Strategy
- **Discounts:** `IDiscountStrategy` → `PercentageDiscountStrategy`, `FlatDiscountStrategy` (`Domain/Pricing/DiscountStrategies.cs`).
- **Fulfilment fees:** `IFulfillmentFeeStrategy` → `HomeDeliveryFeeStrategy`, `PickupFeeStrategy` (`Application/Features/Orders/Pricing.cs`). They are injected as a collection and selected by `FulfillmentFeeCalculator`.
- **Problem it solves:** pricing rules change often. A new rule is a new class, not an edit to a `switch` spread across services (the Open/Closed Principle).
- **Viva line:** "Adding 'express delivery' means adding one class and one DI registration. The checkout code doesn't change."

## 4. Factory
- **Where:** `DiscountStrategyFactory.Create(DiscountType)` and the static factory `Order.Create(...)`.
- **Why:** `Order.Create` guarantees that an order can only come into existence in a valid state: it has items, has an address if it is a delivery, gets the correct initial status for its payment method, records the first history row, and raises its event.
- **Alternative rejected:** a public constructor plus setters, which allows half-built, invalid orders.

## 5. Adapter
- **Payment:** `IPaymentGateway` → `SimulatedPaymentGateway`. A `RazorpayGateway` would implement the same interface.
- **AI:** `IFoodIntentExtractor` → `ClaudeFoodIntentExtractor` (official Anthropic SDK). The Gemini/OpenAI/local Ollama options would be further adapters.
- **Identity:** `IIdentityService` → `IdentityService` wraps ASP.NET Core Identity's `UserManager`/`SignInManager`.
- **Viva line:** "The application speaks its own language (`ChargeAsync`, `ExtractAsync`). Adapters translate that into each vendor's API."

## 6. Observer / Domain events
- **Where:** `Order` raises `OrderPlacedEvent` and `OrderStatusChangedEvent`. `BiteBoxDbContext.SaveChangesAsync` dispatches them to `IDomainEventHandler<T>` implementations after the save. `OrderNotificationHandler` creates customer notifications.
- **Why:** the Order aggregate doesn't know notifications exist. An email or SMS handler can be added later without touching order code.
- **Alternative rejected:** MediatR. It is unnecessary for two events, and a 40-line dispatcher is easier to explain.

## 7. State machine (State pattern, table-driven)
- **Where:** `Domain/Orders/OrderStateMachine.cs`. The legal transitions, who may perform each one, and fulfilment restrictions are all held as data.
- **Why:** this rules out arbitrary status changes. The same table powers server enforcement, the admin UI buttons (`allowedNextStatuses`) and exhaustive unit tests.

## 8. CQRS-lite
- **Reads** project straight into DTOs with `AsNoTracking` (`CatalogService`, `ReportService`, `OrderQueries.ToSummariesAsync`).
- **Writes** load the aggregate and go through domain methods.
- **Not used:** separate read databases or a CQRS framework. The read and write loads don't justify them.

## 9. Value object and snapshot
- **Where:** `DeliveryAddress` (an immutable record stored as an EF owned type), and order-item name and price snapshots.
- **Why:** history must not change when the menu or an address changes.

## 10. Specification?
- **Considered and not used:** the menu filter is one composable `IQueryable` pipeline in a single place (`CatalogService.SearchProductsAsync`). Specification objects would only add indirection.

## Cross-cutting patterns in the API
- **Chain of responsibility** (the middleware pipeline): exception handler → security headers → static files → CORS → authentication → authorisation → rate limiter → controllers.
- **Filters:** `ValidationFilter` and the global `AntiforgeryValidationFilter`.
- **Options pattern:** `PricingOptions`, `RestaurantOptions`, `AiOptions` bound from configuration, so rates and fees are not hard-coded.

## SOLID, with concrete examples

| Principle | Example |
|---|---|
| **S**ingle responsibility | `StockLedger` only changes stock and records movements. `PriceCalculator` only computes totals. Controllers only translate HTTP. |
| **O**pen/closed | Discount and fee strategies; new BiteBot providers via `IFoodIntentExtractor` |
| **L**iskov substitution | Any `IPaymentGateway` (simulated or real) can be swapped in without `OrderService`/`PaymentService` noticing |
| **I**nterface segregation | Small ports (`IFileStorage`, `IPaymentGateway`, `ICurrentUser`) instead of one "IServices" interface |
| **D**ependency inversion | Application defines the interfaces, Infrastructure implements them, and DI wires them in `Program.cs` |

## Angular-side patterns
- **Facade / store services** (`CartStore`, `AuthStore`) hide HTTP and localStorage from components.
- **Interceptor** (chain of responsibility) for CSRF retry and session expiry.
- **Guards** for navigation policy.
- **Presentational components** (`bb-product-card`, `bb-stepper`, `bb-status`) with signal inputs and outputs.
