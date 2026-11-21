# Abstract

**BiteBox** is a full-stack web application for online food ordering and restaurant management, built for a single restaurant. Customers browse a live menu, ask an AI assistant (**BiteBot**) for suggestions in plain English, build a cart, check out with server-calculated prices and coupons, pay by cash on delivery or a simulated online payment, and track each order through a live status timeline. Restaurant staff use a role-based back office (**BiteBox Admin**):
- the kitchen runs the order queue;
- delivery partners move orders out for delivery;
- the administrator manages the menu, stock, promotions and people, and reads an analytics dashboard with revenue, top dishes and low-stock alerts.

The system uses **Angular 22** (standalone components, signals, zoneless change detection), an **ASP.NET Core 9 Web API** in **Clean Architecture** (Domain, Application, Infrastructure, API), and **MySQL 8** through **Entity Framework Core 9** code-first migrations.

Correctness and security get particular attention:
- Prices are always computed on the server.
- An explicit state machine allows only legal order transitions, and each role may make only some of them.
- Stock is reserved with an atomic conditional SQL update, so two customers can never buy the last portion.
- Checkout is idempotent.
- Sessions use HttpOnly cookies with anti-forgery tokens, role and policy authorisation, account lockout and rate limiting.

BiteBot is *grounded*: it may only recommend dishes that really exist and are in stock, with prices taken from the database. It runs on a free rule-based engine by default, and can optionally use Claude for request understanding.

The project has **61 automated backend tests** (unit, mock-based and integration tests against a real MySQL database, including a concurrent overselling race) and **11 frontend tests**. All of them pass.

**Keywords:** online food ordering, restaurant management system, Angular, ASP.NET Core, Clean Architecture, MySQL, Entity Framework Core, AI recommendation, state machine, concurrency control, web security.

<div class="page-break"></div>

# Chapter 1: Project overview

## 1.1 Problem statement
Small and mid-sized restaurants usually take orders by phone, through third-party aggregators that charge high commission, or with spreadsheets and paper tickets. This causes:
- **Errors and disputes:** wrong prices, forgotten items, coupons applied by hand.
- **Overselling:** dishes that have sold out are still accepted, and customers are disappointed.
- **No visibility:** customers phone the restaurant to ask "where is my order?"; the owner has no reliable sales figures.
- **No single source of truth:** the menu, stock, orders and customers live in different places.
- **Choice overload:** a customer facing a long menu, especially a group with a budget, struggles to decide what to order.

## 1.2 Objectives
1. Build a **direct ordering channel** that the restaurant owns: menu, cart, checkout, payment and tracking.
2. Make the server the **only authority on prices, stock and order status**, so the browser cannot be trusted with money.
3. **Prevent overselling** under concurrent load, and make checkout **idempotent**, so double clicks never create duplicate orders.
4. Give staff a **role-based back office** where each role sees and does only what it should.
5. Give the owner **real-time analytics**: revenue, average order value, top dishes, low stock, order mix and CSV reports.
6. Add an **AI assistant** that understands requests such as *"spicy veg dinner for two under ₹600"* and answers only with real, in-stock dishes.
7. Follow **industry practice**: layered architecture, SOLID, design patterns, validation, secure authentication, automated tests, containerisation and documentation.

## 1.3 Existing system and its limitations

| Existing approach | Limitations |
|---|---|
| Phone / walk-in orders written on paper | Errors, no history, no analytics, staff tied up on calls |
| Third-party aggregator apps | 18–30% commission, the restaurant doesn't own the customer relationship or data, little control over menu presentation |
| Generic website with a contact form | No cart, no pricing logic, no stock, no tracking |
| Spreadsheet stock and sales tracking | Manual, error-prone, not linked to orders, so overselling still happens |

## 1.4 Proposed system
BiteBox replaces these with one integrated system:
- **Customer storefront:** a responsive single-page application with search and filters, dish details, a guest cart that merges into the account on sign-in, checkout with live price quotes, payment, order tracking, reviews, favourites and saved addresses.
- **BiteBox Admin:** dashboard, kitchen queue, menu and category management with image upload, inventory with a complete movement ledger, promotions, people management, and reports.
- **BiteBot:** natural-language food recommendations, grounded in the live catalogue.
- **A secure REST API:** enforces every business rule and stores everything in one MySQL database.

**Advantages over the existing system:** no commission, accurate automatic pricing, no overselling, live tracking for customers, live numbers for the owner, a complete audit trail (status history, stock movements), and an easy path to real payments and notifications through adapter interfaces.

## 1.5 Feasibility study
- **Technical feasibility:** every technology is mature, free and already installed on the development machine (.NET SDK 9, Node 24, Angular CLI 22, MySQL 8). The architecture runs on a single small server or in Docker.
- **Economic feasibility:** no licence costs. The AI assistant works offline with its rule-based engine; the optional Claude API is pay-per-use. Payment is simulated for the project. A real gateway such as Razorpay charges only per transaction.
- **Operational feasibility:** staff screens are task-focused: the kitchen sees a queue with one-click next steps. Customers use a familiar e-commerce flow. The interface is responsive from 360 px phones to desktops.
- **Schedule feasibility:** the work was split into phases (audit → architecture → backend core → frontend → admin → AI → tests → deployment → documentation). Each phase was built and verified before the next.

## 1.6 Development methodology
An **iterative, incremental** model was used. Each increment was designed, implemented, built, tested and documented before the next began:

1. **Phase 0: environment audit.** Find the installed toolchain and adapt the plan to it (for example, .NET 9 instead of 10).
2. **Architecture and domain model.** Entities, the order state machine, pricing strategies.
3. **Persistence.** EF Core mappings, migrations, the seeder.
4. **Application services and API.** Validation, authorisation, error contract.
5. **Angular storefront.** Then the admin back office.
6. **BiteBot.** The AI assistant.
7. **Automated tests.** Defects were found and fixed along the way; see the Testing chapter.
8. **Docker, CI and documentation.**

## 1.7 Hardware and software requirements

| | Minimum (development / demo) |
|---|---|
| **Processor** | Dual-core 64-bit CPU |
| **Memory** | 8 GB RAM (16 GB recommended when running IDE, MySQL, API and the Angular dev server together) |
| **Disk** | 5 GB free (SDKs, node_modules, database) |
| **Operating system** | Windows 10/11, Linux or macOS |
| **Software** | .NET SDK 9, Node.js 22+ / npm, Angular CLI 22, MySQL 8.0+, a modern browser; optionally Docker Desktop |
| **Client** | Any modern browser on a phone, tablet or PC |

## 1.8 Organisation of this report
- **Chapter 1** (this chapter) gives the project overview.
- **Chapter 2** shows the application through screenshots of the running system.
- **The Software Requirements Specification and System Design** follows as the main body of the report. It covers:
  - requirements: functional, non-functional and interfaces;
  - architecture;
  - a walkthrough of the backend and frontend code;
  - database design;
  - key algorithms;
  - security, testing, deployment and traceability.
- **The final chapter** gives the conclusion, limitations, future scope and references.
