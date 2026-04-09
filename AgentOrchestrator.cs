using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DataRequestAgent
{
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
                // Step 1 — AI analyses question
                var selection = await _ollama.SelectDatasetAsync(question);

                if (selection.Datasets.Count == 1 && selection.Datasets[0] == Dataset.Unknown)
                {
                    PrintWarning("Could not determine a relevant dataset. Try rephrasing.");
                    status = "failed";
                    _logger.Add(question, "Unknown", selection.Category, status);
                    await _db.SaveLogAsync(question, "Unknown", selection.Category, status);
                    return;
                }

                // Step 2 — Handle join vs multi vs single
                if (selection.IsJoin && selection.Datasets.Count == 2)
                {
                    await HandleJoinQueryAsync(question, selection);
                }
                else if (selection.Datasets.Count == 2)
                {
                    await HandleMultiQueryAsync(question, selection);
                }
                else
                {
                    await HandleSingleQueryAsync(question, selection);
                }

                var datasetLabel = string.Join(" + ", selection.Datasets);
                _logger.Add(question, datasetLabel, selection.Category, status);
                await _db.SaveLogAsync(question, datasetLabel, selection.Category, status);

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

        // ── Single dataset ────────────────────────────────────────────────────

        private async Task HandleSingleQueryAsync(string question, DatasetSelection selection)
        {
            var dataset = selection.Datasets[0];
            PrintDatasetBadge(new List<Dataset> { dataset }, selection.Category, false);

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  [DB] Querying");
            var results = await _db.QueryAsync(dataset, question);
            Console.WriteLine(" ✓");
            Console.ResetColor();

            if (results.Count == 0)
                PrintWarning("No records found.");
            else
                PrintResults(results, dataset.ToString());
        }

        // ── Two datasets side by side ─────────────────────────────────────────

        private async Task HandleMultiQueryAsync(string question, DatasetSelection selection)
        {
            PrintDatasetBadge(selection.Datasets, selection.Category, false);

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  [DB] Querying both datasets in parallel");

            // Run both queries at the same time
            var taskA = _db.QueryAsync(selection.Datasets[0], question);
            var taskB = _db.QueryAsync(selection.Datasets[1], question);
            await Task.WhenAll(taskA, taskB);

            Console.WriteLine(" ✓");
            Console.ResetColor();

            if (taskA.Result.Count == 0 && taskB.Result.Count == 0)
            {
                PrintWarning("No records found in either dataset.");
                return;
            }

            if (taskA.Result.Count > 0)
                PrintResults(taskA.Result, selection.Datasets[0].ToString());

            if (taskB.Result.Count > 0)
                PrintResults(taskB.Result, selection.Datasets[1].ToString());
        }

        // ── Joined query ──────────────────────────────────────────────────────

        private async Task HandleJoinQueryAsync(string question, DatasetSelection selection)
        {
            PrintDatasetBadge(selection.Datasets, selection.Category, true);

            if (!string.IsNullOrEmpty(selection.JoinHint))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  [JOIN] {selection.JoinHint}");
                Console.ResetColor();
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  [DB] Running join query");
            var results = await _db.QueryJoinAsync(selection.Datasets[0], selection.Datasets[1], question);
            Console.WriteLine(" ✓");
            Console.ResetColor();

            if (results.Count == 0)
                PrintWarning("No matching records found across datasets.");
            else
                PrintResults(results, $"{selection.Datasets[0]} ⟕ {selection.Datasets[1]}");
        }

        // ── Display helpers ───────────────────────────────────────────────────

        private void PrintDatasetBadge(List<Dataset> datasets, string category, bool isJoin)
        {
            var colors = new[] { ConsoleColor.Blue, ConsoleColor.Green, ConsoleColor.Magenta };
            Console.Write("  ▶ Dataset");
            if (datasets.Count > 1) Console.Write("s");
            Console.Write(": ");

            for (int i = 0; i < datasets.Count; i++)
            {
                Console.ForegroundColor = colors[i % colors.Length];
                Console.Write(datasets[i].ToString());
                if (i < datasets.Count - 1)
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write(isJoin ? " ⟕ " : " + ");
                }
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"  |  Category: {category}");
            if (isJoin) Console.Write("  |  JOIN");
            Console.WriteLine();
            Console.ResetColor();
        }

        private void PrintResults(List<Dictionary<string, object>> rows, string label)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"  Results from {label} ({rows.Count} record{(rows.Count == 1 ? "" : "s")}):");
            Console.ResetColor();
            Console.WriteLine("  " + new string('─', 70));

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

            Console.WriteLine("  " + new string('─', 70));
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

    // ── In-memory logger ──────────────────────────────────────────────────────

    public class RequestLogger
    {
        private readonly List<LogEntry> _entries = new();
        public record LogEntry(string Question, string Dataset, string Category, string Status, DateTime AskedAt);

        public void Add(string question, string dataset, string category, string status)
            => _entries.Add(new LogEntry(question, dataset, category, status, DateTime.Now));

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

            var datasetCounts  = new Dictionary<string, int>();
            var categoryCounts = new Dictionary<string, int>();
            int successCount   = 0;

            foreach (var e in _entries)
            {
                datasetCounts[e.Dataset]   = datasetCounts.GetValueOrDefault(e.Dataset) + 1;
                categoryCounts[e.Category] = categoryCounts.GetValueOrDefault(e.Category) + 1;
                if (e.Status == "success") successCount++;
            }

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"\n  Total requests : {_entries.Count}");
            Console.WriteLine($"  Successful     : {successCount}");

            Console.WriteLine("\n  Dataset breakdown:");
            foreach (var kv in datasetCounts)
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine($"    {kv.Key,-20} → {kv.Value} request(s)");
            }

            Console.WriteLine("\n  Category breakdown:");
            foreach (var kv in categoryCounts)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"    {kv.Key,-20} → {kv.Value} request(s)");
            }

            Console.ResetColor();
            Console.WriteLine("\n  Recent requests:");
            Console.WriteLine("  " + new string('─', 70));

            foreach (var e in _entries)
            {
                var statusColor = e.Status == "success" ? ConsoleColor.Green : ConsoleColor.Red;
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"  {e.AskedAt:HH:mm:ss}  ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"[{e.Dataset,-22}] ");
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.Write($"[{e.Category,-12}] ");
                Console.ForegroundColor = statusColor;
                Console.Write($"[{e.Status,-8}] ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(e.Question.Length > 40 ? e.Question[..40] + "..." : e.Question);
            }

            Console.WriteLine("  " + new string('─', 70));
            Console.ResetColor();
        }
    }
}