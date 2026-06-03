using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Datavery.Domain.Enums;
using Datavery.Domain.Models;

namespace Datavery.BLL.Services
{
    public record OllamaRequest(
        [property: JsonPropertyName("model")]  string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("stream")] bool   Stream = false
    );

    public record OllamaResponse(
        [property: JsonPropertyName("response")] string Response
    );

    public class OllamaService
    {
        private readonly HttpClient _http;
        private readonly string     _model;
        private readonly string     _baseUrl;

        public OllamaService(string model = "llama3", string baseUrl = "http://localhost:11434")
        {
            _http    = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            _model   = model;
            _baseUrl = baseUrl;
        }

        public async Task<DatasetSelection> SelectDatasetAsync(string question)
        {
            // ── Few-Shot + Chain of Thought prompt ────────────────────────────
            // The model reasons about the question semantically instead of
            // matching keywords, then selects the correct dataset(s).

            var prompt =
                "You are a data analyst. Your job is to read a question and figure out\n" +
                "which dataset(s) contain the answer. Think step by step before deciding.\n\n" +

                "Available datasets:\n" +
                "- Employees    = people who work at the company, staff members, workers, who someone is.\n" +
                "                Columns: id, name, code, tag, created_at, updated_at, deleted_at\n" +
                "- Items        = clothing items that are tracked, physical garments, tagged products,\n" +
                "                what is being processed or collected. Each item belongs to an employee and a workstation.\n" +
                "                Columns: id, tag, clothing_type, workstation_id, employee_id\n" +
                "- Workstations = physical locations or stations where items are processed,\n" +
                "                company sites, warehouses, work locations.\n" +
                "                Columns: id, name, warehouse_id, tag, created_at, deleted_at\n\n" +

                "Possible joins:\n" +
                "- Employees + Items        linked on: Items.employee_id = Employees.id\n" +
                "- Workstations + Items     linked on: Items.workstation_id = Workstations.id\n" +
                "- Employees + Workstations linked on: both connected through Items\n\n" +

                "Category options: lookup, filter, count, aggregation, listing, join\n\n" +

                "--- Examples ---\n\n" +

                "Q: \"Show me all employees\"\n" +
                "Thought: The question is about people who work at the company → Employees table.\n" +
                "Answer: {\"datasets\": [\"Employees\"], \"category\": \"listing\", \"isJoin\": false, \"joinHint\": \"\"}\n\n" +

                "Q: \"How many clothing items are there in total?\"\n" +
                "Thought: Clothing items = Items table. Asking for a count → aggregation.\n" +
                "Answer: {\"datasets\": [\"Items\"], \"category\": \"count\", \"isJoin\": false, \"joinHint\": \"\"}\n\n" +

                "Q: \"Which workstation has the most items?\"\n" +
                "Thought: Most items per workstation = need both Items and Workstations, grouped by workstation → JOIN.\n" +
                "Answer: {\"datasets\": [\"Workstations\", \"Items\"], \"category\": \"aggregation\", \"isJoin\": true, \"joinHint\": \"Items.workstation_id = Workstations.id\"}\n\n" +

                "Q: \"How many items does each employee have?\"\n" +
                "Thought: Items per employee = need Items and Employees together, counting per person → JOIN.\n" +
                "Answer: {\"datasets\": [\"Employees\", \"Items\"], \"category\": \"aggregation\", \"isJoin\": true, \"joinHint\": \"Items.employee_id = Employees.id\"}\n\n" +

                "Q: \"Which staff members are working at each location?\"\n" +
                "Thought: Staff = Employees, location = Workstations, connected through Items → JOIN.\n" +
                "Answer: {\"datasets\": [\"Employees\", \"Workstations\"], \"category\": \"join\", \"isJoin\": true, \"joinHint\": \"Employees and Workstations connected through Items\"}\n\n" +

                "Q: \"Show me the workstations\"\n" +
                "Thought: Asking about locations/stations directly → Workstations table.\n" +
                "Answer: {\"datasets\": [\"Workstations\"], \"category\": \"listing\", \"isJoin\": false, \"joinHint\": \"\"}\n\n" +

                "--- Now answer this ---\n\n" +
                "Q: \"" + question + "\"\n" +
                "Thought:";

            try
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("  [AI] Analysing question");

                var request  = new OllamaRequest(_model, prompt, false);
                var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/generate", request);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<OllamaResponse>();
                var raw    = result?.Response?.Trim() ?? "";

                Console.WriteLine(" ✓");
                Console.ResetColor();

                // Extract JSON — model writes Thought: ... Answer: {...}
                var jsonStart = raw.LastIndexOf('{');
                var jsonEnd   = raw.LastIndexOf('}');
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                    raw = raw[jsonStart..(jsonEnd + 1)];
                else
                {
                    // No JSON found at all
                    return new DatasetSelection
                    {
                        Datasets = new List<Dataset> { Dataset.Unknown },
                        Category = "unknown",
                        IsJoin   = false,
                        JoinHint = ""
                    };
                }    

                if (!raw.TrimEnd().EndsWith("}"))
                    raw = raw.TrimEnd() + "}";

                using var doc = JsonDocument.Parse(raw);
                var root      = doc.RootElement;

                var category = "";
                var isJoin   = false;
                var joinHint = "";

                if (root.TryGetProperty("category", out var c))
                    category = c.ValueKind == JsonValueKind.String ? c.GetString() ?? "lookup" : "lookup";

                if (root.TryGetProperty("isJoin", out var j))
                    isJoin = j.ValueKind == JsonValueKind.True;

                if (root.TryGetProperty("joinHint", out var jh))
                    joinHint = jh.ValueKind == JsonValueKind.String ? jh.GetString() ?? "" : "";

                var datasets = new List<Dataset>();
                if (root.TryGetProperty("datasets", out var datasetsEl))
                {
                    foreach (var el in datasetsEl.EnumerateArray())
                    {
                        var d = ParseDataset(el.GetString() ?? "");
                        if (d != Dataset.Unknown && !datasets.Contains(d))
                            datasets.Add(d);
                    }
                }

                if (datasets.Count == 0) datasets.Add(Dataset.Unknown);
                return new DatasetSelection
                {
                    Datasets = datasets,
                    Category = category,
                    IsJoin   = isJoin,
                    JoinHint = joinHint
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($" ✗ ({ex.Message})");
                Console.ResetColor();
                return new DatasetSelection
                {
                    Datasets = new List<Dataset> { Dataset.Unknown },
                    Category = "unknown",
                    IsJoin   = false,
                    JoinHint = ""
                };
            }
        }

        private static Dataset ParseDataset(string s) => s.ToLower() switch
        {
            "employees"    => Dataset.Employees,
            "items"        => Dataset.Items,
            "workstations" => Dataset.Workstations,
            _              => Dataset.Unknown
        };
    }
}
