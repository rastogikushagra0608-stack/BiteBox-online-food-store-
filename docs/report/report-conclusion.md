# Conclusion and future scope

## Conclusion
BiteBox meets its objectives. It is a working, end-to-end food ordering and restaurant management system:

| Objective | How it is met |
|---|---|
| Customer ordering | Customers can find food, get grounded AI suggestions, order, pay and track their orders. |
| Back office | Staff run the kitchen and deliveries from role-specific screens. |
| Owner analytics | The owner sees accurate, live business numbers. |
| Correctness | It is enforced on the server and proved by tests: <ul><li>prices are calculated by the API;</li><li>orders move only along legal state-machine transitions;</li><li>a concurrent test with four buyers and one portion produced exactly one sale and three clean "sold out" responses;</li><li>repeated checkouts with the same idempotency key create a single order.</li></ul> |
| Security | HttpOnly cookie sessions, CSRF tokens, role- and policy-based authorisation, lockout, rate limits, upload signature checks and a uniform error contract that never leaks internals. |
| Maintainability | Clean Architecture keeps business rules independent of frameworks. New payment providers, AI providers, discount types or notification channels are new adapter or strategy classes, not rewrites. |

**Key learning outcomes:**
- designing a domain model with invariants;
- handling concurrency at the database level;
- building secure cookie-based authentication for a SPA;
- writing integration tests against a real database;
- using modern Angular (signals, zoneless);
- integrating an LLM safely, by grounding its output in trusted data.

## Limitations
- **Payment is simulated.** No real money is collected.
- **Single restaurant only.** There is no support for branches or a marketplace.
- **Order status updates use polling** (auto-refresh) rather than push (WebSockets/SignalR).
- **Notifications are in-app only.** There are no email, SMS or push notifications.
- **BiteBot's default engine is rule-based English parsing.** Hinglish or very free-form requests work better with the optional Claude adapter, which needs a paid API key.
- **Some parts were not verified on the development machine:** the Docker Compose setup and the live Claude adapter, because Docker Desktop was stopped and no API key was configured.

## Future scope
1. Integrate a real payment gateway (a Razorpay or Stripe adapter in test mode first) with webhook confirmation.
2. Real-time order tracking with SignalR, and rider GPS tracking with delivery assignment.
3. Email, SMS and WhatsApp notifications as extra domain-event handlers.
4. Multi-branch support and a marketplace model.
5. Product variants (half/full), add-ons and allergen information.
6. A loyalty programme and personalised recommendations based on order history.
7. Conversation memory and Hinglish support for BiteBot.
8. A native or PWA mobile app for customers and riders.
9. Observability (OpenTelemetry), a Redis menu cache, and a CDN for images.
10. An upgrade to .NET 10 / EF Core 10 (only framework and package versions change).

## References
1. IEEE Std 830-1998, *IEEE Recommended Practice for Software Requirements Specifications*; ISO/IEC/IEEE 29148:2018, *Requirements engineering*.
2. Angular documentation: https://angular.dev
3. ASP.NET Core documentation: https://learn.microsoft.com/aspnet/core
4. Entity Framework Core documentation: https://learn.microsoft.com/ef/core
5. MySQL 8.0 Reference Manual: https://dev.mysql.com/doc/refman/8.0/en/
6. MySQL Connector/NET and `MySql.EntityFrameworkCore`: https://dev.mysql.com/doc/connector-net/en/
7. FluentValidation documentation: https://docs.fluentvalidation.net
8. OWASP Application Security Verification Standard (ASVS) and the OWASP Cheat Sheet Series (CSRF, authentication, file upload): https://owasp.org
9. Anthropic Claude API documentation: https://docs.claude.com
10. R. C. Martin, *Clean Architecture: A Craftsman's Guide to Software Structure and Design*, Prentice Hall, 2017.
11. E. Gamma, R. Helm, R. Johnson, J. Vlissides, *Design Patterns: Elements of Reusable Object-Oriented Software*, Addison-Wesley, 1994.
12. E. Evans, *Domain-Driven Design: Tackling Complexity in the Heart of Software*, Addison-Wesley, 2003.
13. RFC 7807 / RFC 9457, *Problem Details for HTTP APIs*.
14. Food photography: Unsplash (Unsplash License), https://unsplash.com
