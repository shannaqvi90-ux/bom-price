using System.Text.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;

namespace BomPriceApproval.API.Infrastructure.Services;

public class AdminAuditLogger(AppDbContext db)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Adds a new AdminAuditLog row to the DbContext (NOT saved — caller is expected to call SaveChangesAsync in the same transaction as the entity mutation).
    /// </summary>
    public void Log<TBefore, TAfter>(int adminUserId, AdminActionType actionType, string entityType, int entityId, string reason, TBefore before, TAfter? after)
        where TBefore : class
        where TAfter : class
    {
        var row = new AdminAuditLog
        {
            AdminUserId = adminUserId,
            ActionType = actionType,
            EntityType = entityType,
            EntityId = entityId,
            Reason = reason,
            BeforeJson = JsonSerializer.Serialize(before, JsonOpts),
            AfterJson = after is null ? null : JsonSerializer.Serialize(after, JsonOpts),
            CreatedAt = DateTime.UtcNow
        };
        db.AdminAuditLogs.Add(row);
    }
}
