# Vendor Funding API

A small ASP.NET Core (.NET 8) Web API on SQL Server that tracks **vendor funding**: money a supplier pays a retailer for promoting, stocking or discounting its products. It calculates how much each deal has earned, bills the vendor for the unbilled portion, and reports unpaid claims by age.

Built to learn .NET, C# and SQL Server in a finance setting, using AI-assisted development (Claude Code) with every line reviewed and tested by me.

## What it does

| Funding type | Vendor pays | Example in seed data |
|---|---|---|
| Billback | A rate per unit the retailer **buys** during the window | 260 gallons of milk x $0.50 = $130.00 |
| Scan-based | A rate per unit the retailer **sells** during the window | 200 bags x $0.25 = $50.00, capped at $40.00 |
| Lump sum | A fixed fee, earned evenly per day across the window | $3,100 display fee, $100 per day |
| Off-invoice | A percent off the unit cost of each unit the retailer **buys** during the window | 10% off $528.50 of juice purchases = $52.85 |

Rules the code enforces, each covered by a test:

- Only activity inside the agreement window, and on or before the as-of date, counts.
- An optional cap limits the total payout, and the response says when it was applied.
- Each new claim bills only what was earned since the last claim, never a negative amount, and a period cannot be billed twice.
- Money rounds half away from zero ($2.345 to $2.35), not .NET's default banker's rounding.
- A lump sum ends at exactly the full amount, with no penny lost to rounding.

## Endpoints

| Method | Route | Purpose |
|---|---|---|
| GET | `/vendors` | List vendors |
| GET | `/agreements/{id}` | One funding agreement |
| GET | `/agreements/{id}/accrual?asOf=YYYY-MM-DD` | Amount earned so far |
| GET | `/agreements/{id}/claims` | Claims billed so far |
| POST | `/agreements/{id}/claims` | Bill everything earned through `periodEnd` that is not yet billed |
| GET | `/reports/aging?asOf=YYYY-MM-DD` | Unpaid claims in 0-30 / 31-60 / 61-90 / 90+ day buckets |

Errors use the standard Problem Details format: 404 for an unknown agreement, 400 for a period already billed, 422 when there is nothing new to bill.

## Project layout

```
src/VendorFunding.Core          Business rules. No database, no web: pure C#, fully unit tested.
src/VendorFunding.Data          SQL Server access with ADO.NET and hand-written, parameterized T-SQL.
src/VendorFunding.Api           Minimal API endpoints. Depends on an interface, not on SQL Server.
tests/VendorFunding.Tests       xUnit: business rules plus HTTP tests with an in-memory database.
tests/VendorFunding.IntegrationTests   xUnit against a real SQL Server.
db/                             Schema, seed data, and reporting queries (aging, balances, window functions).
.github/workflows/ci.yml        Build and unit tests, then SQL Server in a container for integration tests.
```

## Run it locally

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and Docker.

```bash
# 1. Start SQL Server
docker compose up -d

# 2. Create the database and load sample data (sqlcmd ships inside the container)
docker compose cp db/. sqlserver:/tmp/db
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P Local_Dev_Pass1 -C -i /tmp/db/01_schema.sql
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P Local_Dev_Pass1 -C -i /tmp/db/02_seed.sql

# 3. Run the tests
dotnet test

# 4. Run the API, then try the calls in src/VendorFunding.Api/requests.http
dotnet run --project src/VendorFunding.Api
```

Re-running `01_schema.sql` and `02_seed.sql` resets the data, which the integration tests expect.

## Design choices

- **Plain ADO.NET instead of Entity Framework.** Every query is visible T-SQL, so the SQL can be reviewed, tuned and run by hand in SSMS. All values go in through parameters, never string concatenation, which is what prevents SQL injection.
- **`decimal`, never `double`, for money.** `DECIMAL(19,4)` in the database, `decimal` in C#.
- **Business rules separate from the database.** The aging rule exists in both SQL (for the report) and C# (for unit tests), and an integration test checks that they agree.
- **Injected `TimeProvider` instead of `DateTime.Now`.** "Today" can be controlled in tests.
