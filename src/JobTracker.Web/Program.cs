using JobTracker.Web.Data;
using JobTracker.Web.Identity;
using JobTracker.Web.Applications;
using JobTracker.Web.Extraction;
using JobTracker.Web.Demo;
using JobTracker.Web.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
if (builder.Environment.IsProduction())
{
    builder.Logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        options.UseUtcTimestamp = true;
    });
}
else
{
    builder.Logging.AddSimpleConsole(options =>
    {
        options.IncludeScopes = true;
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    });
}

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "JobTracker.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        || builder.Environment.IsEnvironment("Testing")
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? (builder.Environment.IsEnvironment("Testing")
        ? "Host=localhost;Database=jobtracker_tests;Username=unused;Password=unused"
        : throw new InvalidOperationException(
            "ConnectionStrings:DefaultConnection is not configured. Use .NET user secrets locally."));

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedEmail = true;
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "JobTracker.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        || builder.Environment.IsEnvironment("Testing")
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
    options.LoginPath = "/account/login";
    options.AccessDeniedPath = "/account/access-denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("account", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Environment.IsEnvironment("Testing") ? 100 : 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    options.AddPolicy("imports", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Environment.IsEnvironment("Testing") ? 100 : 10,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
});

builder.Services.AddSingleton<DevelopmentMailStore>();
builder.Services.AddSingleton<IAccountEmailSender, DevelopmentAccountEmailSender>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DemoCatalog>();
builder.Services.AddScoped<DevelopmentAccountBootstrapper>();
builder.Services.AddScoped<InvitationService>();
builder.Services.AddScoped<InvitationRegistrationService>();
builder.Services.AddScoped<InvitationCommandRunner>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();
builder.Services.AddScoped<OwnedApplicationService>();
builder.Services.AddScoped<ApplicationTrackerService>();
builder.Services.AddScoped<ApplicationWorkflowService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<RetentionOperationsService>();
builder.Services.AddScoped<CompanyTrackerService>();
builder.Services.AddSingleton<PastedJobTextExtractor>();
builder.Services.AddSingleton<JobPostingHtmlExtractor>();
builder.Services.AddSingleton(JobPostingFetchOptions.Default);
builder.Services.AddSingleton<IHostAddressResolver, SystemHostAddressResolver>();
builder.Services.AddSingleton<PublicUrlSafetyPolicy>();
builder.Services.AddSingleton<IPublicAddressConnector, SystemPublicAddressConnector>();
builder.Services.AddSingleton(SafeHttpConnectionOptions.Default);
builder.Services.AddSingleton<SafeHttpConnectionFactory>();
builder.Services
    .AddHttpClient<IJobPostingFetcher, SafeJobPostingFetcher>(client =>
    {
        client.Timeout = Timeout.InfiniteTimeSpan;
    })
    .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
    {
        var connections = serviceProvider.GetRequiredService<SafeHttpConnectionFactory>();
        return new SocketsHttpHandler
        {
            ActivityHeadersPropagator = null,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip
                | DecompressionMethods.Deflate
                | DecompressionMethods.Brotli,
            ConnectCallback = connections.ConnectAsync,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            MaxConnectionsPerServer = 4,
            MaxResponseHeadersLength = 32,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            UseCookies = false,
            UseProxy = false,
        };
    });
builder.Services.AddScoped<ExtractionDraftService>();
builder.Services.AddScoped<JobPostingUrlImportService>();
builder.Services.AddScoped<BrowserExtensionImportService>();
builder.Services.AddHealthChecks()
    .AddCheck(
        "self",
        () => HealthCheckResult.Healthy(),
        tags: ["live"])
    .AddCheck<DatabaseReadinessHealthCheck>(
        "database",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"]);

var app = builder.Build();

await using (var commandScope = app.Services.CreateAsyncScope())
{
    var commandExitCode = await commandScope.ServiceProvider
        .GetRequiredService<InvitationCommandRunner>()
        .TryRunAsync(args);

    if (commandExitCode is not null)
    {
        Environment.ExitCode = commandExitCode.Value;
        return;
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Home/HandleStatusCode", "?code={0}");

app.UseMiddleware<PrivacySafeRequestLoggingMiddleware>();

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.XFrameOptions = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers.ContentSecurityPolicy =
            "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; " +
            "font-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
        context.Response.Headers["Permissions-Policy"] =
            "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
        context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
        context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
        context.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";

        if (context.User.Identity?.IsAuthenticated == true)
        {
            context.Response.Headers.CacheControl = "no-store, max-age=0";
            context.Response.Headers.Pragma = "no-cache";
        }

        return Task.CompletedTask;
    });

    await next();
});

app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = HealthResponseWriter.WriteAsync,
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync,
});
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync,
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

if (app.Environment.IsDevelopment())
{
    await using var bootstrapScope = app.Services.CreateAsyncScope();
    await bootstrapScope.ServiceProvider
        .GetRequiredService<DevelopmentAccountBootstrapper>()
        .InitializeAsync();
}

app.Run();

public partial class Program;
