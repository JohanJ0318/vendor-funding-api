using VendorFunding.Core;

namespace VendorFunding.Api;

public sealed record CreateClaimRequest(DateOnly PeriodEnd, DateOnly? BilledDate);

public sealed record ClaimCreatedResponse(
    int ClaimId,
    int AgreementId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal Amount,
    decimal EarnedToDate);

public static class FundingEndpoints
{
    public static IEndpointRouteBuilder MapFundingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapGet("/vendors", async (IFundingRepository repo, CancellationToken ct) =>
            Results.Ok(await repo.GetVendorsAsync(ct)));

        app.MapGet("/agreements/{id:int}", async (int id, IFundingRepository repo, CancellationToken ct) =>
            await repo.GetAgreementAsync(id, ct) is { } agreement
                ? Results.Ok(agreement)
                : Results.NotFound());

        // How much has this vendor earned us so far?
        app.MapGet("/agreements/{id:int}/accrual", async (
            int id, DateOnly? asOf, IFundingRepository repo, TimeProvider clock, CancellationToken ct) =>
        {
            var agreement = await repo.GetAgreementAsync(id, ct);
            if (agreement is null)
                return Results.NotFound();

            var date = asOf ?? Today(clock);
            var activity = await repo.GetActivityAsync(id, agreement.StartDate, agreement.EndDate, ct);
            return Results.Ok(AccrualCalculator.CalculateEarned(agreement, activity, date));
        });

        app.MapGet("/agreements/{id:int}/claims", async (int id, IFundingRepository repo, CancellationToken ct) =>
            await repo.GetAgreementAsync(id, ct) is null
                ? Results.NotFound()
                : Results.Ok(await repo.GetClaimsAsync(id, ct)));

        // Bill the vendor for everything earned through PeriodEnd that has not been billed yet.
        app.MapPost("/agreements/{id:int}/claims", async (
            int id, CreateClaimRequest request, IFundingRepository repo, TimeProvider clock, CancellationToken ct) =>
        {
            var agreement = await repo.GetAgreementAsync(id, ct);
            if (agreement is null)
                return Results.NotFound();

            var priorClaims = await repo.GetClaimsAsync(id, ct);
            var periodStart = priorClaims.Count == 0
                ? agreement.StartDate
                : priorClaims.Max(c => c.PeriodEnd).AddDays(1);

            if (request.PeriodEnd < periodStart)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["periodEnd"] = [$"Must be on or after {periodStart:yyyy-MM-dd}; earlier periods are already billed."],
                });
            }

            var activity = await repo.GetActivityAsync(id, agreement.StartDate, agreement.EndDate, ct);
            var earned = AccrualCalculator.CalculateEarned(agreement, activity, request.PeriodEnd).Earned;
            var amount = AccrualCalculator.AmountToBill(earned, priorClaims);

            if (amount == 0)
            {
                return Results.Problem(
                    title: "Nothing to bill",
                    detail: "Everything earned through this date has already been billed.",
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var billedDate = request.BilledDate ?? Today(clock);
            var claimId = await repo.InsertClaimAsync(id, periodStart, request.PeriodEnd, amount, billedDate, ct);

            return Results.Created(
                $"/agreements/{id}/claims/{claimId}",
                new ClaimCreatedResponse(claimId, id, periodStart, request.PeriodEnd, amount, earned));
        });

        // Accounts-receivable aging of unpaid vendor claims.
        app.MapGet("/reports/aging", async (DateOnly? asOf, IFundingRepository repo, TimeProvider clock, CancellationToken ct) =>
            Results.Ok(await repo.GetAgingReportAsync(asOf ?? Today(clock), ct)));

        return app;
    }

    private static DateOnly Today(TimeProvider clock) =>
        DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
}
