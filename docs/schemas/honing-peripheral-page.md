---
type: schema
title: HoningPeripheralPage table
description: MANTI table read by MANTI 3 to list item codes; owned by MANTI 2, read-only for MANTI 3.
tags: [sql-server, manti-db, read-only]
related_concepts: [../api/get-items.md]
last_updated: 2026-10-15
---

# dbo.HoningPeripheralPage

Database `Manti_IscarData` (server `DBS3,2433`). MANTI 3 only runs `SELECT`; it never changes the
schema or data, and EF Core migrations are excluded (`ExcludeFromMigrations`).

| Column | Type | Null | Used by MANTI 3 |
|---|---|---|---|
| `ItemCode` | text | yes | yes: returned by `GET /api/items` |

Only the columns MANTI 3 reads are listed. Other columns exist but are not mapped.

## Indexes
None known; not needed for a full read of a small table.
