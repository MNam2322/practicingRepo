using System.Globalization;
using BookingApp.Core.Payments;

namespace BookingApp.Core.Tests;

/// <summary>Money and the payment's changes. Library: 4.9 Case 4 ("pin that conversion with tests").</summary>
public class MoneyTests
{
    [Theory]
    [InlineData("100000", "VND", 100_000L, "100000 VND")]
    [InlineData("12.50", "USD", 1250L, "12.50 USD")]
    [InlineData("12800", "JPY", 12_800L, "12800 JPY")]
    public void Money_uses_the_currency_exponent_and_invariant_culture(string amount, string currency, long minor, string text)
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");   // a host culture that writes "12,50"
            var money = Money.FromMajor(decimal.Parse(amount, CultureInfo.InvariantCulture), currency);
            Assert.Equal(minor, money.Minor);
            Assert.Equal(text, money.ToString());
            Assert.Equal(decimal.Parse(amount, CultureInfo.InvariantCulture), money.Major);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Too_many_decimals_or_an_unknown_currency_are_refused_not_guessed()
    {
        Assert.Throws<ArgumentException>(() => Money.FromMajor(0.5m, "VND"));
        Assert.Throws<ArgumentException>(() => Money.FromMajor(1.005m, "USD"));
        Assert.Throws<NotSupportedException>(() => Money.FromMajor(1, "XYZ"));
        Assert.False(Money.IsSupported("XYZ"));
        Assert.True(Money.IsSupported("VND"));
    }
}

public class PaymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static Payment New() => Payment.Start(Guid.NewGuid(), new Money(100_000, "VND"), Now);

    [Fact]
    public void A_payment_is_paid_once_and_refunded_once()
    {
        var payment = New();
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.True(payment.MarkPaid("ch_1", Now));
        Assert.False(payment.MarkPaid("ch_2", Now));   // the provider repeating itself
        Assert.Equal("ch_1", payment.ProviderReference);
        Assert.False(payment.MarkFailed("card_declined"));

        Assert.True(payment.MarkRefunded(Now.AddHours(1)));
        Assert.False(payment.MarkRefunded(Now.AddHours(2)));
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(Now.AddHours(1), payment.RefundedAt);
        Assert.False(payment.MarkPaid("ch_1", Now));
    }

    [Fact]
    public void A_failed_payment_stays_failed_and_nothing_unpaid_is_refunded()
    {
        var payment = New();
        Assert.False(payment.MarkRefunded(Now));
        Assert.True(payment.MarkFailed("insufficient_funds"));
        Assert.Equal("insufficient_funds", payment.FailureCode);
        Assert.False(payment.MarkPaid("ch_1", Now));
        Assert.False(payment.MarkRefunded(Now));
        Assert.Equal(PaymentStatus.Failed, payment.Status);
    }
}
