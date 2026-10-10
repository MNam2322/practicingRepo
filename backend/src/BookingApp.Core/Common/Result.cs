namespace BookingApp.Core.Common;

/// <summary>Why an operation was refused. The API maps each kind to an HTTP status.</summary>
public enum ErrorKind
{
    /// <summary>The thing asked for does not exist (404).</summary>
    NotFound,
    /// <summary>The request breaks a business rule, such as booking in the past (422).</summary>
    RuleViolation,
    /// <summary>The request clashes with the current state, such as a slot already taken (409).</summary>
    Conflict,
    /// <summary>The caller may not do this to this booking (403).</summary>
    Forbidden,
    /// <summary>The caller is not signed in, or their credentials are wrong (401).</summary>
    Unauthenticated,
    /// <summary>The payment was refused, such as a declined card (402).</summary>
    PaymentDeclined,
    /// <summary>Something the request depends on did not answer, such as the payment provider; try again (503).</summary>
    Unavailable,
}

/// <param name="Code">Stable, machine-readable code such as "slot_taken". Clients switch on this.</param>
/// <param name="Message">A sentence a person can read.</param>
public sealed record Error(ErrorKind Kind, string Code, string Message);

/// <summary>
/// Success with a value, or an expected failure. Expected failures (a taken slot, a late cancellation)
/// are results, not exceptions: exceptions are for bugs and broken infrastructure.
/// </summary>
public readonly record struct Result<T>
{
    private Result(T? value, Error? error) { Value = value; Error = error; }

    public T? Value { get; }
    public Error? Error { get; }
    public bool IsSuccess => Error is null;

    public static Result<T> Ok(T value) => new(value, null);
    public static Result<T> Fail(Error error) => new(default, error);

    public static implicit operator Result<T>(Error error) => Fail(error);
}
