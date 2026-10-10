namespace BookingApp.Core.Common;

/// <summary>A response as it was first sent, kept so a retry with the same key gets exactly the same answer.</summary>
public sealed record StoredResponse(int StatusCode, string Body, string ContentType, string? Location);

public enum ClaimOutcome
{
    /// <summary>The key is new for this caller: go ahead, then complete or release it.</summary>
    Claimed,
    /// <summary>The first request with this key finished: send its stored response again.</summary>
    Completed,
    /// <summary>The first request with this key is still running.</summary>
    InProgress,
    /// <summary>The key was used before for a different request.</summary>
    PayloadDiffers,
}

public sealed record Claim(ClaimOutcome Outcome, StoredResponse? Response = null);

/// <summary>
/// Remembers what was done with each Idempotency-Key, per caller, so a retried POST is harmless. Claiming must be
/// atomic (a unique key, never read-then-write) and the response stored before it is returned.
/// Library: 8.2 "Idempotent HTTP APIs and safe retries", 12.5 "Idempotency keys — preventing double charges".
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>Claims the key for this caller and request, or reports what happened to it before.</summary>
    /// <param name="scope">Who is asking: keys are only unique per caller.</param>
    /// <param name="requestHash">A fingerprint of the request; the same key with another body is refused.</param>
    Task<Claim> TryClaimAsync(string scope, string key, string requestHash, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Stores the response for the claimed key.</summary>
    Task CompleteAsync(string scope, string key, StoredResponse response, CancellationToken cancellationToken);

    /// <summary>Forgets a claim whose request failed unexpectedly, so a retry can try again.</summary>
    Task ReleaseAsync(string scope, string key, CancellationToken cancellationToken);
}

public static class Idempotency
{
    /// <summary>How long a key is remembered. A retry after this is treated as a new request.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromHours(24);

    /// <summary>
    /// A claim still "in progress" this long after it was made belongs to a request that died (a crash between claiming
    /// and completing); a retry may take it over.
    /// </summary>
    public static readonly TimeSpan AbandonedAfter = TimeSpan.FromMinutes(2);
}
