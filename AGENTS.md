# AGENTS.md — MANTI 3

> This file is the "constitution" for AI agents (and humans) in this repo.
> It is vendor-neutral (readable by Claude Code, GitHub Copilot, Spec Kit, …).
> Keep it short, factual, and up to date. Instructions here override an
> agent's default habits.

## What MANTI 3 is

MANTI 3 is a **new, standalone product** — a clean greenfield rebuild, not a
folder inside MANTI 2. It is split into **two projects**:

| Project | Tech | Role |
| --- | --- | --- |
| `src/Manti3.Frontend` | Blazor WebAssembly (net10.0) | The UI. Runs in the browser / WebView2. |
| `src/Manti3.Backend/` | ASP.NET Core Web API (net10.0), 4 projects: `Domain`, `Application`, `Infrastructure`, `Api` | The data & business logic. Built per the `clean-slice-dotnet-service` skill (`C:\skills`). |

The frontend is a **pure client**: it holds no business logic and reaches the
backend **only over HTTP/JSON**. The backend never renders UI.

## Relationship to MANTI 2 (important)

- **All real work happens here, in MANTI 3.**
- In MANTI 2 we touch **only `WebViewHostTest`** ("the host"): its WebView2 is
  pointed at the MANTI 3 frontend URL to *embed the display*. Nothing else in
  MANTI 2 is modified.
- So MANTI 2 is just a window that shows MANTI 3. The two stay decoupled.

## Project map

```
C:\_1_MANTI3\
  MANTI3.slnx                     solution (new .slnx format)
  AGENTS.md                       this file
  src\
    Manti3.Frontend\              Blazor WASM UI
    Manti3.Backend\               ASP.NET Core Web API (Clean Architecture + vertical slices)
      Manti3.Backend.Domain\
      Manti3.Backend.Application\ Features\<Area>\<UseCase>\
      Manti3.Backend.Infrastructure\
      Manti3.Backend.Api\         endpoints + Program.cs
  tests\                          unit, architecture, integration tests
```

## Rules (do not break these)

1. **Frontend ↔ Backend only over HTTP.** The frontend calls the backend's
   REST endpoints; it never shares a class library of business logic with it.
2. **Backend owns the data.** Validation and business rules live in the backend,
   never in the Blazor UI.
3. **CORS:** the backend must allow the frontend's origin (they run on
   different ports in dev).
4. **UI is RTL Hebrew** by default.
5. **Target framework is net10.0** for both projects.
6. Code identifiers are English; UI strings and comments may be Hebrew.
7. **The MANTI database is read-only for MANTI 3, with one exception:** SELECT only, no
   schema changes, no EF migrations. The exception (approved 2026-11-04) is the access-requests
   **"אישור ושמירה" button** (changed 2026-11-05 from two buttons to one): a human presses it, and
   the backend rebuilds `script.sql` from the values in the table, runs it on **SQL25DEV (Windows authentication) or SQL2025CLN2 (SQL login,
   `AllowWrite`, approved 2026-11-08), nowhere else**, first as a dry run that rolls back, and saves only if that dry run
   passed (the backend still refuses to save a script that did not dry-run in the same content).
   The script must pass `AccessScriptGuard` (INSERT into four tables (User_Profiles, Authorisations_WorkCtrs, Authorizations, User_Toolbars) and one UPDATE; nothing else). The agent itself still never writes. Secrets come from
   user-secrets / env vars.
8. **MANTI 2 code may be changed only on the `HONING_TEST` branch.**
9. **Backend code follows `C:\skills\clean-slice-dotnet-service`** (layers,
   slices, `Result<T>`, ProblemDetails, health probes, tests first).

## How we work (methodology)

This project is also a feasibility test of two AI-assisted methods:

- **Spec-Driven Development (Spec Kit):** non-trivial features are built through
  the `/speckit-*` flow — `specify` → `plan` → `tasks` → `implement`. Write the
  spec before the code.
- **OKF (Open Knowledge Format):** durable project knowledge is captured as a
  bundle of Markdown files with YAML frontmatter, so it is portable across tools.

## Adding a feature (default checklist)

1. Define the API contract in the backend (endpoint + request/response shape).
2. Implement the backend logic and return JSON.
3. In the frontend, call the endpoint with `HttpClient` and render the result.
4. Add the page route and a nav-menu entry in the frontend.
5. Keep the frontend dumb: no business rules, just display + user input.
