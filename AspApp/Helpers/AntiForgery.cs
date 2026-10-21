using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AspApp.Helpers;

public class GenerateAntiforgeryTokenCookieAttribute : ResultFilterAttribute
{
    public override void OnResultExecuting(ResultExecutingContext context)
    {
        var antiforgery = context.HttpContext.RequestServices.GetService<IAntiforgery>();

        // Send the request token as a JavaScript-readable cookie
        var tokens = antiforgery!.GetAndStoreTokens(context.HttpContext);

        context.HttpContext.Response.Cookies.Append(
            "XSRF-TOKEN",
            tokens.RequestToken!,
            new CookieOptions() { HttpOnly = false, Secure = true, SameSite = SameSiteMode.Strict });
    }

    public override void OnResultExecuted(ResultExecutedContext context)
    {
    }
}