using BookingApp.Core.Common;

namespace BookingApp.Infrastructure.Repositories;

/// <summary>Idempotency keys in memory, for the InMemory provider. Claims are atomic under one lock.</summary>
internal sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private sealed record Entry(string RequestHash, DateTimeOffset ClaimedAt, StoredResponse? Response);

    private readonly Lock _gate = new();
    private readonly Dictionary<(string Scope, string Key), Entry> _entries = [];

    public Task<Claim> TryClaimAsync(string scope, string key, string requestHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue((scope, key), out var existing) && existing.ClaimedAt + Idempotency.KeepFor > now)
            {
                if (existing.RequestHash != requestHash) return Task.FromResult(new Claim(ClaimOutcome.PayloadDiffers));
                if (existing.Response is not null) return Task.FromResult(new Claim(ClaimOutcome.Completed, existing.Response));
                if (existing.ClaimedAt + Idempotency.AbandonedAfter > now) return Task.FromResult(new Claim(ClaimOutcome.InProgress));
            }
            _entries[(scope, key)] = new Entry(requestHash, now, null);
            return Task.FromResult(new Claim(ClaimOutcome.Claimed));
        }
    }

    public Task CompleteAsync(string scope, string key, StoredResponse response, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue((scope, key), out var entry)) _entries[(scope, key)] = entry with { Response = response };
        }
        return Task.CompletedTask;
    }

    public Task ReleaseAsync(string scope, string key, CancellationToken cancellationToken)
    {
        lock (_gate) _entries.Remove((scope, key));
        return Task.CompletedTask;
    }
}
