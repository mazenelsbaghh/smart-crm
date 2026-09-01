using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Modules.AI.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class GeminiMockEnvironmentTests
{
    [Theory]
    [InlineData("Production", null, false)]
    [InlineData("Production", "mock_test", false)]
    [InlineData("Development", null, true)]
    [InlineData("Test", "mock_test", true)]
    public void Mock_keys_are_accepted_only_in_non_production_test_environments(
        string environmentName,
        string? apiKey,
        bool expected)
    {
        var handler = new GeminiMockHandler(new TestHostEnvironment
        {
            EnvironmentName = environmentName
        });

        Assert.Equal(expected, handler.IsMockKey(apiKey));
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Advertising.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
