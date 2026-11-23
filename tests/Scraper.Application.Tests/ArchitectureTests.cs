using System.Reflection;
using NetArchTest.Rules;

namespace Scraper.Application.Tests;

/// <summary>Enforces the dependency rule: Domain ← Application ← Infrastructure / hosts.</summary>
public class ArchitectureTests
{
    private static readonly string[] InfrastructureNamespaces =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "StackExchange.Redis",
        "RabbitMQ",
        "Microsoft.Playwright",
        "AngleSharp",
        "Microsoft.AspNetCore",
    ];

    [Theory]
    [InlineData("Scraper.Domain")]
    [InlineData("Scraper.Application")]
    public void Core_does_not_depend_on_infrastructure(string assemblyName)
    {
        var result = Types.InAssembly(Assembly.Load(assemblyName))
            .ShouldNot().HaveDependencyOnAny(InfrastructureNamespaces)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Domain_does_not_depend_on_application()
    {
        var result = Types.InAssembly(Assembly.Load("Scraper.Domain"))
            .ShouldNot().HaveDependencyOn("Scraper.Application")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    // Domain and Application are still empty, so the rules above pass trivially. This test proves the
    // rule really detects violations by running it against Infrastructure, which must fail it.
    [Fact]
    public void Rule_detects_infrastructure_dependencies()
    {
        var result = Types.InAssembly(Assembly.Load("Scraper.Infrastructure"))
            .ShouldNot().HaveDependencyOnAny(InfrastructureNamespaces)
            .GetResult();

        Assert.False(result.IsSuccessful);
    }

    private static string Describe(TestResult result) =>
        "Forbidden dependencies in: " + string.Join(", ", result.FailingTypeNames ?? []);
}
