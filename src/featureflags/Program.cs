using FeatureFlags.Data;
using FeatureFlags.Components;
using FeatureFlags.Components.Models;
using FeatureFlags.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<FeatureFlagDbContext>(options =>
    options.UseSqlite("Data Source=featureflags.db"));
builder.Services.AddScoped<IFeatureFlagConfirmationService, FeatureFlagConfirmationService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/api/featureflags", async (FeatureFlagDbContext db) =>
{
    var flags = await db.FeatureFlags
        .OrderBy(f => f.Name)
        .ToListAsync();

    var response = new FeatureFlagsResponse
    {
        FeatureFlags = flags.ToDictionary(
            f => f.Name,
            f => f.IsEnabled)
    };

    return Results.Ok(response);
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FeatureFlagDbContext>();
    db.Database.Migrate();
}

app.Run();
