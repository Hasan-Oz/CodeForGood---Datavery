using Datavery.DAL.Services;
using Datavery.Domain.Enums;
using Datavery.Domain.Models;

namespace Datavery.BLL.Services
{
    public class QueryService
    {
        private readonly DatabaseService _db;
        private readonly OllamaService _ollama;

        public QueryService(DatabaseService db, OllamaService ollama)
        {
            _db = db;
            _ollama = ollama;
        }

        public async Task<QueryResponse> HandleQueryAsync(string question)
        {
            var response = new QueryResponse
            {
                Question = question
            };

            try
            {
                var selection = await _ollama.SelectDatasetAsync(question);

                response.Category = selection.Category;
                response.IsJoin = selection.IsJoin;
                response.JoinHint = selection.JoinHint;
                response.Datasets = selection.Datasets.Select(d => d.ToString()).ToList();

                if (selection.Datasets.Count == 1 && selection.Datasets[0] == Dataset.Unknown)
                {
                    response.Status = "failed";
                    response.Message = "Could not determine a relevant dataset. Try rephrasing.";
                    await _db.SaveLogAsync(question, "Unknown", selection.Category, response.Status);
                    return response;
                }

                List<Dictionary<string, object>> results;

                if (selection.IsJoin && selection.Datasets.Count == 2)
                {
                    results = await _db.QueryJoinAsync(selection.Datasets[0], selection.Datasets[1], question);
                }
                else if (selection.Datasets.Count == 2)
                {
                    var a = await _db.QueryAsync(selection.Datasets[0], question);
                    var b = await _db.QueryAsync(selection.Datasets[1], question);
                    results = a.Concat(b).ToList();
                }
                else
                {
                    results = await _db.QueryAsync(selection.Datasets[0], question);
                }

                response.Results = results;
                response.Message = results.Count == 0
                    ? "No records found."
                    : "Query completed successfully.";

                var datasetLabel = string.Join(" + ", response.Datasets);
                await _db.SaveLogAsync(question, datasetLabel, selection.Category, "success");

                return response;
            }
            catch (Exception ex)
            {
                response.Status = "error";
                response.Message = $"Something went wrong: {ex.Message}";
                await _db.SaveLogAsync(question, "Unknown", "unknown", "error");
                return response;
            }
        }
    }
}