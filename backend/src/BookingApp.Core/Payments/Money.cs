using System.Globalization;

namespace BookingApp.Core.Payments;

/// <summary>
/// An amount of money in a currency's minor units (cents; for VND, the dong itself). Money crosses the payment boundary
/// as this value, never as a bare decimal, and each currency's exponent comes from a table: a currency without one is
/// refused, not guessed. Library: 4.9 Case 4 "Adapter: two payment providers, two money formats", 1.2 "Money, decimals
/// and rounding".
/// </summary>
public readonly record struct Money(long Minor, string Currency)
{
    /// <summary>ISO 4217 decimal places for the currencies this app accepts.</summary>
    public static int ExponentOf(string currency) => currency switch
    {
        "VND" or "JPY" or "KRW" => 0,
        "USD" or "EUR" or "GBP" or "AUD" or "SGD" => 2,
        _ => throw new NotSupportedException($"No exponent configured for currency '{currency}'."),
    };

    public static bool IsSupported(string? currency)
    {
        if (currency is null) return false;
        try
        {
            ExponentOf(currency);
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>From an amount in major units, such as 12.50 USD. Refuses more decimals than the currency has.</summary>
    public static Money FromMajor(decimal amount, string currency)
    {
        var scaled = amount * Scale(ExponentOf(currency));
        if (scaled != decimal.Truncate(scaled))
            throw new ArgumentException($"{amount.ToString(CultureInfo.InvariantCulture)} has more decimals than {currency} allows.", nameof(amount));
        return new Money((long)scaled, currency);
    }

    public int Exponent => ExponentOf(Currency);

    /// <summary>The amount in major units, such as 12.50.</summary>
    public decimal Major => Minor / Scale(Exponent);

    public bool IsZero => Minor == 0;

    /// <summary>"12.50 USD", "100000 VND": invariant culture, so the host's language never changes it.</summary>
    public override string ToString() => $"{Major.ToString("F" + Exponent, CultureInfo.InvariantCulture)} {Currency}";

    private static decimal Scale(int exponent) => exponent switch { 0 => 1m, 2 => 100m, _ => 1000m };
}
