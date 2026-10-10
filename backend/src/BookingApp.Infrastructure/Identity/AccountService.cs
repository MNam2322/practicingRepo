using System.Security.Claims;
using BookingApp.Core.Accounts;
using BookingApp.Core.Bookings;
using BookingApp.Core.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace BookingApp.Infrastructure.Identity;

/// <summary>The outcome of signing up or in: the account, and how many anonymous bookings moved to it.</summary>
/// <param name="Principal">Who is now signed in, for anything this same request issues for them (an antiforgery token).</param>
public sealed record SignedIn(Account Account, int MovedBookings, ClaimsPrincipal Principal);

/// <summary>
/// Accounts: sign up, sign in, sign out and "who am I". Wraps ASP.NET Core Identity (UserManager, SignInManager), so
/// controllers never touch it directly. Signing in issues a new auth cookie (no session fixation) and moves the
/// caller's anonymous bookings to the account. Library: 10.2 "Credentials", "Staying signed in".
/// </summary>
public sealed class AccountService(UserManager<AppUser> users, SignInManager<AppUser> signIn, BookingService bookings,
    IOptions<AccountBootstrapOptions> bootstrap)
{
    /// <summary>Grants the roles configured for this email (Accounts:Bootstrap). Run before the auth cookie is issued.</summary>
    public async Task EnsureBootstrapRolesAsync(AppUser user)
    {
        foreach (var role in bootstrap.Value.RolesFor(user.Email))
            if (!await users.IsInRoleAsync(user, role)) await users.AddToRoleAsync(user, role);
    }

    /// <summary>Signs up and in. On a 400 the second value holds the field errors (password, email) for the response.</summary>
    public async Task<(Result<SignedIn> Result, IReadOnlyDictionary<string, string[]> FieldErrors)> SignUpAsync(
        string email, string password, string name, CustomerId? anonymous, CancellationToken cancellationToken)
    {
        var user = new AppUser { Id = Guid.CreateVersion7(), UserName = email.Trim(), Email = email.Trim(), DisplayName = name.Trim() };
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            if (created.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateUserName) or nameof(IdentityErrorDescriber.DuplicateEmail)))
                return (new Error(ErrorKind.Conflict, "email_taken", "There is already an account with that email. Sign in instead."), Empty);
            var fields = created.Errors
                .GroupBy(e => e.Code.StartsWith("Password", StringComparison.Ordinal) ? "password" : e.Code.Contains("Email", StringComparison.Ordinal) || e.Code.Contains("UserName", StringComparison.Ordinal) ? "email" : "")
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
            return (new Error(ErrorKind.RuleViolation, "validation_failed", "Some fields are missing or invalid."), fields);
        }
        await users.AddToRoleAsync(user, Roles.Customer);
        await EnsureBootstrapRolesAsync(user);
        await signIn.SignInAsync(user, isPersistent: true);
        return (Result<SignedIn>.Ok(await SignedInAsync(user, anonymous, cancellationToken)), Empty);
    }

    public async Task<Result<SignedIn>> SignInAsync(string email, string password, CustomerId? anonymous, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is null)
        {
            // Same answer and about the same time as a wrong password, so sign-in cannot be used to find out which
            // emails have accounts (library 10.1 "A login form that confirmed which emails exist").
            users.PasswordHasher.HashPassword(TimingDummy, password);
            return InvalidCredentials;
        }
        await EnsureBootstrapRolesAsync(user);
        var result = await signIn.PasswordSignInAsync(user, password, isPersistent: true, lockoutOnFailure: true);
        if (result.IsLockedOut)
            return new Error(ErrorKind.Unauthenticated, "account_locked", "Too many failed attempts. Try again in a few minutes.");
        if (!result.Succeeded) return InvalidCredentials;
        return Result<SignedIn>.Ok(await SignedInAsync(user, anonymous, cancellationToken));
    }

    public Task SignOutAsync() => signIn.SignOutAsync();

    /// <summary>The signed-in account, or null for an anonymous caller.</summary>
    public async Task<Account?> CurrentAsync(ClaimsPrincipal principal)
    {
        var user = await users.GetUserAsync(principal);
        return user is null ? null : await ToAccountAsync(user);
    }

    private async Task<SignedIn> SignedInAsync(AppUser user, CustomerId? anonymous, CancellationToken cancellationToken)
    {
        var account = await ToAccountAsync(user);
        var moved = anonymous is { } from ? await bookings.ClaimAsync(from, account.CustomerId, cancellationToken) : 0;
        return new SignedIn(account, moved, await signIn.CreateUserPrincipalAsync(user));
    }

    private async Task<Account> ToAccountAsync(AppUser user) =>
        new(user.Id, user.Email ?? "", user.DisplayName, [.. (await users.GetRolesAsync(user)).Order()]);

    private static readonly Error InvalidCredentials = new(ErrorKind.Unauthenticated, "invalid_credentials", "That email and password do not match an account.");
    private static readonly IReadOnlyDictionary<string, string[]> Empty = new Dictionary<string, string[]>();
    private static readonly AppUser TimingDummy = new();
}
