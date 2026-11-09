---
type: concept
title: Architecture
updated: 2026-10-06
---

# Architecture

MANTI 3 is split into **two decoupled projects** that communicate only over HTTP.

| Project | Tech | Role |
| --- | --- | --- |
| `src/Manti3.Frontend` | Blazor WebAssembly (net10.0) | The UI. A pure client — no business logic. |
| `src/Manti3.Backend`  | ASP.NET Core Web API (net10.0) | Data and business logic. Returns JSON. |

## The boundary

The frontend reaches the backend **only through REST endpoints** (`HttpClient` →
JSON). They do not share a business-logic library. This keeps each side
replaceable: the UI could be rewritten without touching the backend, and vice
versa.

Because the two run on different ports in development, the backend must enable
**CORS** for the frontend's origin.

## Where it runs

The frontend is embedded in the legacy system through MANTI 2's `WebViewHostTest`
host, whose WebView2 points at the frontend URL. See
[the greenfield decision](decision-greenfield-two-projects.md) for why MANTI 2 is
otherwise untouched.

Related: [Conventions](conventions.md) · [Glossary](glossary.md)
