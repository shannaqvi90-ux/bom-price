using BomPriceApproval.API.Infrastructure.Services;
using FluentAssertions;

namespace BomPriceApproval.Tests.Infrastructure;

public class PasswordGeneratorTests
{
    [Fact]
    public void Generate_DefaultLength_Returns12Chars()
    {
        var pwd = PasswordGenerator.Generate();
        pwd.Length.Should().Be(12);
    }

    [Fact]
    public void Generate_AlwaysContainsAllCharClasses()
    {
        for (int i = 0; i < 100; i++)
        {
            var pwd = PasswordGenerator.Generate();
            pwd.Should().MatchRegex("[a-z]", "needs lowercase");
            pwd.Should().MatchRegex("[A-Z]", "needs uppercase");
            pwd.Should().MatchRegex("[0-9]", "needs digit");
            pwd.Should().MatchRegex("[!@#$%^&*]", "needs special");
        }
    }

    [Fact]
    public void Generate_ProducesDifferentValuesAcrossCalls()
    {
        var samples = Enumerable.Range(0, 50).Select(_ => PasswordGenerator.Generate()).ToHashSet();
        samples.Count.Should().BeGreaterThan(45, "should be near-collision-free at 12 chars");
    }
}
