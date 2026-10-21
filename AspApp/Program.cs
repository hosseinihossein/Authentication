using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AspApp.Helpers;
using AspApp.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using OpenIddict.Abstractions;
using OpenIddict.Client;
using OpenIddict.Client.WebIntegration;

namespace AspApp;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);


        //******************* Kestrel *******************
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Any, 80);
            options.Limits.MaxRequestBodySize = 16 * 1024;// 16 KB
        });


        //******************* Identity_DbContext *******************
        builder.Services.AddDbContext<Identity_DbContext>(options =>
        {
            options.UseNpgsql(builder.Configuration["Postgres:ConnectionString:Identity"]);
            options.UseOpenIddict<Guid>();
        });
        //******************* DataProtection_DbContext *******************
        builder.Services.AddDbContext<DataProtection_DbContext>(options =>
        {
            options.UseNpgsql(builder.Configuration["Postgres:ConnectionString:DataProtection"]);
        });


        //******************* Identity service *******************
        builder.Services.AddIdentity<IdentityUser<Guid>, IdentityRole<Guid>>(options =>
        {
            options.User.AllowedUserNameCharacters =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._";

            options.User.RequireUniqueEmail = true;

            options.SignIn.RequireConfirmedEmail = false;

            options.Password.RequireDigit = false;
            options.Password.RequiredLength = 8;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredUniqueChars = 1;
        })
        .AddEntityFrameworkStores<Identity_DbContext>()
        .AddDefaultTokenProviders();


        //******************* identity cookie *******************
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true; // prevent from js access
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // https only
            options.Cookie.SameSite = SameSiteMode.Lax;// protect unsafe methods from csrf
            options.Cookie.Path = "/api/identity/";

            //Default for "Remember Me". Without "Remember Me" → Session cookie (expires when browser closes)
            options.ExpireTimeSpan = TimeSpan.FromDays(14);//null or don't set for session lifetime
            options.SlidingExpiration = true;//Reset expiration when user interacts halfway through the lifespan

            options.LoginPath = "/Authentication";
            //options.AccessDeniedPath = "/Account/AccessDenied";
        });


        //******************* Data Protection *******************
        // Configure shared Data Protection keys in SQL DB
        builder.Services.AddDataProtection()
        .PersistKeysToDbContext<DataProtection_DbContext>() // Store keys in the same DB
        .SetApplicationName("SharedIdentityApp"); // Must match across all servers


        //******************* SecurityStampValidatorOptions *******************
        builder.Services.Configure<SecurityStampValidatorOptions>(options =>
        {
            options.ValidationInterval = TimeSpan.Zero;
        });


        /*/ OIDC Client encryption
        using var algorithm = RSA.Create(keySizeInBits: 4096);

        var subject = new X500DistinguishedName("CN=OIDC Client Encryption Certificate");
        var request = new CertificateRequest(subject, algorithm, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyEncipherment, critical: true));

        var clientEncryptionCert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
*/

        // **********    CertManager    **********
        CertManager certManager = new(builder.Environment);
        builder.Services.AddSingleton<CertManager>(certManager);


        //******************* OpenIddict *******************
        builder.Services.AddOpenIddict()
        .AddCore(options =>
        {
            options.UseEntityFrameworkCore()
            .UseDbContext<Identity_DbContext>()
            .ReplaceDefaultEntities<Guid>();
        })
        .AddClient(async options =>
        {
            // Note: this sample uses the code flow, but you can enable the other flows if necessary.
            options.AllowAuthorizationCodeFlow();

            // Register the signing and encryption credentials used to protect
            // sensitive data like the state tokens produced by OpenIddict.            
            /*options.AddEncryptionCertificate(clientEncryptionCert)
            .AddSigningCredentials(new SigningCredentials(
                new ECDsaSecurityKey(ECDsa.Create(ECCurve.NamedCurves.nistP256)),
                SecurityAlgorithms.EcdsaSha256
            ))*/
            options.AddEncryptionCertificate(await certManager.GetRsaKeyEncryptionCert())
            .AddSigningKey(new ECDsaSecurityKey(ECDsa.Create(ECCurve.NamedCurves.nistP256)));

            // Register the ASP.NET Core host and configure the ASP.NET Core-specific options.
            options.UseAspNetCore()
            .EnableRedirectionEndpointPassthrough()
            .EnableStatusCodePagesIntegration();

            // Register the System.Net.Http integration and use the identity of the current
            // assembly as a more specific user agent, which can be useful when dealing with
            // providers that use the user agent as a way to throttle requests (e.g Reddit).
            options.UseSystemNetHttp()
            .SetProductInformation(typeof(Program).Assembly);

            // Register the Web providers integrations.
            //
            // Note: to mitigate mix-up attacks, it's recommended to use a unique redirection endpoint
            // URI per provider, unless all the registered providers support returning a special "iss"
            // parameter containing their URL as part of authorization responses. For more information,
            // see https://datatracker.ietf.org/doc/html/draft-ietf-oauth-security-topics#section-4.4.
            options.UseWebProviders()
            .AddGitHub(opts =>
            {
                opts.SetClientId(builder.Configuration["GitHubClientId"]!)
                .SetClientSecret(builder.Configuration["GitHubClientSecret"]!)
                .SetRedirectUri("api/identity/Authentication/GitHubLoginCallback");
            })
            .AddGoogle(opts =>
            {
                opts.SetClientId(builder.Configuration["GoogleClientId"]!)
                .SetClientSecret(builder.Configuration["GoogleClientSecret"]!)
                .SetRedirectUri("api/identity/Authentication/GoogleLoginCallback");
            });

            /*options.AddEventHandler<OpenIddictClientEvents.PrepareConfigurationRequestContext>(b =>
            {
                b.UseInlineHandler(cntx =>
                {
                    cntx.Configuration.AuthorizationResponseIssParameterSupported = true;
                    return ValueTask.CompletedTask;
                });
            });*/

            options.RemoveEventHandler(OpenIddictClientHandlers.ValidateIssuerParameter.Descriptor);

            /*options.AddRegistration(new OpenIddictClientRegistration
            {
                ProviderName = "GitHub_Provider",
                Issuer = new Uri("https://github.com/login/oauth",UriKind.Absolute),
                ClientId = builder.Configuration["GitHubClientId"],
                ClientSecret = builder.Configuration["GitHubClientSecret"],
                RedirectUri = new Uri("Api/Authentication/GitHubLoginCallback", UriKind.Relative),

                Scopes = {OpenIddictConstants.Scopes.OpenId, "read:user", "user:email"},

                Configuration = new OpenIddictConfiguration
                {
                    AuthorizationResponseIssParameterSupported = true,
                    Issuer = new Uri("https://github.com/login/oauth",UriKind.Absolute),
                    AuthorizationEndpoint = new Uri("https://github.com/login/oauth/authorize",UriKind.Absolute),
                    TokenEndpoint = new Uri("https://github.com/login/oauth/access_token",UriKind.Absolute),
                    JsonWebKeySetUri = new Uri("https://github.com/login/oauth/.well-known/jwks",UriKind.Absolute),

                    ResponseTypesSupported = { OpenIddictConstants.ResponseTypes.Code},
                    ResponseModesSupported = {OpenIddictConstants.ResponseModes.Query},
                    GrantTypesSupported = {OpenIddictConstants.GrantTypes.AuthorizationCode},

                    TokenEndpointAuthMethodsSupported = 
                    {
                        //OpenIddictConstants.ClientAuthenticationMethods.ClientSecretBasic,
                        OpenIddictConstants.ClientAuthenticationMethods.ClientSecretPost,
                    }
                }
            });*/
        });



        //******************* ControllersWithViews *******************
        builder.Services.AddControllers();


        //******************* AntiForgery *******************
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            //options.Cookie.Name = "XSRF-TOKEN";//swap error
        });


        //**************************** Kafka Producer **************************
        KafkaProducerService kafkaService = new(builder.Configuration);
        builder.Services.AddSingleton<KafkaProducerService>(kafkaService);


        //**************************** Logger **************************
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(); // Keep Console for local visibility
        builder.Logging.AddKafka(kafkaService, builder.Configuration);


        //**************************** Email Sender **************************
        builder.Services.AddSingleton<KafkaEmailSender>();


        //******************* IHttpClientFactory *******************
        builder.Services.AddHttpClient();


        //**************************** Turnstile **************************
        builder.Services.AddSingleton<TurnstileService>();


        //******************* Username Generator *******************
        builder.Services.AddSingleton<UsernameGenerator>();


        // ************ MongoDb ***********
        // Register IMongoClient as a Singleton
        IMongoClient mongoClient = new MongoClient(builder.Configuration["MongoDb:ConnectionString"]);
        builder.Services.AddSingleton<IMongoClient>(mongoClient);
        // Register IMongoDatabase directly as a Singleton for quick access
        IMongoDatabase userDb = mongoClient.GetDatabase(builder.Configuration["MongoDb:UserDbName"]);
        builder.Services.AddSingleton<IMongoDatabase>(userDb);


        var app = builder.Build();


        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            KnownIPNetworks = { new System.Net.IPNetwork(IPAddress.Parse("172.18.0.0"), 16) }
        });


        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapDefaultControllerRoute();


        //**************************** Seed Data **************************
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var dataProtectionDb = scope.ServiceProvider.GetRequiredService<DataProtection_DbContext>();
            await dataProtectionDb.Database.MigrateAsync();

            var identityDb = scope.ServiceProvider.GetRequiredService<Identity_DbContext>();
            await identityDb.Database.MigrateAsync();

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
            //***** Create "admin" Identity *****
            IdentityUser<Guid>? admin = await userManager.FindByNameAsync("admin");
            if (admin == null)
            {
                string adminPassword = builder.Configuration["Identity:AdminPassword"]!;
                admin = new IdentityUser<Guid>
                {
                    UserName = "admin",
                    Email = "admin@HoLibz.com",
                    EmailConfirmed = true,
                };
                IdentityResult result = await userManager.CreateAsync(admin, adminPassword);

                if (!result.Succeeded)
                {
                    foreach (var error in result.Errors)
                    {
                        //Console.WriteLine(error.Description);
                        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                        logger.LogWarning(error.Description);
                    }
                    return;
                }
            }

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            //***** Seed Roles *****
            if (await roleManager.FindByNameAsync("Identity_Admins") == null)
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>("Identity_Admins"));
                await userManager.AddToRoleAsync(admin, "Identity_Admins");
            }

        }


        //******************* app.Run ******************
        Console.WriteLine($"\n*** App is running on all network port '80'");
        app.Run();
    }
}
