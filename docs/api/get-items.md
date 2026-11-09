---
type: api
title: Get Items
description: Returns the ItemCode of every row in the MANTI table HoningPeripheralPage as a JSON array.
tags: [items, rest, read-only]
related_concepts: [../schemas/honing-peripheral-page.md, ../architecture/backend-layers.md]
last_updated: 2026-10-15
---

# GET /api/items

## Request
| Part | Value |
|---|---|
| Method and path | `GET /api/items` |
| Query / body | none |

## Responses
| Status | Content type | Body |
|---|---|---|
| 200 | `application/json` | `["8421348", "8421132"]` (trimmed codes, `[]` when the table is empty) |
| 500 | `application/problem+json` | RFC 9457 ProblemDetails, no exception details |

## Notes
- No input, so no `400`; the list has no identity, so no `404`.
- Read-only: executes a single `SELECT` ([schema](../schemas/honing-peripheral-page.md)).
- Authentication: not implemented (known gap, see `specs/001-items-list/spec.md`).
