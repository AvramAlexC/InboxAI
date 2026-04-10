# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

WISMO AI is a multi-tenant "Where Is My Order" platform for Romanian e-commerce. It ingests customer support messages, classifies them via OpenAI (intent: WISMO/REFUND/QUESTION/SPAM), extracts order IDs and emails, generates draft responses in Romanian, and tracks AWB shipment statuses from Romanian courier services (Sameday, FanCourier, Cargus).

## Build & Run Commands

### Backend (.NET 8, SQLite)
```bash
# Build
dotnet build Wismo.Api/Wismo.Api.sln

# Run API (serves on http://localhost:5146 by default)
dotnet run --project Wismo.Api

# Run all tests
dotnet test Wismo.Api.Tests

# Run a single test class
dotnet test Wismo.Api.Tests --filter "FullyQualifiedName~OpenAIProcessorServiceTests"

# Run a single test method
dotnet test Wismo.Api.Tests --filter "FullyQualifiedName~OpenAIProcessorServiceTests.MethodName"
```

### Frontend (React + Vite)
```bash
cd wismo-ui
npm install
npm run dev          # dev server on http://localhost:5173
npm run build        # production build
npm run test         # vitest (single run)
npm run test:watch   # vitest (watch mode)
npm run lint         # eslint
```

## Architecture

### Backend — Vertical Slice + MediatR

- **Features/** — organized by domain area (Auth, Dashboard, Tickets, Shopify). Each feature folder contains endpoint mappings (minimal APIs) and MediatR command/query handlers.
- **MediatR pipeline** — commands go through `ValidationBehavior<,>` (FluentValidation) before reaching handlers.
- **Multitenancy** — tenant isolation via JWT `tenant_id` claim. `ITenantContext` (resolved from `HttpTenantContext`) provides the current tenant. `AppDbContext` applies a global query filter on `SupportTicket` by `TenantId`.
- **Couriers/** — `ICourierStatusClient` interface with implementations for each courier (Sameday, FanCourier, Cargus). AWB references follow `COURIER:AWB` format (e.g., `SAMEDAY:123456789`). `AwbReferenceParser` splits these. `AwbStatusMapper` normalizes external statuses to internal ones.
- **Jobs/** — Quartz.NET cron job (`AwbStatusUpdateJob`) periodically syncs AWB statuses for in-transit tickets via `IAwbStatusSyncService`.
- **Services/OpenAIProcessorService** — calls OpenAI API to classify tickets. Has regex-based fallback extraction for order IDs and emails when AI fails. Uses Polly for retry, circuit breaker, and timeout policies.
- **Realtime/** — SignalR hub (`TenantDashboardHub`) pushes dashboard updates to connected tenant clients.
- **Repositories/** — repository + unit of work pattern over EF Core/SQLite.

### Frontend — React 18 + Vite

- **src/session/** — session/token storage utilities
- **src/hooks/** — `useStoreSession` for auth state management
- **src/lib/** — API client (axios) and SignalR client
- **src/config/** — API configuration
- Tailwind CSS v4 for styling

## Key Patterns

- **Namespaces**: Backend uses both `Wismo.Api.*` and `WismoAI.Api.*` / `WismoAI.Core.*` namespaces (the latter for Tickets feature and Services).
- **Tests**: xUnit + Moq + FluentAssertions. Test project mirrors the API folder structure.
- **Configuration**: copy `appsettings.Example.json` to `appsettings.json` for local setup. Key sections: `OpenAI`, `Jwt`, `Couriers`, `AwbTracking`, `Shopify`.
- **Database**: SQLite (`wismo.db`), auto-created on startup via `EnsureCreated()`. Some tables (`StoreUsers`, `ShopifyStoreConnections`) are created with raw SQL in `Program.cs`.
- **Validation messages** and **draft responses** are in Romanian.
