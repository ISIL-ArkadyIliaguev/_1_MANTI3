---
type: index
title: MANTI 3 Knowledge Bundle
updated: 2026-10-06
---

# MANTI 3 — Knowledge Bundle (OKF)

This directory is an **Open Knowledge Format (OKF)** bundle: one concept per
Markdown file, each with YAML frontmatter (`type` is the one required field),
cross-linked with plain Markdown links. The format is portable — any AI tool or
plain text editor can read it, with no special tooling.

## Contents

- [Architecture](architecture.md) — the two-project design and the HTTP boundary.
- [Decision: greenfield, two projects](decision-greenfield-two-projects.md) — why MANTI 3 is a fresh solution, not a folder in MANTI 2.
- [Conventions](conventions.md) — the rules for building features here.
- [Methodology: OKF + Spec Kit](methodology-okf-speckit.md) — how we capture knowledge and drive development.
- [Glossary](glossary.md) — agentic-development terms, bilingual.
- [MANTI 2 permissions model](permissions-schema.md) — users, work-center checkmarks, screen permissions, copy-from-user (draft from code).
- [Change log](log.md) — dated history of this bundle.

## How to extend this bundle

Add one Markdown file per new concept, give it a `type:` in the frontmatter,
link it from this index, and add a line to [the log](log.md).
