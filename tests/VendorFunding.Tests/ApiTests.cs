using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VendorFunding.Api;
using VendorFunding.Core;

namespace VendorFunding.Tests;

/// <summary>
/// Runs the real HTTP pipeline in memory with the database swapped for a fake,
/// so routing, validation and status codes are tested without SQL Server.
/// </summary>
public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private HttpClient ClientWith(FakeFundingRepository repo) =>
        _factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<IFundingRepository>();
            services.AddSingleton<IFundingRepository>(repo);
        })).CreateClient();

    private static FakeFundingRepository SeededRepo()
    {
        var repo = new FakeFundingRepository();
        repo.Agreements.Add(new FundingAgreement(1, 1, "Spring milk billback", FundingType.Billback,
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), 0.50m, 0m, null));
        repo.Activity[1] = new List<ItemActivity>
        {
            new(new DateOnly(2026, 3, 2), "MILK-GAL", 120, 0),
            new(new DateOnly(2026, 3, 20), "MILK-GAL", 80, 0),
        };
        return repo;
    }

    [Fact]
    public async Task Accrual_returns_earned_amount()
    {
        var client = ClientWith(SeededRepo());

        var result = await client.GetFromJsonAsync<AccrualResult>("/agreements/1/accrual?asOf=2026-03-31");

        Assert.NotNull(result);
        Assert.Equal(100.00m, result!.Earned);
    }

    [Fact]
    public async Task Unknown_agreement_returns_404()
    {
        var client = ClientWith(SeededRepo());

        var response = await client.GetAsync("/agreements/999/accrual");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Creating_claims_bills_only_the_new_amount_each_period()
    {
        var repo = SeededRepo();
        var client = ClientWith(repo);

        var first = await client.PostAsJsonAsync("/agreements/1/claims",
            new CreateClaimRequest(new DateOnly(2026, 3, 15), new DateOnly(2026, 3, 16)));
        var second = await client.PostAsJsonAsync("/agreements/1/claims",
            new CreateClaimRequest(new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 1)));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var c1 = await first.Content.ReadFromJsonAsync<ClaimCreatedResponse>();
        var c2 = await second.Content.ReadFromJsonAsync<ClaimCreatedResponse>();
        Assert.Equal(60m, c1!.Amount);                        // 120 units by Mar 15
        Assert.Equal(40m, c2!.Amount);                        // 80 more units after
        Assert.Equal(new DateOnly(2026, 3, 16), c2.PeriodStart);
        Assert.Equal(100m, repo.Claims.Sum(c => c.AmountBilled));
    }

    [Fact]
    public async Task Billing_a_period_that_is_already_billed_is_a_validation_error()
    {
        var repo = SeededRepo();
        var client = ClientWith(repo);
        await client.PostAsJsonAsync("/agreements/1/claims",
            new CreateClaimRequest(new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 1)));

        var again = await client.PostAsJsonAsync("/agreements/1/claims",
            new CreateClaimRequest(new DateOnly(2026, 3, 20), null));

        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Single(repo.Claims);
    }

    [Fact]
    public async Task Nothing_earned_means_no_claim_is_created()
    {
        var repo = SeededRepo();
        repo.Activity[1].Clear();
        var client = ClientWith(repo);

        var response = await client.PostAsJsonAsync("/agreements/1/claims",
            new CreateClaimRequest(new DateOnly(2026, 3, 31), null));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Empty(repo.Claims);
    }
}
