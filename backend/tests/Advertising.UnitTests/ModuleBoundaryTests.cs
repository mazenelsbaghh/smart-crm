using Xunit;

namespace Advertising.UnitTests;

public sealed class ModuleBoundaryTests
{
    private static readonly string[] ForbiddenAdvertisingReferences =
    [
        "using Modules.AI.",
        "using Modules.Brain.",
        "using Modules.Conversations.",
        "using Modules.CRM.",
        "using Modules.GroupAppointments.",
        "using Modules.Media.",
        "using Modules.Projects.",
        "db.ConnectedPages",
        "db.ProjectSettings",
        "IMinIoStorageService"
    ];

    [Fact]
    public void Advertising_has_no_direct_dependency_on_another_module()
    {
        var advertisingRoot = Path.Combine(FindRepositoryRoot(), "backend", "src", "Modules", "Advertising");
        var violations = Directory.EnumerateFiles(advertisingRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => ForbiddenAdvertisingReferences
                .Where(reference => File.ReadAllText(file).Contains(reference, StringComparison.Ordinal))
                .Select(reference => $"{Path.GetRelativePath(advertisingRoot, file)} -> {reference}"))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "backend", "backend.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
