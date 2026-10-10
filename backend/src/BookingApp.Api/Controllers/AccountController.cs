using BookingApp.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BookingApp.Api.Controllers;

/// <summary>Accounts: sign up, sign in, sign out, and who is signed in. The session is the HttpOnly "bk-auth" cookie.</summary>
[Route("api")]
public sealed class AccountController(AccountService accounts) : ApiControllerBase
{
    /// <summary>Rate limit for sign-up and sign-in, per IP (Program.cs).</summary>
    public const string SignInPolicy = "account-sign-in";

    /// <summary>Creates an account and signs in. Bookings made on this browser before move to the new account.</summary>
    [HttpPost("account/signup")]
    [AllowAnonymous]
    [EnableRateLimiting(SignInPolicy)]
    [EndpointName("SignUp")]
    [Consumes(Json)]
    [ProducesResponseType<SignedInResponse>(StatusCodes.Status201Created, Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson, Description = "validation_failed (errors.email, errors.password, errors.name)")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson, Description = "email_taken")]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<SignedInResponse>> SignUp(SignUpRequest request, CancellationToken ct)
    {
        var (result, fields) = await accounts.SignUpAsync(request.Email!, request.Password!, request.Name!, CurrentCustomer.AnonymousId(HttpContext), ct);
        if (fields.Count > 0)
        {
            foreach (var (field, messages) in fields)
                foreach (var message in messages) ModelState.AddModelError(field, message);
            return BadRequestHandler.InvalidModelState(ControllerContext) is ObjectResult invalid ? invalid : BadRequest();
        }
        if (!result.IsSuccess) return Failure(result.Error!);
        CurrentCustomer.ForgetAnonymous(HttpContext);
        BookingSecurity.IssueAntiforgeryToken(HttpContext, result.Value!.Principal);   // tokens belong to who is signed in
        return Created("/api/me", ToResponse(result.Value!));
    }

    /// <summary>Signs in. Bookings made on this browser while signed out move to the account.</summary>
    [HttpPost("account/signin")]
    [AllowAnonymous]
    [EnableRateLimiting(SignInPolicy)]
    [EndpointName("SignIn")]
    [Consumes(Json)]
    [ProducesResponseType<SignedInResponse>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson, Description = "validation_failed")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson, Description = "invalid_credentials, account_locked")]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<SignedInResponse>> SignIn(SignInRequest request, CancellationToken ct)
    {
        var result = await accounts.SignInAsync(request.Email!, request.Password!, CurrentCustomer.AnonymousId(HttpContext), ct);
        if (!result.IsSuccess) return Failure(result.Error!);
        CurrentCustomer.ForgetAnonymous(HttpContext);
        BookingSecurity.IssueAntiforgeryToken(HttpContext, result.Value!.Principal);
        return ToResponse(result.Value!);
    }

    /// <summary>Signs out: the auth cookie is removed and this browser is anonymous again.</summary>
    [HttpPost("account/signout")]
    [AllowAnonymous]
    [EndpointName("SignOut")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SignOutOfAccount()
    {
        await accounts.SignOutAsync();
        BookingSecurity.IssueAntiforgeryToken(HttpContext, new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity()));
        return NoContent();
    }

    /// <summary>The signed-in account. 401 unauthenticated when nobody is signed in.</summary>
    [HttpGet("me")]
    [EndpointName("GetMe")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson, Description = "unauthenticated")]
    public async Task<ActionResult<AccountResponse>> Me()
    {
        var account = await accounts.CurrentAsync(User);
        return account is null
            ? Failure(new Core.Common.Error(Core.Common.ErrorKind.Unauthenticated, "unauthenticated", "Sign in to do that."))
            : AccountResponse.From(account);
    }

    private static SignedInResponse ToResponse(SignedIn signedIn) => new(AccountResponse.From(signedIn.Account), signedIn.MovedBookings);
}
