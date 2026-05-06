using Datavery.BLL.Services;
using Datavery.Domain.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Datavery.Presentation.Pages
{
    public class IndexModel : PageModel
    {
        private readonly QueryService _queryService;

        public IndexModel(QueryService queryService)
        {
            _queryService = queryService;
        }

        [BindProperty]
        public string Question { get; set; } = "";

        public QueryResponse? Response { get; set; }

        public void OnGet()
        {
        }

        public async Task OnPostAsync()
        {
            if (string.IsNullOrWhiteSpace(Question))
                return;

            Response = await _queryService.HandleQueryAsync(Question);
        }
    }
}