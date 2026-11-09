---
type: decision
title: "Decision: greenfield solution with two projects"
status: accepted
date: 2026-10-06
---

# Decision: greenfield solution with two projects

## Context

MANTI 2 already has a modern experiment (`ModernModules/ModernApp`, a Blazor
Server modular monolith embedded in a WPF shell). The question was whether MANTI 3
should grow inside that, or start clean.

## Decision

MANTI 3 is a **new, standalone solution** at `C:\_1_MANTI3`, built as **two
projects**: a Blazor WebAssembly frontend and an ASP.NET Core Web API backend.

MANTI 2 is **not modified**, except for `WebViewHostTest` (the host), whose
WebView2 will display the MANTI 3 frontend. MANTI 2 becomes just a window onto
MANTI 3.

## Why

- A clean project is the ideal ground to evaluate the OKF and Spec Kit
  methodologies without the noise of existing code.
- A true frontend/backend split (two projects) is clearer and more replaceable
  than a single combined app.
- Keeping MANTI 2 untouched removes risk from the legacy system.

## Consequences

- We maintain a new solution and its CI/build separately.
- The frontend/backend split requires CORS and an explicit HTTP contract.
- Integration with MANTI 2 is limited to pointing the host's WebView2 at a URL.

Related: [Architecture](architecture.md)
