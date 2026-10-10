using System.Security.Claims;
using BookingApp.Core.Bookings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace BookingApp.Api;

/// <summary>
/// The customer making the request. Signed in: their account id. Otherwise an anonymous id from the "bk-customer"
/// cookie, issued on first use, so people can book without an account; signing in moves those bookings to the
/// account. Controller actions take it as a parameter and <see cref="CurrentCustomerBinder"/> fills it in.
/// </summary>
[CurrentCustomerBinder.FromCookie]
public sealed record CurrentCustomer(CustomerId Id)
{
    public const string CookieName = "bk-customer";

    public static CurrentCustomer FromRequest(HttpContext context)
    {
        if (AccountId(context) is { } account) return new CurrentCustomer(account);
        if (AnonymousId(context) is { } existing) return new CurrentCustomer(existing);

        var id = CustomerId.New();
        context.Response.Cookies.Append(CookieName, id.ToString(), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            IsEssential = true,
            MaxAge = TimeSpan.FromDays(365),
        });
        return new CurrentCustomer(id);
    }

    /// <summary>The signed-in account's id, or null when the caller is anonymous.</summary>
    public static CustomerId? AccountId(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true
        && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? new CustomerId(id) : null;

    /// <summary>The anonymous id from the cookie, without issuing one.</summary>
    public static CustomerId? AnonymousId(HttpContext context) =>
        CustomerId.TryParse(context.Request.Cookies[CookieName], out var id) ? id : null;

    /// <summary>After signing in, the anonymous id has done its job: its bookings now belong to the account.</summary>
    public static void ForgetAnonymous(HttpContext context) => context.Response.Cookies.Delete(CookieName);
}

/// <summary>Binds a <see cref="CurrentCustomer"/> action parameter from the request cookie.</summary>
public sealed class CurrentCustomerBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        bindingContext.Result = ModelBindingResult.Success(CurrentCustomer.FromRequest(bindingContext.HttpContext));
        bindingContext.ValidationState[bindingContext.Result.Model!] = new ValidationStateEntry { SuppressValidation = true };
        return Task.CompletedTask;
    }

    /// <summary>
    /// Marks the parameter as bound by this binder from a non-request source, so it is not taken from the body and
    /// does not appear as a parameter in the OpenAPI document.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Parameter)]
    public sealed class FromCookieAttribute : ModelBinderAttribute
    {
        public FromCookieAttribute()
        {
            BinderType = typeof(CurrentCustomerBinder);
            BindingSource = BindingSource.Special;
        }
    }
}
