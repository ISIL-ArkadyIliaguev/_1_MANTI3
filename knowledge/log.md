---
type: log
title: Change Log
updated: 2026-10-06
---

# Change Log

Dates are ISO-8601 (YYYY-MM-DD).

- **2026-10-06** — Bundle created. MANTI 3 solution scaffolded at `C:\_1_MANTI3`
  (`Manti3.Frontend` = Blazor WASM, `Manti3.Backend` = ASP.NET Core Web API,
  both net10.0). Recorded the architecture, the greenfield decision, the
  conventions, the methodology, and the glossary.
- **2026-10-07** — Installed `uv` (0.12.23) and `specify-cli` (1.1.1).
  Initialized Spec Kit in the project for Claude Code (`specify init --here
  --integration claude`). Added `.specify/` (templates, scripts, constitution
  template) and `.claude/skills/speckit-*` (the SDD command skills). Next:
  fill the constitution and specify the "list + menu" sample feature.
- **2026-10-28** — Added [MANTI 2 permissions model](permissions-schema.md), a draft read from
  MANTI 2 code for the access-requests agent (spec 002). Table names and stored-procedure bodies
  are still to be verified in SSMS.
