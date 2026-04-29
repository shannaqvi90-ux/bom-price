using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Infrastructure.Services;

public interface ICodeGeneratorService
{
    Task<string> NextCustomerCodeAsync();
    Task<string> NextItemCodeAsync(ItemType type);
}

public class CodeGeneratorService : ICodeGeneratorService
{
    private readonly AppDbContext _db;

    public CodeGeneratorService(AppDbContext db) => _db = db;

    public Task<string> NextCustomerCodeAsync() => NextAsync("CUST");

    public Task<string> NextItemCodeAsync(ItemType type) => type switch
    {
        ItemType.FinishedGood => NextAsync("FG"),
        ItemType.RawMaterial  => NextAsync("RM"),
        _ => throw new ArgumentException($"Unsupported ItemType: {type}", nameof(type))
    };

    private async Task<string> NextAsync(string sequence)
    {
        // Row-level lock for concurrent safety. Postgres FOR UPDATE locks the matched row
        // for the duration of the transaction; concurrent callers wait their turn.
        await using var tx = await _db.Database.BeginTransactionAsync();

        var sql = "SELECT \"NextValue\" AS \"Value\" FROM \"CodeCounters\" WHERE \"Sequence\" = {0} FOR UPDATE";
        var current = await _db.Database
            .SqlQueryRaw<int>(sql, sequence)
            .FirstOrDefaultAsync();

        if (current == 0)
            throw new InvalidOperationException(
                $"Counter row missing for sequence '{sequence}'. Run V3_AddCodeCounters migration.");

        var next = current + 1;
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE \"CodeCounters\" SET \"NextValue\" = {0} WHERE \"Sequence\" = {1}",
            next, sequence);

        await tx.CommitAsync();

        return $"{sequence}-{current:D4}";
    }
}
