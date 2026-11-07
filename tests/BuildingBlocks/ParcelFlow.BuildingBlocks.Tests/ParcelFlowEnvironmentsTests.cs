using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using ParcelFlow.BuildingBlocks.Api.Hosting;
using Xunit;

namespace ParcelFlow.BuildingBlocks.Tests;

public sealed class ParcelFlowEnvironmentsTests
{
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Demo", true)]
    [InlineData("demo", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("Testing", false)]
    public void Startup_database_initialization_is_limited_to_development_and_the_demo(string name, bool allowed) =>
        Environment(name).AllowsStartupDatabaseInitialization().Should().Be(allowed);

    [Fact]
    public void The_demo_is_not_development() =>
        Environment(ParcelFlowEnvironments.Demo).IsDevelopment().Should().BeFalse();

    private static IHostEnvironment Environment(string name) => new TestEnvironment { EnvironmentName = name };

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
