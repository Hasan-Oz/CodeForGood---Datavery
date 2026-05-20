using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Datavery.Domain.Enums;
using Microsoft.Data.SqlClient;

namespace Datavery.DAL.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;

        public DatabaseService(string connectionString =
            "Server=localhost,1434;Database=Datavery;User Id=sa;Password=Admin1234!;TrustServerCertificate=True;")
        {
            _connectionString = connectionString;
        }

        // ── Init ──────────────────────────────────────────────────────────────
        // Tables (Employees, Items, Workstations) already exist and are seeded
        // from the CSV import. We only create RequestLogs if missing.

        public async Task InitialiseAsync()
        {
            await using var db = new SqlConnection(_connectionString);
            await db.OpenAsync();

            var schema = @"
                SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='RequestLogs' AND xtype='U')
                CREATE TABLE RequestLogs (
                    Id       INT IDENTITY(1,1) PRIMARY KEY,
                    Question NVARCHAR(500) NOT NULL,
                    Dataset  NVARCHAR(100) NOT NULL,
                    Category NVARCHAR(50)  NOT NULL,
                    Status   NVARCHAR(20)  NOT NULL,
                    AskedAt  DATETIME2     NOT NULL DEFAULT GETUTCDATE()
                );";

            await using (var cmd = new SqlCommand(schema, db))
                await cmd.ExecuteNonQueryAsync();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  [DB] Connected to Datavery — real client data ready.");
            Console.ResetColor();
        }

        // ── Single dataset query ──────────────────────────────────────────────

        public async Task<List<Dictionary<string, object>>> QueryAsync(Dataset dataset, string question)
        {
            var sql = dataset switch
            {
                Dataset.Employees    => BuildEmployeesQuery(question),
                Dataset.Items        => BuildItemsQuery(question),
                Dataset.Workstations => BuildWorkstationsQuery(question),
                _                    => null
            };
            if (sql == null) return new();
            return await ExecuteQueryAsync(sql);
        }

        // ── JOIN queries ──────────────────────────────────────────────────────

        public async Task<List<Dictionary<string, object>>> QueryJoinAsync(Dataset a, Dataset b, string question)
        {
            var q = question.ToLower();
            string sql = "";

            // Employees + Items
            if ((a == Dataset.Employees && b == Dataset.Items) ||
                (a == Dataset.Items && b == Dataset.Employees))
            {
                if (q.Contains("count") || q.Contains("how many") || q.Contains("total"))
                    sql = @"SELECT e.name AS Employee, COUNT(i.id) AS TotalItems
                            FROM Employees e
                            INNER JOIN Items i ON e.id = i.employee_id
                            GROUP BY e.name
                            ORDER BY TotalItems DESC";
                else if (q.Contains("type") || q.Contains("clothing"))
                    sql = @"SELECT e.name AS Employee, i.tag AS ItemTag, i.clothing_type AS ClothingType
                            FROM Employees e
                            INNER JOIN Items i ON e.id = i.employee_id
                            ORDER BY e.name";
                else
                    sql = @"SELECT TOP 20 e.name AS Employee, e.code, i.tag AS ItemTag, i.clothing_type AS ClothingType
                            FROM Employees e
                            INNER JOIN Items i ON e.id = i.employee_id
                            ORDER BY e.name";
            }

            // Workstations + Items
            else if ((a == Dataset.Workstations && b == Dataset.Items) ||
                     (a == Dataset.Items && b == Dataset.Workstations))
            {
                if (q.Contains("count") || q.Contains("how many") || q.Contains("most"))
                    sql = @"SELECT w.name AS Workstation, w.tag AS WsTag, COUNT(i.id) AS TotalItems
                            FROM Workstations w
                            INNER JOIN Items i ON w.id = i.workstation_id
                            GROUP BY w.name, w.tag
                            ORDER BY TotalItems DESC";
                else if (q.Contains("type") || q.Contains("clothing"))
                    sql = @"SELECT w.name AS Workstation, i.tag AS ItemTag, i.clothing_type AS ClothingType
                            FROM Workstations w
                            INNER JOIN Items i ON w.id = i.workstation_id
                            ORDER BY w.name";
                else
                    sql = @"SELECT TOP 20 w.name AS Workstation, w.tag AS WsTag, i.tag AS ItemTag, i.clothing_type AS ClothingType
                            FROM Workstations w
                            INNER JOIN Items i ON w.id = i.workstation_id
                            ORDER BY w.name";
            }

            // Employees + Workstations (via Items)
            else if ((a == Dataset.Employees && b == Dataset.Workstations) ||
                     (a == Dataset.Workstations && b == Dataset.Employees))
            {
                sql = @"SELECT e.name AS Employee, w.name AS Workstation, w.tag AS WsTag, COUNT(i.id) AS ItemsProcessed
                        FROM Employees e
                        INNER JOIN Items i ON e.id = i.employee_id
                        INNER JOIN Workstations w ON w.id = i.workstation_id
                        GROUP BY e.name, w.name, w.tag
                        ORDER BY e.name";
            }

            if (string.IsNullOrEmpty(sql)) return new();
            return await ExecuteQueryAsync(sql);
        }

        // ── Query builders ────────────────────────────────────────────────────

        private string BuildEmployeesQuery(string question)
        {
            var q = question.ToLower();
            if (q.Contains("recent") || q.Contains("new") || q.Contains("latest"))
                return "SELECT TOP 5 id, name, code, tag, created_at FROM Employees ORDER BY created_at DESC";
            if (q.Contains("count") || q.Contains("how many"))
                return "SELECT COUNT(*) AS TotalEmployees FROM Employees WHERE deleted_at IS NULL";
            if (q.Contains("deleted") || q.Contains("inactive"))
                return "SELECT id, name, code, deleted_at FROM Employees WHERE deleted_at IS NOT NULL";
            if (q.Contains("active"))
                return "SELECT id, name, code, tag FROM Employees WHERE deleted_at IS NULL ORDER BY name";
            return "SELECT TOP 10 id, name, code, tag, created_at FROM Employees WHERE deleted_at IS NULL ORDER BY name";
        }

        private string BuildItemsQuery(string question)
        {
            var q = question.ToLower();
            if (q.Contains("count") || q.Contains("how many") || q.Contains("total"))
                return "SELECT COUNT(*) AS TotalItems FROM Items";
            if (q.Contains("type") || q.Contains("clothing"))
                return "SELECT clothing_type, COUNT(*) AS Count FROM Items GROUP BY clothing_type ORDER BY Count DESC";
            if (q.Contains("workstation") || q.Contains("station"))
                return "SELECT workstation_id, COUNT(*) AS ItemCount FROM Items GROUP BY workstation_id ORDER BY ItemCount DESC";
            if (q.Contains("employee") || q.Contains("who"))
                return "SELECT employee_id, COUNT(*) AS ItemCount FROM Items GROUP BY employee_id ORDER BY ItemCount DESC";
            return "SELECT TOP 10 id, tag, clothing_type, workstation_id, employee_id FROM Items ORDER BY id";
        }

        private string BuildWorkstationsQuery(string question)
        {
            var q = question.ToLower();
            if (q.Contains("count") || q.Contains("how many"))
                return "SELECT COUNT(*) AS TotalWorkstations FROM Workstations WHERE deleted_at IS NULL";
            if (q.Contains("warehouse"))
                return "SELECT id, name, warehouse_id, tag FROM Workstations ORDER BY warehouse_id";
            if (q.Contains("recent") || q.Contains("new") || q.Contains("latest"))
                return "SELECT TOP 5 id, name, tag, created_at FROM Workstations ORDER BY created_at DESC";
            return "SELECT id, name, warehouse_id, tag, created_at FROM Workstations WHERE deleted_at IS NULL ORDER BY id";
        }

        // ── Generic executor ──────────────────────────────────────────────────

        private async Task<List<Dictionary<string, object>>> ExecuteQueryAsync(string sql)
        {
            var results = new List<Dictionary<string, object>>();
            await using var conn   = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd    = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? "—" : reader.GetValue(i);
                results.Add(row);
            }
            return results;
        }

        // ── Logging ───────────────────────────────────────────────────────────

        public async Task SaveLogAsync(string question, string dataset, string category, string status)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "INSERT INTO RequestLogs (Question, Dataset, Category, Status) VALUES (@q, @d, @c, @s)", conn);
            cmd.Parameters.AddWithValue("@q", question);
            cmd.Parameters.AddWithValue("@d", dataset);
            cmd.Parameters.AddWithValue("@c", category);
            cmd.Parameters.AddWithValue("@s", status);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<Dictionary<string, object>>> GetLogsAsync()
            => await ExecuteQueryAsync(
                "SELECT TOP 20 Question, Dataset, Category, Status, AskedAt FROM RequestLogs ORDER BY AskedAt DESC");
    }
}
