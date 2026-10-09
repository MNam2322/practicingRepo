using BookingApp.Core.Bookings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace BookingApp.Api;

/// <summary>
/// The customer making the request, from the "bk-customer" cookie; a new anonymous id is issued when there is
/// none. Controller actions take it as a parameter and <see cref="CurrentCustomerBinder"/> fills it in.
/// TASKS.md replaces this with real accounts (and keeps anonymous bookings on sign-in).
/// </summary>
[ModelBinder(BinderType = typeof(CurrentCustomerBinder))]
public sealed record CurrentCustomer(CustomerId Id)
{
    public const string CookieName = "bk-customer";

    public static CurrentCustomer FromRequest(HttpContext context)
    {
        if (CustomerId.TryParse(context.Request.Cookies[CookieName], out var existing)) return new CurrentCustomer(existing);

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
}

/// <summary>Binds a <see cref="CurrentCustomer"/> action parameter from the request cookie.</summary>
internal sealed class CurrentCustomerBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        bindingContext.Result = ModelBindingResult.Success(CurrentCustomer.FromRequest(bindingContext.HttpContext));
        bindingContext.ValidationState[bindingContext.Result.Model!] = new ValidationStateEntry { SuppressValidation = true };
        return Task.CompletedTask;
    }
}
