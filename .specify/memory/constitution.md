<!--
Sync Impact Report (temporary; remove before commit)
- Version change: 2.0.0 → 2.1.0 (MINOR: principle I clarified from "two projects" to "two deliverables"; new principle VI;
  read-only DB and secrets constraints added; nav-menu requirement dropped from III and the workflow)
- Earlier: 2.0.0 replaced the "WebViewHostTest only" rule with: any MANTI 2 change, only on `HONING_TEST`.
- Modified principles: I renamed "Two Projects, HTTP-Only Boundary" → "Two Deliverables, HTTP-Only Boundary";
  III (nav-menu removed). Added: VI. Backend Follows the Clean-Slice Standard.
- Added sections: Technology & Integration Constraints; Development Workflow
- Removed sections: none
- Deferred items: none. RATIFICATION_DATE set to the date of first adoption (2026-10-07).
- Source: AGENTS.md (kept as the runtime guidance file).
-->
# MANTI 3 Constitution

## Core Principles

### I. Two Deliverables, HTTP-Only Boundary
MANTI 3 MUST consist of exactly two deliverables: the frontend project `src/Manti3.Frontend` (Blazor WebAssembly) and the backend, a set of projects under `src/Manti3.Backend/` (ASP.NET Core
Web API, structured per Principle VI). The frontend MUST reach the backend only over HTTP/JSON and
MUST NOT reference any backend project. The two deliverables MUST NOT share a class library
containing business logic. The backend MUST NOT render UI. The backend MUST allow the frontend's origin through CORS, since
they run on different ports in development.

Rationale: a hard boundary keeps the UI replaceable and lets MANTI 2's `WebViewHostTest` embed
the frontend by URL without coupling.

### II. Backend Owns Data and Rules
All data access, validation, and business rules MUST live in the backend. The frontend MUST NOT
duplicate or enforce business rules beyond basic input affordances. Every endpoint MUST have a
defined request/response contract before it is implemented.

Rationale: a single source of truth for rules prevents divergent behavior between clients.

### III. Dumb Frontend
The frontend MUST only display data and capture user input, calling backend endpoints with
`HttpClient`. Each new page MUST have a route.

Rationale: keeps the client thin and testable through the backend contract.

### IV. RTL Hebrew UI
The UI MUST be right-to-left Hebrew by default. Code identifiers MUST be in English; UI strings
and comments MAY be in Hebrew.

Rationale: the product's users are Hebrew speakers; English identifiers keep the code portable.

### V. Spec-Driven Development
Non-trivial features MUST be built through the Spec Kit flow: specify → plan → tasks →
implement, with the spec written before the code. Durable project knowledge SHOULD be captured
as OKF (Open Knowledge Format) Markdown bundles with YAML frontmatter.

Rationale: this project is also a feasibility test of these two methods.

### VI. Backend Follows the Clean-Slice Standard
The backend MUST follow the `clean-slice-dotnet-service` skill (`C:\skills\clean-slice-dotnet-service`):
Clean Architecture layers (Domain, Application, Infrastructure, Api) organised as vertical slices
(one use case = one folder = one handler = one endpoint class), handlers returning `Result<T>`,
failures mapped to ProblemDetails, liveness/readiness/startup health probes, Serilog logging, and
tests written first (unit, architecture, integration). Configuration for a skill-covered topic that
the project has not adopted yet (Redis, OpenTelemetry export, Kubernetes, CI) MUST be recorded as a
known gap in the feature's spec, not silently skipped. Authentication is not implemented yet and is a
recorded known gap.

Rationale: one tested standard keeps every backend slice consistent and reviewable.

## Technology & Integration Constraints

- Both projects MUST target net10.0.
- MANTI 3 is a standalone greenfield product. MANTI 2 code MAY be changed as part of MANTI 3
  work (for example the host that embeds the MANTI 3 frontend), but ONLY on the `HONING_TEST`
  branch. Changes to MANTI 2 MUST NOT be made on any other branch.
- The MANTI database is owned by MANTI 2. MANTI 3 MUST NOT change its schema or data: access is
  read-only (SELECT), and no EF Core migrations are created or applied against it.
- Secrets (database user and password) MUST come from user-secrets or environment variables and
  MUST NOT be committed.
- The solution file is `MANTI3.slnx`.

## Development Workflow

Adding a feature MUST follow this order: (1) define the API contract in the backend, (2)
implement backend logic returning JSON, (3) call the endpoint from the frontend with
`HttpClient` and render the result, (4) add the page route. Changes MUST be
checked against the principles above during review.

## Governance

This constitution supersedes other practices in the repository. `AGENTS.md` is the runtime
guidance file for agents and humans and MUST remain consistent with it. Amendments MUST be made
via `/speckit-constitution`, documented with a Sync Impact Report, and update the version
according to semantic versioning: MAJOR for removed or redefined principles, MINOR for added
or materially expanded principles or sections, PATCH for clarifications. Features MUST be
reviewed for compliance at the plan stage; any violation MUST be justified in writing.

**Version**: 2.1.0 | **Ratified**: 2026-10-07 | **Last Amended**: 2026-10-15
