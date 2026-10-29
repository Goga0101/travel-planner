using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TravelPlanner.API.Infrastructure;
using TravelPlanner.Application;
using TravelPlanner.Infrastructure;
using TravelPlanner.Infrastructure.Catalog;
using TravelPlanner.Infrastructure.Persistence;
using TravelPlanner.Infrastructure.Providers;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });
builder.Services.AddProblemDetails();
builder.Services.AddRequestLocalization(o =>
{
    // English default, Georgian supported. Chosen by ?culture=ka or the Accept-Language header.
    string[] cultures = [TravelPlanner.Application.Common.T.English, TravelPlanner.Application.Common.T.Georgian];
    o.SetDefaultCulture(cultures[0]).AddSupportedCultures(cultures).AddSupportedUICultures(cultures);
    o.ApplyCurrentCultureToResponseHeaders = true;
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new() { Title = "Travel Planner API", Version = "v1", Description = "Trip search and planning. Booking happens on the providers' official websites." });
    var xml = Path.Combine(AppContext.BaseDirectory, "TravelPlanner.API.xml");
    if (File.Exists(xml)) o.IncludeXmlComments(xml);
});

builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

// The API's own sign-in token (issued after Google / Facebook sign-in), sent by the web client as "Authorization: Bearer".
var jwt = builder.Configuration.GetSection(TravelPlanner.Infrastructure.Auth.AuthOptions.SectionName).Get<TravelPlanner.Infrastructure.Auth.AuthOptions>()?.Jwt
          ?? new TravelPlanner.Infrastructure.Auth.AuthOptions.JwtSettings();
builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = TravelPlanner.Infrastructure.Auth.JwtAccessTokenIssuer.ValidationParameters(jwt);
    });
builder.Services.AddAuthorization();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddPolicy(ApiConstants.CorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .WithMethods("GET", "POST")
    .WithHeaders("Content-Type", "Accept", "Accept-Language", "Authorization")
    .WithExposedHeaders("Location", "Retry-After", "Content-Language")));

var rateLimits = builder.Configuration.GetSection("RateLimiting").Get<RateLimitSettings>() ?? new RateLimitSettings();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = async (context, ct) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
        await Results.Problem(statusCode: 429, title: TravelPlanner.Application.Common.T.Get("api.tooManyTitle"),
                detail: TravelPlanner.Application.Common.T.Get("api.tooMany"))
            .ExecuteAsync(context.HttpContext);
    };
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(
        ClientKey(ctx), _ => new FixedWindowRateLimiterOptions { PermitLimit = rateLimits.GlobalPerMinute, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy(ApiConstants.SearchCreatePolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ClientKey(ctx), _ => new FixedWindowRateLimiterOptions { PermitLimit = rateLimits.SearchesPerMinute, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy(ApiConstants.SignInPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ClientKey(ctx), _ => new FixedWindowRateLimiterOptions { PermitLimit = rateLimits.SignInsPerMinute, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy(ApiConstants.RefreshPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ClientKey(ctx), _ => new FixedWindowRateLimiterOptions { PermitLimit = rateLimits.RefreshesPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

await InitializeDatabaseAsync(app);

app.UseRequestLocalization();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    await next();
});
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/swagger/v1/swagger.json", "Travel Planner API v1"));
}

if (app.Configuration.GetValue<bool>("UseHttpsRedirection")) app.UseHttpsRedirection();
// A public page (e.g. GitHub Pages) calling an API on localhost triggers the browser's private/local-network preflight.
// Answer it only for allowed origins; regular CORS rules still apply.
app.Use(async (context, next) =>
{
    if (HttpMethods.IsOptions(context.Request.Method) &&
        context.Request.Headers.TryGetValue("Access-Control-Request-Private-Network", out var pna) && pna == "true" &&
        allowedOrigins.Contains(context.Request.Headers.Origin.ToString(), StringComparer.OrdinalIgnoreCase))
    {
        context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
    }
    await next();
});
app.UseCors(ApiConstants.CorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

var status = app.Services.GetRequiredService<ProviderStatusRegistry>();
foreach (var s in status.Statuses)
    app.Logger.LogInformation("Provider [{Category}] {Name}: {State} – {Note}", s.Category, s.Name, s.Active ? "active" : "inactive",
        TravelPlanner.Application.Common.T.Get(s.NoteKey));

app.Run();

static string ClientKey(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

static async Task InitializeDatabaseAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var config = app.Configuration;
    if (config.GetValue("Database:MigrateOnStartup", false))
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }
    if (config.GetValue("Catalog:SeedOnStartup", true))
    {
        await scope.ServiceProvider.GetRequiredService<CatalogSeeder>().SeedAsync(CancellationToken.None);
    }
}

public partial class Program;
