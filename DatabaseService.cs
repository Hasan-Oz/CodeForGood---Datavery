using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace DataRequestAgent
{
    public class DatabaseService
    {
        private readonly string _connectionString;

        public DatabaseService(string connectionString =
            "Server=localhost,1433;Database=DataRequestAgent;User Id=sa;Password=Admin1234!;TrustServerCertificate=True;")
        {
            _connectionString = connectionString;
        }

        // ── Init ──────────────────────────────────────────────────────────────

        public async Task InitialiseAsync()
        {
            var masterConn = _connectionString.Replace("Database=DataRequestAgent", "Database=master");
            await using (var conn = new SqlConnection(masterConn))
            {
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(
                    "IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'DataRequestAgent') CREATE DATABASE DataRequestAgent", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            await using var db = new SqlConnection(_connectionString);
            await db.OpenAsync();

            var schema = @"
                SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Users' AND xtype='U')
                CREATE TABLE Users (
                    Id INT IDENTITY(1,1) PRIMARY KEY, Name NVARCHAR(100) NOT NULL,
                    Email NVARCHAR(150) NOT NULL, Role NVARCHAR(50) NOT NULL,
                    RegisteredAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(), IsActive BIT NOT NULL DEFAULT 1);

                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Sales' AND xtype='U')
                CREATE TABLE Sales (
                    Id INT IDENTITY(1,1) PRIMARY KEY, Product NVARCHAR(100) NOT NULL,
                    Amount DECIMAL(10,2) NOT NULL, Customer NVARCHAR(100) NOT NULL,
                    SoldAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(), Region NVARCHAR(50) NOT NULL);

                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Projects' AND xtype='U')
                CREATE TABLE Projects (
                    Id INT IDENTITY(1,1) PRIMARY KEY, Name NVARCHAR(100) NOT NULL,
                    Owner NVARCHAR(100) NOT NULL, Status NVARCHAR(50) NOT NULL,
                    Deadline DATETIME2 NOT NULL, TeamSize INT NOT NULL);

                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='RequestLogs' AND xtype='U')
                CREATE TABLE RequestLogs (
                    Id INT IDENTITY(1,1) PRIMARY KEY, Question NVARCHAR(500) NOT NULL,
                    Dataset NVARCHAR(100) NOT NULL, Category NVARCHAR(50) NOT NULL,
                    Status NVARCHAR(20) NOT NULL, AskedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE());";

            await using (var cmd = new SqlCommand(schema, db))
                await cmd.ExecuteNonQueryAsync();

            await using (var cmd = new SqlCommand("SELECT COUNT(*) FROM Users", db))
            {
                var count = (int)await cmd.ExecuteScalarAsync();
                if (count == 0) await SeedAsync(db);
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  [DB] Connected and ready.");
            Console.ResetColor();
        }

        private async Task SeedAsync(SqlConnection db)
        {
            var seed = @"
                SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
                INSERT INTO Users (Name, Email, Role, RegisteredAt, IsActive) VALUES
                ('Alice Martens','alice@wecfg.com','Admin','2024-01-15',1),
                ('Ben Hoekstra','ben@wecfg.com','Developer','2024-02-20',1),
                ('Clara Visser','clara@wecfg.com','Designer','2024-03-10',1),
                ('David Smit','david@wecfg.com','Developer','2024-04-05',1),
                ('Eva de Jong','eva@wecfg.com','Manager','2024-05-18',0),
                ('Frank Bakker','frank@wecfg.com','Developer','2024-06-22',1),
                ('Grace Willems','grace@wecfg.com','Designer','2024-07-30',1),
                ('Hank Peters','hank@wecfg.com','Developer','2024-08-14',1);

                INSERT INTO Sales (Product, Amount, Customer, SoldAt, Region) VALUES
                ('AI Starter Pack',1200.00,'Alice Martens','2024-09-01','Noord-Holland'),
                ('Data Dashboard',3400.50,'Ben Hoekstra','2024-09-15','Zuid-Holland'),
                ('API Integration',850.00,'Clara Visser','2024-10-02','Utrecht'),
                ('AI Starter Pack',1200.00,'David Smit','2024-10-18','Gelderland'),
                ('Custom Report',2100.75,'Alice Martens','2024-11-05','Noord-Holland'),
                ('Data Dashboard',3400.50,'Frank Bakker','2024-11-20','Noord-Brabant'),
                ('API Integration',850.00,'Grace Willems','2024-12-03','Limburg'),
                ('Enterprise Suite',9500.00,'Hank Peters','2025-01-10','Noord-Holland'),
                ('AI Starter Pack',1200.00,'Ben Hoekstra','2025-01-25','Overijssel'),
                ('Custom Report',2100.75,'Clara Visser','2025-02-08','Utrecht');

                INSERT INTO Projects (Name, Owner, Status, Deadline, TeamSize) VALUES
                ('AI Data Agent','Alice Martens','In Progress','2025-06-30',4),
                ('Client Dashboard','Ben Hoekstra','In Progress','2025-04-15',3),
                ('Mobile App Redesign','Clara Visser','Planning','2025-08-01',2),
                ('API Gateway','David Smit','Completed','2025-02-28',3),
                ('Data Pipeline','Frank Bakker','In Progress','2025-05-20',5),
                ('Marketing Portal','Grace Willems','Planning','2025-09-10',2),
                ('Reporting Engine','Hank Peters','Completed','2025-01-31',4),
                ('Security Audit','Alice Martens','In Progress','2025-03-31',2);";

            await using var cmd = new SqlCommand(seed, db);
            await cmd.ExecuteNonQueryAsync();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  [DB] Sample data seeded.");
            Console.ResetColor();
        }

        // ── Single dataset query ──────────────────────────────────────────────

        public async Task<List<Dictionary<string, object>>> QueryAsync(Dataset dataset, string question)
        {
            var sql = dataset switch
            {
                Dataset.Users    => BuildUsersQuery(question),
                Dataset.Sales    => BuildSalesQuery(question),
                Dataset.Projects => BuildProjectsQuery(question),
                _                => null
            };
            if (sql == null) return new();
            return await ExecuteQueryAsync(sql);
        }

        // ── JOIN query ────────────────────────────────────────────────────────

        public async Task<List<Dictionary<string, object>>> QueryJoinAsync(Dataset a, Dataset b, string question)
        {
            var q = question.ToLower();
            string sql = "";

            // Users + Sales join
            if ((a == Dataset.Users && b == Dataset.Sales) || (a == Dataset.Sales && b == Dataset.Users))
            {
                if (q.Contains("total") || q.Contains("spent") || q.Contains("revenue"))
                    sql = @"SELECT u.Name, u.Role, u.Email,
                                   COUNT(s.Id) AS TotalOrders,
                                   SUM(s.Amount) AS TotalSpent
                            FROM Users u
                            INNER JOIN Sales s ON u.Name = s.Customer
                            GROUP BY u.Name, u.Role, u.Email
                            ORDER BY TotalSpent DESC";
                else if (q.Contains("product") || q.Contains("bought") || q.Contains("purchased"))
                    sql = @"SELECT u.Name, u.Role, s.Product, s.Amount, s.SoldAt, s.Region
                            FROM Users u
                            INNER JOIN Sales s ON u.Name = s.Customer
                            ORDER BY s.SoldAt DESC";
                else
                    sql = @"SELECT u.Name, u.Email, u.Role,
                                   s.Product, s.Amount, s.Region, s.SoldAt
                            FROM Users u
                            INNER JOIN Sales s ON u.Name = s.Customer
                            ORDER BY s.SoldAt DESC";
            }

            // Users + Projects join
            else if ((a == Dataset.Users && b == Dataset.Projects) || (a == Dataset.Projects && b == Dataset.Users))
            {
                if (q.Contains("in progress") || q.Contains("active"))
                    sql = @"SELECT u.Name, u.Email, u.Role,
                                   p.Name AS ProjectName, p.Status, p.Deadline, p.TeamSize
                            FROM Users u
                            INNER JOIN Projects p ON u.Name = p.Owner
                            WHERE p.Status = 'In Progress'
                            ORDER BY p.Deadline";
                else if (q.Contains("completed"))
                    sql = @"SELECT u.Name, u.Email,
                                   p.Name AS ProjectName, p.Status, p.Deadline
                            FROM Users u
                            INNER JOIN Projects p ON u.Name = p.Owner
                            WHERE p.Status = 'Completed'";
                else
                    sql = @"SELECT u.Name, u.Email, u.Role,
                                   p.Name AS ProjectName, p.Status, p.Deadline, p.TeamSize
                            FROM Users u
                            INNER JOIN Projects p ON u.Name = p.Owner
                            ORDER BY p.Deadline";
            }

            if (string.IsNullOrEmpty(sql)) return new();
            return await ExecuteQueryAsync(sql);
        }

        // ── Query builders ────────────────────────────────────────────────────

        private string BuildUsersQuery(string question)
        {
            var q = question.ToLower();
            if (q.Contains("inactive") || q.Contains("not active"))
                return "SELECT Id, Name, Email, Role, RegisteredAt FROM Users WHERE IsActive = 0 ORDER BY RegisteredAt DESC";
            if (q.Contains("admin"))
                return "SELECT Id, Name, Email, RegisteredAt FROM Users WHERE Role = 'Admin'";
            if (q.Contains("developer"))
                return "SELECT Id, Name, Email, RegisteredAt FROM Users WHERE Role = 'Developer' ORDER BY RegisteredAt";
            if (q.Contains("recent") || q.Contains("last") || q.Contains("new"))
                return "SELECT TOP 5 Id, Name, Email, Role, RegisteredAt FROM Users ORDER BY RegisteredAt DESC";
            if (q.Contains("count") || q.Contains("how many"))
                return "SELECT Role, COUNT(*) AS Total FROM Users GROUP BY Role ORDER BY Total DESC";
            return "SELECT TOP 10 Id, Name, Email, Role, RegisteredAt, IsActive FROM Users ORDER BY RegisteredAt DESC";
        }

        private string BuildSalesQuery(string question)
        {
            var q = question.ToLower();
            if (q.Contains("top") || q.Contains("highest") || q.Contains("most"))
                return "SELECT TOP 5 Product, Amount, Customer, Region, SoldAt FROM Sales ORDER BY Amount DESC";
            if (q.Contains("total") || q.Contains("revenue") || q.Contains("sum"))
                return "SELECT Product, SUM(Amount) AS TotalRevenue, COUNT(*) AS SalesCount FROM Sales GROUP BY Product ORDER BY TotalRevenue DESC";
            if (q.Contains("region"))
                return "SELECT Region, SUM(Amount) AS TotalRevenue, COUNT(*) AS SalesCount FROM Sales GROUP BY Region ORDER BY TotalRevenue DESC";
            if (q.Contains("recent") || q.Contains("last") || q.Contains("latest"))
                return "SELECT TOP 5 Product, Amount, Customer, Region, SoldAt FROM Sales ORDER BY SoldAt DESC";
            return "SELECT TOP 10 Id, Product, Amount, Customer, Region, SoldAt FROM Sales ORDER BY SoldAt DESC";
        }

        private string BuildProjectsQuery(string question)
        {
            var q = question.ToLower();
            if (q.Contains("in progress") || q.Contains("active") || q.Contains("ongoing"))
                return "SELECT Name, Owner, Deadline, TeamSize FROM Projects WHERE Status = 'In Progress' ORDER BY Deadline";
            if (q.Contains("completed") || q.Contains("done") || q.Contains("finished"))
                return "SELECT Name, Owner, Deadline, TeamSize FROM Projects WHERE Status = 'Completed'";
            if (q.Contains("planning") || q.Contains("upcoming"))
                return "SELECT Name, Owner, Deadline, TeamSize FROM Projects WHERE Status = 'Planning' ORDER BY Deadline";
            if (q.Contains("deadline") || q.Contains("due"))
                return "SELECT Name, Owner, Status, Deadline FROM Projects ORDER BY Deadline ASC";
            if (q.Contains("team") || q.Contains("size") || q.Contains("large"))
                return "SELECT Name, Owner, Status, TeamSize FROM Projects ORDER BY TeamSize DESC";
            return "SELECT TOP 10 Id, Name, Owner, Status, Deadline, TeamSize FROM Projects ORDER BY Deadline";
        }

        // ── Generic executor ──────────────────────────────────────────────────

        private async Task<List<Dictionary<string, object>>> ExecuteQueryAsync(string sql)
        {
            var results = new List<Dictionary<string, object>>();
            await using var conn = new SqlConnection(_connectionString);
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