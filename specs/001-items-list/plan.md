# Plan: Items list

## Slice
- Application: `Features/Items/GetItems/` (`GetItemsQuery`, `GetItemsHandler`, `IItemsReader`)
- Api: `Endpoints/Items/GetItemsEndpoint.cs` (`GET /api/items`)
- Infrastructure: `Persistence/ItemsReader.cs` implements the port with EF Core `AsNoTracking`
- Domain: `Items/HoningPeripheralItem` (anemic read model)

## DDD
Not needed: plain read of reference data, no invariants, no events.

## EF Core
- `AppDbContext` with a keyless entity mapped to `dbo.HoningPeripheralPage`, column `ItemCode`.
- `ExcludeFromMigrations()`: the table belongs to MANTI 2. No migrations are created or applied.
- `EnableRetryOnFailure()` on SQL Server.

## Configuration keys
| Key | Source |
|---|---|
| `ConnectionStrings:Default` | appsettings (server and database only, no credentials) |
| `Db:User`, `Db:Password` | user-secrets or env vars `Db__User`, `Db__Password` |
| `Cors:AllowedOrigins` | appsettings |

## Tests (written for each requirement)
- Unit: `GetItemsHandlerTests` (FR-1, FR-2, FR-3); `ArchitectureTests` (layer and slice rules).
- Integration (Testcontainers, SQL Server 2025): `ItemsApiTests` (FR-1, FR-3, FR-6, probes).

## Kubernetes / CI
No changes: not adopted yet (see spec, Known gaps).
