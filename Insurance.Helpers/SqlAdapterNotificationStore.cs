using System.Data;
using Insurance.Contracts.Notifications;
using Microsoft.Data.SqlClient;

namespace Insurance.Helpers;

public sealed class SqlAdapterNotificationStore
{
    private readonly string _connectionString;
    public SqlAdapterNotificationStore(string connectionString) => _connectionString = connectionString;

    public int Insert(InsuranceNotificationDto dto)
    {
        using var connection = new SqlConnection(_connectionString);
        using var command = new SqlCommand("""
            SET NOCOUNT ON;
            INSERT INTO dbo.InsuranceNotification
                (EventType, EntityName, EntityId, Title, Message, TargetUserName, TargetRole, OccurredUtc,
                 IsRead, IsDeleted, Active)
            VALUES
                (@EventType, @EntityName, @EntityId, @Title, @Message, @TargetUserName, @TargetRole, @OccurredUtc,
                 0, 0, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, connection);
        command.Parameters.AddWithValue("@EventType", dto.EventType);
        command.Parameters.AddWithValue("@EntityName", dto.EntityName);
        command.Parameters.AddWithValue("@EntityId", dto.EntityId);
        command.Parameters.AddWithValue("@Title", dto.Title);
        command.Parameters.AddWithValue("@Message", dto.Message);
        command.Parameters.AddWithValue("@TargetUserName", (object?)dto.TargetUserName ?? DBNull.Value);
        command.Parameters.AddWithValue("@TargetRole", (object?)dto.TargetRole ?? DBNull.Value);
        command.Parameters.AddWithValue("@OccurredUtc", dto.OccurredUtc);
        connection.Open();
        var id = command.ExecuteScalar();
        dto.NotificationId = id is null or DBNull ? 0 : Convert.ToInt32(id);
        dto.IsRead = false;
        dto.IsDeleted = false;
        dto.Active = true;
        return dto.NotificationId;
    }

    public IReadOnlyList<InsuranceNotificationDto> Recent(int take = 50, bool unreadOnly = false)
    {
        using var connection = new SqlConnection(_connectionString);
        using var adapter = new SqlDataAdapter("""
            SELECT TOP (@Take) NotificationId, EventType, EntityName, EntityId, Title, Message,
                   TargetUserName, TargetRole, OccurredUtc, IsRead, IsDeleted, Active, ReadUtc, DeletedUtc
            FROM dbo.InsuranceNotification
            WHERE Active = 1 AND IsDeleted = 0
              AND (@UnreadOnly = 0 OR IsRead = 0)
            ORDER BY OccurredUtc DESC, NotificationId DESC
            """, connection);
        adapter.SelectCommand!.Parameters.AddWithValue("@Take", take);
        adapter.SelectCommand.Parameters.AddWithValue("@UnreadOnly", unreadOnly ? 1 : 0);
        var table = new DataTable();
        adapter.Fill(table);
        return table.Rows.Cast<DataRow>().Select(Map).ToList();
    }

    public int MarkRead(int notificationId)
    {
        using var connection = new SqlConnection(_connectionString);
        using var command = new SqlCommand("""
            UPDATE dbo.InsuranceNotification
            SET IsRead = 1, ReadUtc = SYSUTCDATETIME()
            WHERE NotificationId = @Id AND Active = 1 AND IsDeleted = 0
            """, connection);
        command.Parameters.AddWithValue("@Id", notificationId);
        connection.Open();
        return command.ExecuteNonQuery();
    }

    public int SoftDelete(int notificationId)
    {
        using var connection = new SqlConnection(_connectionString);
        using var command = new SqlCommand("""
            UPDATE dbo.InsuranceNotification
            SET IsDeleted = 1, Active = 0, DeletedUtc = SYSUTCDATETIME()
            WHERE NotificationId = @Id AND Active = 1
            """, connection);
        command.Parameters.AddWithValue("@Id", notificationId);
        connection.Open();
        return command.ExecuteNonQuery();
    }

    private static InsuranceNotificationDto Map(DataRow row) => new()
    {
        NotificationId = Convert.ToInt32(row["NotificationId"]),
        EventType = Convert.ToString(row["EventType"]) ?? "",
        EntityName = Convert.ToString(row["EntityName"]) ?? "",
        EntityId = Convert.ToString(row["EntityId"]) ?? "",
        Title = Convert.ToString(row["Title"]) ?? "",
        Message = Convert.ToString(row["Message"]) ?? "",
        TargetUserName = row["TargetUserName"] as string,
        TargetRole = row["TargetRole"] as string,
        OccurredUtc = Convert.ToDateTime(row["OccurredUtc"]),
        IsRead = Convert.ToBoolean(row["IsRead"]),
        IsDeleted = Convert.ToBoolean(row["IsDeleted"]),
        Active = Convert.ToBoolean(row["Active"]),
        ReadUtc = row["ReadUtc"] is DBNull ? null : Convert.ToDateTime(row["ReadUtc"]),
        DeletedUtc = row["DeletedUtc"] is DBNull ? null : Convert.ToDateTime(row["DeletedUtc"])
    };
}
