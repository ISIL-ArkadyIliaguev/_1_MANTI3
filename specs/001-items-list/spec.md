# Feature Specification: Items list (Honing periphery page)

**Status**: Implemented (written retroactively; see Known gaps)
**Constitution**: v2.1.0

## Summary
A MANTI 2 user opens Catalogue > Auxiliary equipment > Honing periphery page and sees the list of
item codes held in the MANTI table `HoningPeripheralPage`. MANTI 3 only reads the data.

## Requirements
- **FR-1**: `GET /api/items` returns every row's `ItemCode` as a JSON array of strings, in the
  order the database returns them, with leading and trailing spaces removed.
- **FR-2**: A row whose `ItemCode` is NULL is returned as an empty string.
- **FR-3**: An empty table returns `200` with `[]`.
- **FR-4**: MANTI 3 never changes the MANTI database: SELECT only, no migrations (constitution).
- **FR-5**: The frontend page `/` shows the codes in a one-column table with a row count, in RTL Hebrew.
- **FR-6**: When the database is unreachable the API returns `500` as ProblemDetails without
  exception details, and `/health/ready` returns `503` while `/health/live` stays `200`.

## Contract
| Request | Success | Failure |
|---|---|---|
| `GET /api/items` | `200 application/json` `["8421348", ...]` | `500 application/problem+json` |
| `GET /health/live` | `200` text `Healthy` | n/a (process check only) |
| `GET /health/ready`, `/health/startup` | `200` `Healthy` | `503` `Unhealthy` |

There is no input, so there is no `400`; the list has no identity, so there is no `404`.

## Non-functional
- Cache: none (the table is small and must show current data). Redis is not used.
- Probe impact: the database check is tagged `ready` and `startup` only, never `live`.
- Security: CORS allows only the configured frontend origins. Database user and password come
  from user-secrets or environment variables.

## Known gaps (recorded per constitution VI)
- **Authentication/authorisation**: not implemented; every caller can read the list.
- **Production database**: the Development settings point at the Tefen production server with a
  shared account; a dedicated read-only account is recommended.
- **Not adopted yet**: Redis cache, OpenTelemetry export, Kubernetes manifests, CI pipeline,
  Playwright E2E for the frontend.
- **Process**: this spec was written after the code; future features must follow
  specify > plan > tasks > implement first.
