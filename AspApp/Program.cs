using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AspApp.Helpers;
using AspApp.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace AspApp;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);


        //******************* Certificates *******************
        string certsDirPath = Path.Combine(builder.Environment.ContentRootPath, "Certs");
        Directory.CreateDirectory(certsDirPath);

        // OIDC Client signing
        string clientSigningCertPath = Path.Combine(certsDirPath, "oidc-client-signing-certificate.pfx");
        X509Certificate2 clientSigningCert;
        if (File.Exists(clientSigningCertPath))
        {
            clientSigningCert = X509CertificateLoader.LoadPkcs12FromFile(clientSigningCertPath, string.Empty);
        }
        else
        {
            using var algorithm = RSA.Create(keySizeInBits: 4096);

            var subject = new X500DistinguishedName("CN=OIDC Client Signing Certificate");
            var request = new CertificateRequest(subject, algorithm, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));

            clientSigningCert = request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(2));

            await File.WriteAllBytesAsync(clientSigningCertPath, clientSigningCert.Export(X509ContentType.Pfx, string.Empty));
        }

        // OIDC Client encryption
        string clientEncryptionCertPath = Path.Combine(certsDirPath, "oidc-client-encryption-certificate.pfx");
        X509Certificate2 clientEncryptionCert;
        if (File.Exists(clientEncryptionCertPath))
        {
            clientEncryptionCert = X509CertificateLoader.LoadPkcs12FromFile(clientEncryptionCertPath, string.Empty);
        }
        else
        {
            using var algorithm = RSA.Create(keySizeInBits: 4096);

            var subject = new X500DistinguishedName("CN=OIDC Client Encryption Certificate");
            var request = new CertificateRequest(subject, algorithm, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyEncipherment, critical: true));

            clientEncryptionCert = request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(2));

            await File.WriteAllBytesAsync(clientEncryptionCertPath, clientEncryptionCert.Export(X509ContentType.Pfx, string.Empty));
        }



        //******************* Kestrel *******************
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Any, builder.Configuration.GetValue<int>("TcpPort"), listenOptions =>
            {
                string pemFilePath = Path.Combine(certsDirPath, "HoLibz.com.pem");
                string keyFilePath = Path.Combine(certsDirPath, "HoLibz.com.key");
                if (System.IO.File.Exists(pemFilePath) && System.IO.File.Exists(keyFilePath))
                {
                    X509Certificate2 x509Certificate2 = X509Certificate2.CreateFromPemFile(pemFilePath, keyFilePath);
                    listenOptions.UseHttps(x509Certificate2);
                }
                else
                {
                    listenOptions.UseHttps();
                }
            });

            options.Limits.MaxRequestBodySize = 16 * 1024;// 16 KB
        });



        //******************* Identity_DbContext *******************
        builder.Services.AddDbContext<Identity_DbContext>(options =>
        {
            options.UseNpgsql(builder.Configuration["ConnectionStrings_Postgres:IdentityConnection"]);
        });



        //******************* Identity service *******************
        builder.Services.AddIdentity<Identity_UserDbModel, Identity_RoleDbModel>(options =>
        {
            options.User.AllowedUserNameCharacters =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._";

            options.User.RequireUniqueEmail = true;

            options.SignIn.RequireConfirmedEmail = false;

            options.Password.RequireDigit = false;
            options.Password.RequiredLength = 5;
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
            options.Cookie.SameSite = SameSiteMode.None;// no csrf protection to solve strict tracking prevention mechanisms

            //Default for "Remember Me". Without "Remember Me" → Session cookie (expires when browser closes)
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;

            //options.LoginPath = "/Account/Login";
            //options.AccessDeniedPath = "/Account/AccessDenied";
        });



        //******************* Data Protection *******************
        // Configure shared Data Protection keys in SQL DB
        builder.Services.AddDataProtection()
        .PersistKeysToDbContext<DataProtection_DbContext>() // Store keys in the same DB
        .SetApplicationName("SharedIdentityApp"); // Must match across all servers



        //******************* OpenIddict *******************
        builder.Services.AddOpenIddict()
        .AddClient(options =>
        {
            // Note: this sample uses the code flow, but you can enable the other flows if necessary.
            options.AllowAuthorizationCodeFlow();

            // Register the signing and encryption credentials used to protect
            // sensitive data like the state tokens produced by OpenIddict.
            options.AddEncryptionCertificate(clientEncryptionCert)
            .AddSigningCertificate(clientSigningCert);

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
            .AddGitHub(options =>
            {
                options.SetClientId(builder.Configuration["GitHubClientId"]!)
                .SetClientSecret(builder.Configuration["GitHubClientSecret"]!)
                .SetRedirectUri("Authentication/Api/GitHubLoginCallback");
            })
            .AddGoogle(options =>
            {
                options.SetClientId(builder.Configuration["GoogleClientId"]!)
                .SetClientSecret(builder.Configuration["GoogleClientSecret"]!)
                .SetRedirectUri("Authentication/Api/GoogleLoginCallback");
            });
        })
        .AddValidation(options =>
        {
            // Note: the validation handler uses OpenID Connect discovery
            // to retrieve the issuer signing keys used to validate tokens.
            options.SetIssuer(builder.Configuration["OidcIssuer"] ?? "https://localhost:5443/");

            // Register the encryption credentials. This sample uses a symmetric
            // encryption key that is shared between the server and the API project.
            //
            // Note: in a real world application, this encryption key should be
            // stored in a safe place (e.g in Azure KeyVault, stored as a secret).
            options.AddEncryptionKey(new SymmetricSecurityKey(
                Convert.FromBase64String("DRjd/GnduI3Efzen9V9BvbNUfc/VKgXltV7Kbk9sMkY="))
            );

            // Register the System.Net.Http integration.
            options.UseSystemNetHttp();

            // Register the ASP.NET Core host.
            options.UseAspNetCore();
        });



        //******************* SecurityStampValidatorOptions *******************
        builder.Services.Configure<SecurityStampValidatorOptions>(options =>
        {
            options.ValidationInterval = TimeSpan.Zero;
        });



        //******************* ControllersWithViews *******************
        builder.Services.AddControllersWithViews(options =>
        {
            options.Filters.Add(new RequireHttpsAttribute());
        });



        //******************* AntiForgery *******************
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            //options.Cookie.Name = "XSRF-TOKEN";//swap error
        });



        //******************* IHttpClientFactory *******************
        builder.Services.AddHttpClient();



        //**************************** Custom Services **************************
        builder.Services.AddSingleton<TurnstileService>();
        builder.Services.AddSingleton<FileExtensionContentTypeProvider>();



        var app = builder.Build();



        app.UseHttpsRedirection();
        app.UseStaticFiles(new StaticFileOptions { ServeUnknownFileTypes = true });
        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapDefaultControllerRoute();

        app.Map("/{*catchAll}", async (HttpContext context, FileExtensionContentTypeProvider provider) =>
        {
            string? catchAll = context.Request.RouteValues["catchAll"]?.ToString();
            if (!string.IsNullOrWhiteSpace(catchAll))
            {
                string staticFilePath =
                Path.Combine(app.Environment.WebRootPath, "AngularApp", "browser", catchAll);
                if (File.Exists(staticFilePath))
                {
                    //var provider = new FileExtensionContentTypeProvider();
                    if (provider.TryGetContentType(staticFilePath, out string? contentType))
                    {
                        context.Response.ContentType = contentType;
                        await context.Response.SendFileAsync(staticFilePath);
                        return;
                    }
                    else
                    {
                        context.Response.ContentType = "application/octet-stream";
                        await context.Response.SendFileAsync(staticFilePath);
                        return;
                    }
                }
            }

            context.Response.ContentType = "text/html";
            await context.Response.SendFileAsync(
                Path.Combine(app.Environment.WebRootPath, "AngularApp", "browser", "index.html")
            );
        });



        //**************************** Seed Data **************************
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var identityDb = scope.ServiceProvider.GetRequiredService<Identity_DbContext>();
            await identityDb.Database.MigrateAsync();

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<Identity_UserDbModel>>();
            //***** Create "admin" Identity *****
            Identity_UserDbModel? admin = await userManager.FindByNameAsync("admin");
            if (admin == null)
            {
                string adminPassword = builder.Configuration["Identity:AdminPassword"]!;
                admin = new Identity_UserDbModel
                {
                    UserName = "admin",
                    DisplayName = "Admin",
                    Description = "This user account belongs to the admin of the app.",
                    Email = "admin@HoLibz.com",
                    EmailConfirmed = true,
                };
                IdentityResult result = await userManager.CreateAsync(admin, adminPassword);

                if (!result.Succeeded)
                {
                    foreach (var error in result.Errors)
                    {
                        Console.WriteLine(error.Description);
                    }
                    return;
                }
            }

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Identity_RoleDbModel>>();
            //***** Seed Roles *****
            if (await roleManager.FindByNameAsync("Identity_Admins") == null)
            {
                await roleManager.CreateAsync(new Identity_RoleDbModel("Identity_Admins"));
                await userManager.AddToRoleAsync(admin, "Identity_Admins");
            }

        }



        //******************* app.Run ******************
        Console.WriteLine($"\n*** Authentication App is running on all network interfaces on port '{builder.Configuration["TcpPort"]}'");
        app.Run();
    }
}
