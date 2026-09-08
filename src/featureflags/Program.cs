using FeatureFlags.Data;
using FeatureFlags.Components;
using FeatureFlags.Components.Models;
using FeatureFlags.Services;
using Microsoft.EntityFrameworkCore;
using FeatureFlags.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddRazorPages();

var connectionString = builder.Configuration.GetConnectionString(FeatureFlagDbContextFactory.ConnectionStringName)
    ?? FeatureFlagDbContextFactory.DefaultConnectionString;
var skipDatabaseMigrations = builder.Configuration.GetValue<bool>("SkipDatabaseMigrations");

builder.Services.AddDbContext<FeatureFlagDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddDbContextFactory<FeatureFlagDbContext>(options =>
    options.UseNpgsql(connectionString), ServiceLifetime.Scoped);
builder.Services.AddScoped<IFeatureFlagConfirmationService, FeatureFlagConfirmationService>();
builder.Services.AddScoped<IProjectPermissionService, ProjectPermissionService>();
builder.Services.AddScoped<IProjectProvisioningService, ProjectProvisioningService>();
builder.Services.AddScoped<ProjectChanges>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString), ServiceLifetime.Scoped);

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services
    .AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("api-key", httpContext =>
    {
        var apiKey = httpContext.Request.Headers["X-API-Key"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: "anonymous",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: apiKey,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue<int>("RateLimits:PermitLimit"), // Max Requests
                Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("RateLimits:WindowSeconds")), // Seconds
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = builder.Configuration.GetValue<int>("RateLimits:QueueLimit") // Max Queue of requests before being rejectedx
            });
    });
});

const string WebsiteCorsPolicy = "WebsiteCorsPolicy";

var allowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "https://logoas.xyz",
    "https://www.logoas.xyz",
    "https://lrfoster03.github.io"
};

builder.Services.AddCors(options =>
{
    options.AddPolicy(WebsiteCorsPolicy, policy =>
    {
        policy
            .SetIsOriginAllowed(origin =>
            {
                if (allowedOrigins.Contains(origin))
                    return true;

                return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                    && (uri.Host == "localhost" || uri.Host == "127.0.0.1")
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
            })
            .WithMethods("GET")
            .WithHeaders("user", "X-API-Key");
    });
});

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new()
        {
            Title = "Feature Flags API",
            Summary = "Public API for evaluating project feature flags and remote configs.",
            Description = "The Feature Flags API returns evaluated feature flag values and project configuration values for a client environment. It is designed to be consumed by websites, applications, and generated SDKs.",
            Version = "1.0.0"
        };

        document.Servers =
        [
            new() { Url = "https://featureflags.logoas.xyz", Description = "Production" },
            new() { Url = "http://localhost:8080", Description = "Local Docker default" }
        ];

        document.Components ??= new();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["ApiKeyAuth"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-API-Key",
            Description = "Client environment API key."
        };

        return Task.CompletedTask;
    });

    options.AddOperationTransformer((operation, _, _) =>
    {
        if (operation.OperationId != "getFeatureFlags")
            return Task.CompletedTask;

        operation.Summary = "Get evaluated feature flags";
        operation.Description = "Returns all feature flags and configs for the project environment associated with the provided client API key. Feature flag values are evaluated for the optional user identifier.";
        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("ApiKeyAuth", null, null)] = []
            }
        ];

        operation.Parameters ??= [];
        if (!operation.Parameters.Any(parameter => parameter.Name == "user" && parameter.In == ParameterLocation.Header))
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "user",
                In = ParameterLocation.Header,
                Required = false,
                Description = "Stable application user identifier used for percentage rollout evaluation. Send an empty value or omit this header for anonymous evaluation.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                Example = "user_123"
            });
        }

        if (operation.Responses is not null
            && operation.Responses.TryGetValue("200", out var successResponse)
            && successResponse.Content is not null
            && successResponse.Content.TryGetValue("application/json", out var jsonResponse))
        {
            jsonResponse.Examples ??= new Dictionary<string, IOpenApiExample>();
            jsonResponse.Examples["success"] = new OpenApiExample
            {
                Summary = "Feature flags response",
                Value = JsonNode.Parse("""
                {
                  "featureFlags": {
                    "NewUI": true,
                    "BetaCheckout": false
                  },
                  "configs": {
                    "Theme": {
                      "color": "blue",
                      "layout": "compact"
                    },
                    "Checkout": {
                      "maxItems": 10,
                      "allowCoupons": true
                    }
                  }
                }
                """)!
            };
        }

        if (operation.Responses is not null
            && operation.Responses.TryGetValue("401", out var unauthorizedResponse))
        {
            unauthorizedResponse.Description = "The X-API-Key header is missing, invalid, or revoked.";
        }

        return Task.CompletedTask;
    });

    options.AddSchemaTransformer((schema, context, _) =>
    {
        if (context.JsonTypeInfo.Type == typeof(FeatureFlagsResponse))
        {
            schema.Description = "Evaluated feature flags and dynamic configs.";

            if (schema.Properties is not null
                && schema.Properties.TryGetValue("featureFlags", out var featureFlagsSchema))
            {
                featureFlagsSchema.Description = "Map of feature flag names to evaluated boolean values.";
            }

            if (schema.Properties is not null
                && schema.Properties.TryGetValue("configs", out var configsSchema))
            {
                configsSchema.Description = "Map of config names to arbitrary JSON objects.";
            }
        }

        return Task.CompletedTask;
    });
});

var app = builder.Build();


app.MapOpenApi("/openapi/v1.json");

app.UseCors();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapRazorPages();

app.UseRateLimiter();

app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/", () => Results.Redirect("/projects"));

app.MapGet("/api/v1/featureflags", GetFeatureFlags)
    .WithName("getFeatureFlags")
    .WithTags("Feature Flags")
    .Produces<FeatureFlagsResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .RequireRateLimiting("api-key")
    .RequireCors(WebsiteCorsPolicy);

app.MapGet("/api/featureflags", GetFeatureFlags)
    .ExcludeFromDescription()
    .RequireRateLimiting("api-key")
    .RequireCors(WebsiteCorsPolicy);

static async Task<IResult> GetFeatureFlags(
    HttpContext http,
    FeatureFlagDbContext db)
{
    var clientKeyValue = http.Request.Headers["X-API-Key"].FirstOrDefault();

    if (string.IsNullOrWhiteSpace(clientKeyValue))
        return Results.Unauthorized();

    var clientKey = await db.ClientKeys
        .Include(k => k.ProjectEnvironment)
        .FirstOrDefaultAsync(k => k.Key == clientKeyValue && k.RevokedAt == null);

    if (clientKey is null)
        return Results.Unauthorized();

    var userId = http.Request.Headers["user"].FirstOrDefault() ?? string.Empty;

    var flags = await db.FeatureFlags
        .Where(f => f.ProjectEnvironmentId == clientKey.ProjectEnvironmentId)
        .OrderBy(f => f.Name)
        .ToListAsync();

    var response = Evaluator.Evaluate(flags, userId);

    response.Configs = await db.Configs
        .Where(c => c.ProjectEnvironmentId == clientKey.ProjectEnvironmentId)
        .OrderBy(c => c.Name)
        .ToDictionaryAsync(c => c.Name, c => c.Value);

    return Results.Ok(response);
}

if (!skipDatabaseMigrations)
{
    using (var scope = app.Services.CreateScope())
    {
        var flagDb = scope.ServiceProvider.GetRequiredService<FeatureFlagDbContext>();

        if (app.Environment.IsDevelopment() && flagDb.Database.HasPendingModelChanges())
        {
            throw new InvalidOperationException(
                "EF model changes detected without a migration. " +
                "Run: dotnet ef migrations add <Name> --project src/featureflags/FeatureFlags.csproj --startup-project src/featureflags/FeatureFlags.csproj");
        }

        flagDb.Database.Migrate();

        var appDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (app.Environment.IsDevelopment() && appDb.Database.HasPendingModelChanges())
        {
            throw new InvalidOperationException(
                "EF model changes detected without a migration. " +
                "Run: dotnet ef migrations add <Name> --project src/featureflags/FeatureFlags.csproj --startup-project src/featureflags/FeatureFlags.csproj");
        }

        appDb.Database.Migrate();
    }
}

app.Run();

public partial class Program { }
