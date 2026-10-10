namespace BookingApp.Core.Payments;

/// <summary>The "Payments" settings: the deposit a booking takes, and how often refunds that failed are retried.</summary>
public sealed class PaymentOptions
{
    public const string Section = "Payments";

    /// <summary>ISO 4217 code. Must be one <see cref="Money"/> knows.</summary>
    public string Currency { get; set; } = "VND";

    /// <summary>The deposit per booking, in major units (100000 VND). 0 turns deposits off: confirming is free.</summary>
    public decimal Deposit { get; set; } = 100_000;

    /// <summary>How often the refund job looks for deposits to give back.</summary>
    public int SweepSeconds { get; set; } = 30;

    public Money DepositAmount => Money.FromMajor(Deposit, Currency);
}
