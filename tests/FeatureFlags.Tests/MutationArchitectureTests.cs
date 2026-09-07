using System.Text.RegularExpressions;

namespace FeatureFlags.Tests;

public class MutationArchitectureTests
{
    [Fact]
    public void Application_writes_cannot_bypass_the_mutation_lifecycle()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "FeatureFlags.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var source = Path.Combine(root!.FullName, "src", "featureflags");
        var violations = new List<string>();
        foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                     .Where(p => (p.EndsWith(".cs") || p.EndsWith(".razor")) &&
                         !p.Contains("/obj/") && !p.Contains("/bin/") && !p.Contains("/Migrations/")))
        {
            var text = File.ReadAllText(path);
            if (Regex.IsMatch(text, @"\.(ExecuteUpdate|ExecuteDelete|ExecuteSql\w*|ExecuteNonQuery)\s*(Async)?\s*\(")) violations.Add(path);
            if (!path.EndsWith("FeatureFlagDbContext.cs") && Regex.IsMatch(text, @"\.SaveChanges(Async)?\s*\(")) violations.Add(path);
            if (!path.EndsWith("ProjectMutation.cs") && Regex.IsMatch(text, @"\.SaveMutationAsync\s*\(")) violations.Add(path);
        }
        Assert.Empty(violations);
    }
}
