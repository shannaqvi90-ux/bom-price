using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Infrastructure;

public class CodeGeneratorServiceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CodeGeneratorServiceTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task NextCustomerCode_ReturnsCustPrefixWithDigits()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICodeGeneratorService>();
        var code = await svc.NextCustomerCodeAsync();
        Assert.Matches(@"^CUST-\d{4,}$", code);
    }

    [Fact]
    public async Task NextItemCode_FinishedGood_UsesFGPrefix()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICodeGeneratorService>();
        var code = await svc.NextItemCodeAsync(ItemType.FinishedGood);
        Assert.Matches(@"^FG-\d{4,}$", code);
    }

    [Fact]
    public async Task NextItemCode_RawMaterial_UsesRMPrefix()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICodeGeneratorService>();
        var code = await svc.NextItemCodeAsync(ItemType.RawMaterial);
        Assert.Matches(@"^RM-\d{4,}$", code);
    }

    [Fact]
    public async Task NextCustomerCode_SequentialCalls_AreUnique()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICodeGeneratorService>();
        var c1 = await svc.NextCustomerCodeAsync();
        var c2 = await svc.NextCustomerCodeAsync();
        var c3 = await svc.NextCustomerCodeAsync();
        Assert.NotEqual(c1, c2);
        Assert.NotEqual(c2, c3);
        Assert.NotEqual(c1, c3);
    }

    [Fact]
    public async Task NextCustomerCode_ConcurrentCalls_AllUnique()
    {
        const int N = 10;
        var tasks = new List<Task<string>>();
        for (int i = 0; i < N; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var scope = _factory.Services.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<ICodeGeneratorService>();
                return await svc.NextCustomerCodeAsync();
            }));
        }
        var codes = await Task.WhenAll(tasks);
        Assert.Equal(N, codes.Distinct().Count());
    }
}
