using Hitek.BackendApi.Data;
using Hitek.BackendApi.Domain;

namespace Hitek.BackendApi.Common;

public static class SystemLogWriter
{
    private const int MaxMessageLength = 1000;

    // Chỉ thêm vào change tracker, không tự lưu: dòng nhật ký được ghi cùng một lần
    // SaveChanges với thay đổi mà nó mô tả, nên không có chuyện có nhật ký mà thiếu thay đổi.
    // tenantId bỏ trống thì HitekDbContext tự gán theo tenant của request.
    public static void Add(
        HitekDbContext db,
        DateTime occurredAt,
        SystemLogLevel level,
        SystemLogCategory category,
        string message,
        Guid? entityId = null,
        string? actor = null,
        Guid? tenantId = null)
    {
        db.SystemLogs.Add(new SystemLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId ?? Guid.Empty,
            OccurredAt = occurredAt,
            Level = level,
            Category = category,
            Message = message.Length <= MaxMessageLength ? message : message[..(MaxMessageLength - 1)] + "…",
            EntityId = entityId,
            Actor = actor
        });
    }
}
