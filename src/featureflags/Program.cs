using FeatureFlags.Data;
using FeatureFlags.Components;
using FeatureFlags.Components.Models;
using FeatureFlags.Services;
using Microsoft.EntityFrameworkCore;
using FeatureFlags.Core;
using Microsoft.AspNetCore.Identity;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddRazorPages();

var connectionString = builder.Configuration.GetConnectionString(FeatureFlagDbContextFactory.ConnectionStringName)
    ?? FeatureFlagDbContextFactory.DefaultConnectionString;

builder.Services.AddDbContext<FeatureFlagDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddDbContextFactory<FeatureFlagDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddScoped<IFeatureFlagConfirmationService, FeatureFlagConfirmationService>();
builder.Services.AddScoped<IProjectPermissionService, ProjectPermissionService>();
builder.Services.AddScoped<IProjectProvisioningService, ProjectProvisioningService>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

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

var app = builder.Build();

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

app.MapGet("/api/featureflags", async (
    HttpContext http,
    FeatureFlagDbContext db) =>
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
});

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


app.Run();
