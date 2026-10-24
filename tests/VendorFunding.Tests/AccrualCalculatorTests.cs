using VendorFunding.Core;

namespace VendorFunding.Tests;

public class AccrualCalculatorTests
{
    private static readonly DateOnly March1 = new(2026, 3, 1);
    private static readonly DateOnly March31 = new(2026, 3, 31);

    private static FundingAgreement Agreement(
        FundingType type, decimal rate = 0m, decimal lumpSum = 0m, decimal? cap = null) =>
        new(1, 1, "test", type, March1, March31, rate, lumpSum, cap);

    private static ItemActivity Day(int month, int day, int purchased = 0, int sold = 0) =>
        new(new DateOnly(2026, month, day), "ITEM", purchased, sold);

    [Fact]
    public void Billback_pays_on_units_purchased_inside_the_window_only()
    {
        var activity = new[]
        {
            Day(2, 28, purchased: 999),   // day before the window
            Day(3, 2, purchased: 120),
            Day(3, 9, purchased: 80),
            Day(4, 1, purchased: 500),    // day after the window
        };

        var result = AccrualCalculator.CalculateEarned(Agreement(FundingType.Billback, rate: 0.50m), activity, March31);

        Assert.Equal(100.00m, result.Earned);   // 200 units x $0.50
        Assert.False(result.CapApplied);
    }

    [Fact]
    public void Billback_ignores_units_sold()
    {
        var result = AccrualCalculator.CalculateEarned(
            Agreement(FundingType.Billback, rate: 1m), new[] { Day(3, 5, sold: 50) }, March31);

        Assert.Equal(0m, result.Earned);
    }

    [Fact]
    public void Scan_based_pays_on_units_sold()
    {
        var activity = new[] { Day(3, 5, purchased: 1000, sold: 90), Day(3, 12, sold: 70) };

        var result = AccrualCalculator.CalculateEarned(Agreement(FundingType.ScanBased, rate: 0.25m), activity, March31);

        Assert.Equal(40.00m, result.Earned);    // 160 units x $0.25
    }

    [Fact]
    public void Activity_after_the_as_of_date_is_not_counted_yet()
    {
        var activity = new[] { Day(3, 5, sold: 10), Day(3, 20, sold: 10) };

        var result = AccrualCalculator.CalculateEarned(
            Agreement(FundingType.ScanBased, rate: 1m), activity, new DateOnly(2026, 3, 15));

        Assert.Equal(10m, result.Earned);
    }

    [Fact]
    public void Cap_limits_the_payout_and_is_reported()
    {
        var activity = new[] { Day(3, 5, sold: 200) };   // $50 earned before the cap

        var result = AccrualCalculator.CalculateEarned(
            Agreement(FundingType.ScanBased, rate: 0.25m, cap: 40m), activity, March31);

        Assert.Equal(40m, result.Earned);
        Assert.True(result.CapApplied);
    }

    [Fact]
    public void Capped_amount_is_whole_cents_even_when_the_cap_has_four_decimal_places()
    {
        // SQL Server returns the cap as DECIMAL(19,4): 40.0000, or 40.0050 for an odd contract value.
        var activity = new[] { Day(3, 5, sold: 200) };

        var result = AccrualCalculator.CalculateEarned(
            Agreement(FundingType.ScanBased, rate: 0.25m, cap: 40.0050m), activity, March31);

        Assert.Equal("40.01", result.Earned.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Nothing_is_earned_before_the_agreement_starts()
    {
        var result = AccrualCalculator.CalculateEarned(
            Agreement(FundingType.LumpSum, lumpSum: 3100m), Array.Empty<ItemActivity>(), new DateOnly(2026, 2, 15));

        Assert.Equal(0m, result.Earned);
    }

    [Theory]
    [InlineData(1, 100.00)]      // first day: 1/31 of $3,100
    [InlineData(10, 1000.00)]
    [InlineData(31, 3100.00)]    // last day: the full amount
    public void Lump_sum_is_earned_evenly_per_day(int dayOfMarch, decimal expected)
    {
        var result = AccrualCalculator.CalculateEarned(
            Agreement(FundingType.LumpSum, lumpSum: 3100m), Array.Empty<ItemActivity>(), new DateOnly(2026, 3, dayOfMarch));

        Assert.Equal(expected, result.Earned);
    }

    [Fact]
    public void Lump_sum_after_the_window_is_exactly_the_full_amount_with_no_rounding_loss()
    {
        // $1,000 over 31 days does not divide evenly; the final total must still be exact.
        var result = AccrualCalculator.CalculateEarned(
            Agreement(FundingType.LumpSum, lumpSum: 1000m), Array.Empty<ItemActivity>(), new DateOnly(2026, 5, 1));

        Assert.Equal(1000m, result.Earned);
    }

    [Fact]
    public void Agreement_that_ends_before_it_starts_is_rejected()
    {
        var bad = new FundingAgreement(1, 1, "bad", FundingType.Billback, March31, March1, 1m, 0m, null);

        Assert.Throws<ArgumentException>(() =>
            AccrualCalculator.CalculateEarned(bad, Array.Empty<ItemActivity>(), March31));
    }

    [Theory]
    [InlineData(2.345, 2.35)]    // half away from zero, not banker's rounding (2.34)
    [InlineData(2.344, 2.34)]
    [InlineData(-2.345, -2.35)]
    public void Money_rounds_half_away_from_zero(decimal input, decimal expected)
    {
        Assert.Equal(expected, AccrualCalculator.RoundToCents(input));
    }

    [Fact]
    public void Amount_to_bill_subtracts_what_was_already_billed()
    {
        var prior = new[] { new Claim(1, 1, March1, new DateOnly(2026, 3, 15), 100m, 100m, March31) };

        Assert.Equal(30m, AccrualCalculator.AmountToBill(130m, prior));
    }

    [Fact]
    public void Amount_to_bill_is_never_negative()
    {
        var prior = new[] { new Claim(1, 1, March1, March31, 150m, 0m, March31) };

        Assert.Equal(0m, AccrualCalculator.AmountToBill(130m, prior));
    }
}
