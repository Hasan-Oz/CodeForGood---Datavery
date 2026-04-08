using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
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

    public class OllamaService
    {
        private readonly HttpClient _http;
        private readonly string _model;
        private readonly string _baseUrl;

        public OllamaService(string model = "llama3", string baseUrl = "http://localhost:11434")
        {
            _http    = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            _model   = model;
            _baseUrl = baseUrl;
        }

        public async Task<(Dataset dataset, string category)> SelectDatasetAsync(string question)
        {
            var prompt = "You are a dataset classifier for a data request system.\n" +
             "Available datasets: Users, Sales, Projects.\n\n" +
             "Users    = user accounts, registrations, profiles, login data\n" +
             "Sales    = transactions, revenue, orders, purchases, amounts\n" +
             "Projects = project status, deadlines, teams, ownership, tasks\n\n" +
             "Classify this question into ONE dataset and ONE category.\n" +
             "Category options: lookup, filter, count, aggregation, listing\n\n" +
             "Question: \"" + question + "\"\n\n" +
             "Respond ONLY with JSON in this exact format (no markdown, no explanation):\n" +
             "{\"dataset\": \"Users\", \"category\": \"listing\"}";
            

            try
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("  [AI] Selecting dataset");

                var request  = new OllamaRequest(_model, prompt, false);
                var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/generate", request);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<OllamaResponse>();
                var raw    = result?.Response?.Trim() ?? "";

                Console.WriteLine(" ✓");
                Console.ResetColor();

                // Clean up any markdown fences
                if (raw.Contains("```"))
                {
                    var s = raw.IndexOf('{');
                    var e = raw.LastIndexOf('}');
                    if (s >= 0 && e > s) raw = raw[s..(e + 1)];
                }

                using var doc      = JsonDocument.Parse(raw);
                var datasetStr     = doc.RootElement.GetProperty("dataset").GetString() ?? "";
                var categoryStr    = doc.RootElement.GetProperty("category").GetString() ?? "lookup";

                var dataset = datasetStr.ToLower() switch
                {
                    "users"    => Dataset.Users,
                    "sales"    => Dataset.Sales,
                    "projects" => Dataset.Projects,
                    _          => Dataset.Unknown
                };

                return (dataset, categoryStr);
            }
            catch (Exception ex)
            {
                Console.WriteLine($" ✗ ({ex.Message})");
                Console.ResetColor();
                return (Dataset.Unknown, "unknown");
            }
        }
    }
}
