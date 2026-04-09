using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DataRequestAgent
{
    public enum Dataset { Users, Sales, Projects, Unknown }

    public record OllamaRequest(
        [property: JsonPropertyName("model")]  string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("stream")] bool   Stream = false
    );

    public record OllamaResponse(
        [property: JsonPropertyName("response")] string Response
    );

    public record DatasetSelection(
        List<Dataset> Datasets,
        string        Category,
        bool          IsJoin,
        string        JoinHint
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
            var prompt =
                "You are a dataset classifier for a data request system.\n" +
                "Available datasets: Users, Sales, Projects.\n\n" +
                "Users    = user accounts, registrations, profiles, login data. Columns: Id, Name, Email, Role, RegisteredAt, IsActive\n" +
                "Sales    = transactions, revenue, orders, purchases, amounts. Columns: Id, Product, Amount, Customer, Region, SoldAt\n" +
                "Projects = project status, deadlines, teams, ownership, tasks. Columns: Id, Name, Owner, Status, Deadline, TeamSize\n\n" +
                "Users and Sales can be joined on: Users.Name = Sales.Customer\n" +
                "Users and Projects can be joined on: Users.Name = Projects.Owner\n\n" +
                "If the question involves TWO datasets, list both in the datasets array.\n" +
                "If a join makes sense, set isJoin to true and provide the joinHint.\n" +
                "Category options: lookup, filter, count, aggregation, listing, join\n\n" +
                "Question: \"" + question + "\"\n\n" +
                "Respond ONLY with JSON (no markdown, no explanation):\n" +
                "{\"datasets\": [\"Users\"], \"category\": \"listing\", \"isJoin\": false, \"joinHint\": \"\"}";

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

                if (raw.Contains("```"))
                {
                    var s = raw.IndexOf('{');
                    var e = raw.LastIndexOf('}');
                    if (s >= 0 && e > s) raw = raw[s..(e + 1)];
                }

                if (!raw.TrimEnd().EndsWith("}"))
                    raw = raw.TrimEnd() + "}";

                using var doc  = JsonDocument.Parse(raw);
                var root       = doc.RootElement;
                var category = "";
                var isJoin = false;
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
                return new DatasetSelection(datasets, category, isJoin, joinHint);
            }
            catch (Exception ex)
            {
                Console.WriteLine($" ✗ ({ex.Message})");
                Console.ResetColor();
                return new DatasetSelection(new List<Dataset> { Dataset.Unknown }, "unknown", false, "");
            }
        }

        private static Dataset ParseDataset(string s) => s.ToLower() switch
        {
            "users"    => Dataset.Users,
            "sales"    => Dataset.Sales,
            "projects" => Dataset.Projects,
            _          => Dataset.Unknown
        };
    }
}