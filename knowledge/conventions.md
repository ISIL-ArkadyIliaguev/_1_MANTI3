---
type: convention
title: Conventions
updated: 2026-10-06
---

# Conventions

These mirror the operational rules in the repo's root `AGENTS.md`; this file is
the *explained* version for humans learning the project.

1. **Frontend ↔ Backend over HTTP only.** No shared business-logic library.
2. **Backend owns the data.** Validation and rules live in the backend, never in
   the Blazor UI. The UI only displays and collects input.
3. **CORS** on the backend must allow the frontend origin (different dev ports).
4. **RTL Hebrew UI** by default.
5. **net10.0** for both projects.
6. Code identifiers in English; UI strings and comments may be Hebrew.

## Adding a feature

1. Define the backend endpoint (request/response shape).
2. Implement backend logic; return JSON.
3. Call it from the frontend with `HttpClient`; render the result.
4. Add the page route and a nav-menu entry.

Related: [Architecture](architecture.md) · [Methodology](methodology-okf-speckit.md)
