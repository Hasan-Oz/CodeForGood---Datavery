using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DataRequestAgent
{
    // ── Orchestrator ──────────────────────────────────────────────────────────

    public class AgentOrchestrator
    {
        private readonly DatabaseService _db;
        private readonly OllamaService   _ollama;
        private readonly RequestLogger   _logger;

        public AgentOrchestrator(DatabaseService db, OllamaService ollama, RequestLogger logger)
        {
            _db     = db;
            _ollama = ollama;
            _logger = logger;
        }

        public async Task HandleQueryAsync(string question)
        {
            var status = "success";

            try
            {
                // Step 1 — AI selects dataset
                var (dataset, category) = await _ollama.SelectDatasetAsync(question);

                if (dataset == Dataset.Unknown)
                {
                    PrintWarning("Could not determine a relevant dataset for your question. Try rephrasing.");
                    status = "failed";
                    _logger.Add(question, "Unknown", category, status);
                    await _db.SaveLogAsync(question, "Unknown", category, status);
                    return;
                }

                PrintDatasetBadge(dataset, category);

                // Step 2 — Query the database
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("  [DB] Querying dataset");
                var results = await _db.QueryAsync(dataset, question);
                Console.WriteLine(" ✓");
                Console.ResetColor();

                // Step 3 — Display results
                if (results.Count == 0)
                {
                    PrintWarning("No records found matching your question.");
                    status = "empty";
                }
                else
                {
                    PrintResults(results, dataset.ToString());
                }

                // Step 4 — Log it
                _logger.Add(question, dataset.ToString(), category, status);
                await _db.SaveLogAsync(question, dataset.ToString(), category, status);

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"\n  Request logged. Type 'logs' to view history.");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                PrintError($"Something went wrong: {ex.Message}");
                _logger.Add(question, "Unknown", "unknown", "error");
                await _db.SaveLogAsync(question, "Unknown", "unknown", "error");
            }
        }

        // ── Display helpers ───────────────────────────────────────────────────

        private void PrintDatasetBadge(Dataset dataset, string category)
        {
            var color = dataset switch
            {
                Dataset.Users    => ConsoleColor.Blue,
                Dataset.Sales    => ConsoleColor.Green,
                Dataset.Projects => ConsoleColor.Magenta,
                _                => ConsoleColor.Gray
            };

            Console.ForegroundColor = color;
            Console.WriteLine($"  ▶ Dataset selected: {dataset}  |  Category: {category}");
            Console.ResetColor();
        }

        private void PrintResults(List<Dictionary<string, object>> rows, string datasetName)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"  Results from {datasetName} ({rows.Count} record{(rows.Count == 1 ? "" : "s")}):");
            Console.ResetColor();
            Console.WriteLine("  " + new string('─', 60));

            foreach (var row in rows)
            {
                Console.Write("  ");
                foreach (var kv in row)
                {
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.Write($"{kv.Key}: ");
                    Console.ForegroundColor = ConsoleColor.White;
                    var val = kv.Value is DateTime dt ? dt.ToString("yyyy-MM-dd") : kv.Value.ToString();
                    Console.Write($"{val}  ");
                }
                Console.WriteLine();
                Console.ResetColor();
            }

            Console.WriteLine("  " + new string('─', 60));
        }

        private void PrintWarning(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  ⚠ {msg}");
            Console.ResetColor();
        }

        private void PrintError(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  ✗ {msg}");
            Console.ResetColor();
        }
    }

    // ── In-memory logger (also persisted to DB) ───────────────────────────────

    public class RequestLogger
    {
        private readonly List<LogEntry> _entries = new();

        public record LogEntry(string Question, string Dataset, string Category, string Status, DateTime AskedAt);

        public void Add(string question, string dataset, string category, string status)
        {
            _entries.Add(new LogEntry(question, dataset, category, status, DateTime.Now));
        }

        public void PrintLogs()
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  ╔══════════════════════════════════════════════════════════╗");
            Console.WriteLine("  ║                   Request Log History                   ║");
            Console.WriteLine("  ╚══════════════════════════════════════════════════════════╝");
            Console.ResetColor();

            if (_entries.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  No requests yet this session.");
                Console.ResetColor();
                return;
            }

            Console.WriteLine();

            // Summary stats
            var datasetCounts = new Dictionary<string, int>();
            var categoryCounts = new Dictionary<string, int>();
            int successCount = 0;

            foreach (var e in _entries)
            {
                datasetCounts[e.Dataset] = datasetCounts.GetValueOrDefault(e.Dataset) + 1;
                categoryCounts[e.Category] = categoryCounts.GetValueOrDefault(e.Category) + 1;
                if (e.Status == "success") successCount++;
            }

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"  Total requests : {_entries.Count}");
            Console.WriteLine($"  Successful     : {successCount}");
            Console.WriteLine();

            Console.WriteLine("  Dataset breakdown:");
            foreach (var kv in datasetCounts)
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine($"    {kv.Key,-12} → {kv.Value} request(s)");
            }

            Console.WriteLine();
            Console.WriteLine("  Category breakdown:");
            foreach (var kv in categoryCounts)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"    {kv.Key,-12} → {kv.Value} request(s)");
            }

            Console.ResetColor();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("  Recent requests:");
            Console.WriteLine("  " + new string('─', 60));

            foreach (var e in _entries)
            {
                var statusColor = e.Status == "success" ? ConsoleColor.Green : ConsoleColor.Red;
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"  {e.AskedAt:HH:mm:ss}  ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"[{e.Dataset,-10}] ");
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.Write($"[{e.Category,-12}] ");
                Console.ForegroundColor = statusColor;
                Console.Write($"[{e.Status,-8}] ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(e.Question.Length > 40 ? e.Question[..40] + "..." : e.Question);
            }

            Console.WriteLine("  " + new string('─', 60));
            Console.ResetColor();
        }
    }
}
