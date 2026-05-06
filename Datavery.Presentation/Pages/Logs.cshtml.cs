using Datavery.DAL.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Datavery.Presentation.Pages
{
    public class LogsModel : PageModel
    {
        private readonly DatabaseService _db;

        public LogsModel(DatabaseService db)
        {
            _db = db;
        }

        public List<Dictionary<string, object>> Logs { get; set; } = new();

        public async Task OnGetAsync()
        {
            Logs = await _db.GetLogsAsync();
        }
    }
}