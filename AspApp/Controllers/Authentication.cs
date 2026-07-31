using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using AspApp.Helpers;
using AspApp.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Client.AspNetCore;
using OpenIddict.Client.WebIntegration;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AspApp.Controllers;

[ApiController]
[Route("[controller]/Api/[action]")]
public class AuthenticationController : ControllerBase
{
    private readonly SignInManager<Identity_UserDbModel> _signInManager;
    private readonly UserManager<Identity_UserDbModel> _userManager;
    private readonly IHttpClientFactory _httpClientFactory;

    private readonly string[] _googleScopeEmail = [Scopes.Email];//[Scopes.OpenId, Scopes.Email, Scopes.Profile];
    //private readonly string[] _googleScopeProfile = [Scopes.Profile];//[Scopes.OpenId, Scopes.Email, Scopes.Profile];
    //private readonly string[] _googleScope = [Scopes.Email, Scopes.Profile];//[Scopes.OpenId, Scopes.Email, Scopes.Profile];
    private readonly string[] _gitHubScopes = ["read:user", "user:email"];


    public AuthenticationController(
        SignInManager<Identity_UserDbModel> signInManager,
        UserManager<Identity_UserDbModel> userManager,
        IHttpClientFactory httpClientFactory
    )
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _httpClientFactory = httpClientFactory;
    }



    [HttpGet]
    public IActionResult EnableTurnstile([FromServices] IConfiguration configuration)
    {
        return Ok(new { enableTurnstile = configuration.GetValue<bool>("TurnsTileEnable", false) });
    }



    [HttpPost]
    public async Task<IActionResult> Login([FromForm] Authentication_Login_FormModel formModel,
    [FromServices] IConfiguration configuration, [FromServices] TurnstileService turnstileService)
    {
        if (ModelState.IsValid)
        {
            if (configuration.GetValue<bool>("TurnsTileEnable", false))
            {
                if (formModel.CfTurnstileResponse == null)
                {
                    ModelState.AddModelError("Turnstile", "CfTurnstileResponse can not be null!");
                    return BadRequest(ModelState);
                }
                var remoteip = HttpContext.Request.Headers["CF-Connecting-IP"].FirstOrDefault() ??
                    HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault() ??
                    HttpContext.Connection.RemoteIpAddress?.ToString();

                TurnstileResponse? turnstileResponse =
                await turnstileService.ValidateTokenAsync(formModel.CfTurnstileResponse, remoteip);

                if (turnstileResponse is null || turnstileResponse.Success is false)
                {
                    ModelState.AddModelError("Turnstile",
                    turnstileResponse is null ? "response null!" : string.Join(", ", turnstileResponse.ErrorCodes));
                    return BadRequest(ModelState);
                }
            }

            Identity_UserDbModel? user;
            if (formModel.UsernameOrEmail.Contains('@'))
            {
                user = await _userManager.FindByEmailAsync(formModel.UsernameOrEmail);
            }
            else
            {
                user = await _userManager.FindByNameAsync(formModel.UsernameOrEmail);
            }

            if (user is null)
            {
                ModelState.AddModelError("Username", "Couldn't find any user with the specified Username or Email");
                return BadRequest(ModelState);
            }

            Microsoft.AspNetCore.Identity.SignInResult result =
            await _signInManager.PasswordSignInAsync(user, formModel.Password, true, false);

            if (result.Succeeded)
            {
                return Redirect(formModel.ReturnUrl ?? "/Authorization/Authorize");
            }
            else
            {
                ModelState.AddModelError("Password", "Invalid credentials");
            }
        }

        return BadRequest(ModelState);
    }

    [HttpPost]
    public async Task<IActionResult> LoginWithGitHub([FromForm] Authentication_LoginWithProvider_FormModel formModel)
    {
        //Url.IsLocalUrl()
        var properties = new AuthenticationProperties
        {
            // Only allow local return URLs to prevent open redirect attacks.(I don't know about this)
            RedirectUri = formModel.ReturnUrl ?? "/"
        };
        properties.SetParameter("scope", _gitHubScopes);

        // Ask the OpenIddict client middleware to redirect the user agent to GitHub.
        return Challenge(properties, OpenIddictClientWebIntegrationConstants.Providers.GitHub);
    }

    [HttpPost]
    public async Task<IActionResult> LoginWithGoogle([FromForm] Authentication_LoginWithProvider_FormModel formModel)
    {
        var properties = new AuthenticationProperties
        {
            // Only allow local return URLs to prevent open redirect attacks.(I don't know about this)
            RedirectUri = formModel.ReturnUrl ?? "/"
        };
        properties.SetParameter("scope", _googleScopeEmail);

        // Ask the OpenIddict client middleware to redirect the user agent to Google.
        return Challenge(properties, OpenIddictClientWebIntegrationConstants.Providers.Google);
    }



    // ************** callbacks ************
    [HttpGet, HttpPost, IgnoreAntiforgeryToken]
    public async Task<ActionResult> GitHubLoginCallback()
    {
        var result = await HttpContext.AuthenticateAsync(OpenIddictClientAspNetCoreDefaults.AuthenticationScheme);

        if (result is not { Succeeded: true, Principal.Identity.IsAuthenticated: true })
        {
            throw new InvalidOperationException("The external authorization data cannot be used for authentication.");
        }
        if (result.Principal.GetClaim(ClaimTypes.NameIdentifier) is null)
        {
            throw new Exception("NameIdentifier can NOT be null!");
        }

        var accessToken = result.Properties.GetTokenValue("access_token");
        //Console.WriteLine($"accessToken: {accessToken}");

        var backchannelAccessToken = result.Properties.GetTokenValue(
            OpenIddictClientAspNetCoreConstants.Tokens.BackchannelAccessToken
        );
        //Console.WriteLine($"backChannelAccessToken: {backchannelAccessToken}");

        var chosenAccessToken = string.IsNullOrWhiteSpace(accessToken) ?
        backchannelAccessToken : accessToken;

        using var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", chosenAccessToken);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MyAspNetApp");

        string res = await httpClient.GetStringAsync("https://api.github.com/user/emails");
        //Console.WriteLine($"\n***** res = {res}");
        var jsonDoc = JsonDocument.Parse(res);

        string? userEmail = null;
        bool emailVerified = false;
        //string? emailVisibility = "private";
        foreach (var emailEntry in jsonDoc.RootElement.EnumerateArray())
        {
            if (emailEntry.GetProperty("primary").GetBoolean())
            {
                userEmail = emailEntry.GetProperty("email").GetString();
                emailVerified = emailEntry.GetProperty("verified").GetBoolean();
                //emailVisibility = emailEntry.GetProperty("visibility").GetString();
                break;
            }
        }

        if (userEmail is null)
        {
            ModelState.AddModelError("Email", "Couldn't get the user's email from github!");
            return BadRequest(ModelState);
        }

        string loginProvider = "GitHub";
        string providerKey = result.Principal.GetClaim(ClaimTypes.NameIdentifier)!;
        UserLoginInfo userLoginInfo = new(loginProvider, providerKey, loginProvider);
        var user = await _userManager.FindByLoginAsync(loginProvider, providerKey);
        if (user is null)
        {
            var userByEmail = await _userManager.FindByEmailAsync(userEmail!);
            if (userByEmail is null)
            {
                //throw new Exception("my exception");
                var username = result.Principal.GetClaim("login")
                + "_" + Guid.NewGuid().ToString("N");
                username = Regex.Replace(username, @"[^a-zA-Z0-9._]", "");
                //MyRegex().Replace();Doesn't work, I don't know why
                //Console.WriteLine($"username: {username}");
                user = new()
                {
                    UserName = username,
                    Email = userEmail,
                    EmailConfirmed = emailVerified,
                    DisplayName = result.Principal.GetClaim("name"),
                    RemoteImageUrl = result.Principal.GetClaim("avatar_url"),
                };
                var userCreationResult = await _userManager.CreateAsync(user);
                if (!userCreationResult.Succeeded)
                {
                    foreach (var error in userCreationResult.Errors)
                    {
                        ModelState.AddModelError("User", error.Description);
                    }
                    return BadRequest(ModelState);
                }
            }
            else
            {
                user = userByEmail;
            }
            var addLoginResult = await _userManager.AddLoginAsync(user, userLoginInfo);
            //Console.WriteLine($"***** addLoginResult: {addLoginResult}");
        }

        var exSigninResult = await _signInManager.ExternalLoginSignInAsync(loginProvider, providerKey, true);
        //Console.WriteLine($"***** exSigninResult: {exSigninResult}");

        return LocalRedirect(result.Properties.RedirectUri ?? "/");
    }


    [HttpGet, HttpPost, IgnoreAntiforgeryToken]
    public async Task<ActionResult> GoogleLoginCallback()
    {
        var result = await HttpContext.AuthenticateAsync(OpenIddictClientAspNetCoreDefaults.AuthenticationScheme);

        if (result is not { Succeeded: true, Principal.Identity.IsAuthenticated: true })
        {
            ModelState.AddModelError("GitHubLogin", "The external authorization data cannot be used for authentication.");
            return BadRequest(ModelState);
            //throw new InvalidOperationException("The external authorization data cannot be used for authentication.");
        }
        if (result.Principal.GetClaim(ClaimTypes.NameIdentifier) is null)
        {
            ModelState.AddModelError("NameIdentifier", "NameIdentifier can NOT be null!");
            return BadRequest(ModelState);
            //throw new Exception("NameIdentifier can NOT be null!");
        }
        if (result.Principal.GetClaim(ClaimTypes.Email) is null)
        {
            ModelState.AddModelError("Email", "Email can NOT be null!");
            return BadRequest(ModelState);
            //throw new Exception("Email can NOT be null!");
        }

        string userEmail = result.Principal.GetClaim(ClaimTypes.Email)!;
        string userDisplayName = userEmail.Substring(0, userEmail.IndexOf('@'));
        string loginProvider = "Google";
        string providerKey = result.Principal.GetClaim(ClaimTypes.NameIdentifier)!;
        UserLoginInfo userLoginInfo = new(loginProvider, providerKey, loginProvider);
        var user = await _userManager.FindByLoginAsync(loginProvider, providerKey);
        if (user is null)
        {
            var userByEmail = await _userManager.FindByEmailAsync(userEmail);
            if (userByEmail is null)
            {
                //throw new Exception("my exception");
                string username = userDisplayName + "_" + Guid.NewGuid().ToString("N");
                username = Regex.Replace(username, @"[^a-zA-Z0-9._]", "");
                //MyRegex().Replace();
                //Console.WriteLine($"username: {username}");
                user = new()
                {
                    UserName = username,
                    Email = userEmail,
                    EmailConfirmed = bool.Parse(result.Principal.GetClaims("email_verified")[0].ToLower()),
                    RemoteImageUrl = result.Principal.GetClaim("picture"),
                    DisplayName = userDisplayName,
                };
                var userCreationResult = await _userManager.CreateAsync(user);
                if (!userCreationResult.Succeeded)
                {
                    foreach (var error in userCreationResult.Errors)
                    {
                        ModelState.AddModelError("User", error.Description);
                    }
                    return BadRequest(ModelState);
                }
            }
            else
            {
                user = userByEmail;
            }
            var addLoginResult = await _userManager.AddLoginAsync(user, userLoginInfo);
            //Console.WriteLine($"addLoginResult: {addLoginResult}");
        }

        var exSigninResult = await _signInManager.ExternalLoginSignInAsync(loginProvider, providerKey, true);
        //Console.WriteLine($"exSigninResult: {JsonSerializer.Serialize(exSigninResult)}");

        return LocalRedirect(result.Properties.RedirectUri ?? "/");
    }



}