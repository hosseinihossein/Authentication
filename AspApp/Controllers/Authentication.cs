using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using AspApp.Helpers;
using AspApp.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using MongoDB.Bson;
using MongoDB.Driver;
using OpenIddict.Abstractions;
using OpenIddict.Client.AspNetCore;
using OpenIddict.Client.WebIntegration;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AspApp.Controllers;

[ApiController]
[Route("api/identity/[controller]/[action]")]
public class AuthenticationController : ControllerBase
{
    private readonly SignInManager<IdentityUser<Guid>> _signInManager;
    private readonly UserManager<IdentityUser<Guid>> _userManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly UsernameGenerator _usernameGenerator;
    private readonly IMongoCollection<BsonDocument> _userProfileCollection;
    private readonly KafkaEmailSender _emailSender;

    private readonly string[] _googleScopeEmail = [Scopes.Email];//[Scopes.OpenId, Scopes.Email, Scopes.Profile];
    //private readonly string[] _googleScopeProfile = [Scopes.Profile];//[Scopes.OpenId, Scopes.Email, Scopes.Profile];
    //private readonly string[] _googleScope = [Scopes.Email, Scopes.Profile];//[Scopes.OpenId, Scopes.Email, Scopes.Profile];
    private readonly string[] _gitHubScopes = ["read:user", "user:email"];


    public AuthenticationController(
        SignInManager<IdentityUser<Guid>> signInManager,
        UserManager<IdentityUser<Guid>> userManager,
        IHttpClientFactory httpClientFactory,
        UsernameGenerator usernameGenerator,
        IMongoDatabase mongoUserDb,
        IConfiguration configuration,
        KafkaEmailSender emailSender
    )
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _httpClientFactory = httpClientFactory;
        _usernameGenerator = usernameGenerator;
        _userProfileCollection = mongoUserDb.GetCollection<BsonDocument>(configuration["MongoDB:ProfileCollectionName"] ?? "Profile");
        _emailSender = emailSender;
    }



    [HttpGet]
    public IActionResult EnableTurnstile([FromServices] IConfiguration configuration)
    {
        return Ok(new { enableTurnstile = configuration.GetValue<bool>("TurnsTileEnable", false) });
    }



    [HttpPost]
    [ValidateTurnstile]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login([FromForm] Authentication_Login_FormModel formModel)
    //[FromServices] IConfiguration configuration, [FromServices] TurnstileService turnstileService)
    {
        if (ModelState.IsValid)
        {
            IdentityUser<Guid>? user;
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
    [ValidateTurnstile]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginWithGitHub([FromForm] Authentication_LoginWithProvider_FormModel formModel)
    {
        //Url.IsLocalUrl()
        var properties = new AuthenticationProperties
        {
            // Only allow local return URLs to prevent open redirect attacks.(I don't know about this)
            RedirectUri = formModel.ReturnUrl ?? "/",
        };
        properties.SetParameter("scope", _gitHubScopes);

        // Ask the OpenIddict client middleware to redirect the user agent to GitHub.
        return Challenge(properties, OpenIddictClientWebIntegrationConstants.Providers.GitHub);
    }

    [HttpPost]
    [ValidateTurnstile]
    [ValidateAntiForgeryToken]
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
                var usernameHint = result.Principal.GetClaim("login");
                if (usernameHint is not null)
                {
                    usernameHint = Regex.Replace(usernameHint, @"[^a-zA-Z0-9._]", "");
                }
                string username = _usernameGenerator.GetUniqueInMilliSeconds(usernameHint);

                user = new()
                {
                    UserName = username,
                    Email = userEmail,
                    EmailConfirmed = emailVerified,
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

                if (!user.EmailConfirmed)
                {
                    //send email validation link
                    await SendEmailValidationLink(user);
                }

                //create user profile document on mongodb
                var bsonDoc = new BsonDocument
                {
                    {"_id", new BsonBinaryData(user.Id, GuidRepresentation.Standard)},
                    { "remoteAvatarUrl", new BsonString(result.Principal.GetClaim("avatar_url")) },
                    { "createdAt", new BsonDateTime(DateTime.UtcNow) },
                };
                await _userProfileCollection.InsertOneAsync(bsonDoc);
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

        return Redirect(result.Properties.RedirectUri ?? "/api/identity/Authorization/Authorize");
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
        string loginProvider = "Google";
        string providerKey = result.Principal.GetClaim(ClaimTypes.NameIdentifier)!;
        UserLoginInfo userLoginInfo = new(loginProvider, providerKey, loginProvider);
        var user = await _userManager.FindByLoginAsync(loginProvider, providerKey);
        if (user is null)
        {
            var userByEmail = await _userManager.FindByEmailAsync(userEmail);
            if (userByEmail is null)
            {
                string username = _usernameGenerator.GetUniqueInMilliSeconds();
                user = new()
                {
                    UserName = username,
                    Email = userEmail,
                    // why gmail need to be verified
                    EmailConfirmed = bool.Parse(result.Principal.GetClaims("email_verified")[0].ToLower())
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

                if (!user.EmailConfirmed)
                {
                    //send email validation link
                    await SendEmailValidationLink(user);
                }

                //create user profile document on mongodb
                var bsonDoc = new BsonDocument
                {
                    {"_id", new BsonBinaryData(user.Id, GuidRepresentation.Standard)},
                    { "remoteAvatarUrl", new BsonString(result.Principal.GetClaim("picture")) },
                    { "createdAt", new BsonDateTime(DateTime.UtcNow) },
                };
                await _userProfileCollection.InsertOneAsync(bsonDoc);
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

        return Redirect(result.Properties.RedirectUri ?? "/api/identity/Authorization/Authorize");
    }



    private async Task SendEmailValidationLink(IdentityUser<Guid> user)
    {
        //***** Generate Email Validation Token *****
        string token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

        //***** Sending Email *****
        string emailMessage = $"<h4>Hi dear {user.UserName}</h4>" +
        "<p>Please click " +
        $"<a href='https://localhost:5443/Identity/ConfirmEmail?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(user.Email!)}' " +
        "target='_blank'>'Here'</a>" +
        " to confirm your email.</p>";

        EmailModel emailModel = new()
        {
            Message = emailMessage,
            ReceiverEmail = user.Email!,
            ReceiverName = user.UserName,
            Subject = "Email Validation"
        };

        _emailSender.Send(emailModel);
    }



}