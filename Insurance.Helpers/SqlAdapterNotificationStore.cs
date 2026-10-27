using Insurance.Contracts.Notifications;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;

namespace Insurance.Helpers
{
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
            (EventType, EntityName, EntityId, Title, Message, TargetUserName, TargetRole, OccurredUtc)
        VALUES
            (@EventType, @EntityName, @EntityId, @Title, @Message, @TargetUserName, @TargetRole, @OccurredUtc);
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
            return dto.NotificationId;
        }

        public IReadOnlyList<InsuranceNotificationDto> Recent(int take = 50)
        {
            using var connection = new SqlConnection(_connectionString);
            using var adapter = new SqlDataAdapter("""
            SELECT TOP (@Take) NotificationId, EventType, EntityName, EntityId, Title, Message,
                   TargetUserName, TargetRole, OccurredUtc
            FROM dbo.InsuranceNotification
            ORDER BY NotificationId DESC
            """, connection);
            adapter.SelectCommand!.Parameters.AddWithValue("@Take", take);
            var table = new DataTable();
            adapter.Fill(table);
            return table.Rows.Cast<DataRow>().Select(row => new InsuranceNotificationDto
            {
                NotificationId = Convert.ToInt32(row["NotificationId"]),
                EventType = Convert.ToString(row["EventType"]) ?? "",
                EntityName = Convert.ToString(row["EntityName"]) ?? "",
                EntityId = Convert.ToString(row["EntityId"]) ?? "",
                Title = Convert.ToString(row["Title"]) ?? "",
                Message = Convert.ToString(row["Message"]) ?? "",
                TargetUserName = row["TargetUserName"] as string,
                TargetRole = row["TargetRole"] as string,
                OccurredUtc = Convert.ToDateTime(row["OccurredUtc"])
            }).ToList();
        }

    }
}
