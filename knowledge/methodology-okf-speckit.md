---
type: concept
title: "Methodology: OKF + Spec Kit"
updated: 2026-10-06
---

# Methodology: OKF + Spec Kit

MANTI 3 is also a feasibility test of two complementary AI-assisted methods.

## OKF — Open Knowledge Format

A vendor-neutral standard (published by Google Cloud, June 2026) for packaging
knowledge as a **directory of Markdown files** with YAML frontmatter. The only
required frontmatter field is `type`. Optional `index.md` and `log.md` aid
navigation and history. Cross-links are plain Markdown links.

**We use it for:** durable project knowledge — exactly this `knowledge/` bundle.
It is the *format for knowledge*.

## Spec Kit — Spec-Driven Development (SDD)

GitHub's open-source toolkit that gives an AI agent a **structured process**
instead of jumping straight to code. The SDD flow: `constitution` → `specify` →
`plan` → `tasks` → `implement` → `converge`, each a `/speckit-*` skill with human
review between steps.

**We use it for:** building non-trivial features. It is the *process for work*.
Requires Python 3.11+ and the `uv` package manager to install.

## How they fit together

OKF answers "what do we know?"; Spec Kit answers "how do we build?". A feature's
spec and plan (Spec Kit) can graduate into durable concept files (OKF) once the
feature ships.

Related: [Glossary](glossary.md) · [Conventions](conventions.md)
