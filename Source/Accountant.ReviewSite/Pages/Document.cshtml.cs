using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Accountant.ReviewSite.Services;

namespace Accountant.ReviewSite.Pages;

public class DocumentModel : PageModel
{
    private readonly DocumentStore _store;

    public DocumentModel(DocumentStore store)
    {
        _store = store;
    }

    public DocumentDetail? Document { get; private set; }

    public IActionResult OnGet(string file)
    {
        Document = _store.GetDetail(file);
        return Document is null ? NotFound() : Page();
    }
}
