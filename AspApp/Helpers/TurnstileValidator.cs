using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AspApp.Helpers;

public class ValidateTurnstileAttribute : ActionFilterAttribute
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionArguments.TryGetValue("signupModel", out var arg))
        {
            if (arg is Identity_SignupFormModel signupModel)
            {
                // inject the turnstile service
                TurnstileService turnstileService = context.HttpContext.RequestServices.GetRequiredService<TurnstileService>();

                TurnstileResponse? turnstileResponse =
                await turnstileService.ValidateTokenAsync(context.HttpContext, signupModel.CfTurnstileResponse);

                if (turnstileResponse is null || turnstileResponse.Success is false)
                {
                    context.ModelState.AddModelError("TurnstileError",
                    turnstileResponse is null ? "response null!" : string.Join(", ", turnstileResponse.ErrorCodes));
                    context.Result = new BadRequestObjectResult(context.ModelState);
                    return;
                    //return BadRequest(ModelState);
                }

                // execute action
                await next();
            }

            context.ModelState.AddModelError("TurnstileError", "signupModel is not of type Identity_SignupFormModel");
            context.Result = new BadRequestObjectResult(context.ModelState);
            return;
        }

        context.ModelState.AddModelError("TurnstileError", "Can not get the value of the signupModel");
        context.Result = new BadRequestObjectResult(context.ModelState);
        return;
    }
}