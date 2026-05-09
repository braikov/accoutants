using Microsoft.AspNetCore.Mvc.RazorPages;
using Accountant.ReviewSite.Services;

namespace Accountant.ReviewSite.Pages;

public class IndexModel : PageModel
{
    private readonly DocumentStore _store;

    public IndexModel(DocumentStore store)
    {
        _store = store;
    }

    public IReadOnlyList<DocumentListItem> Documents { get; private set; } = [];
    public string ImageFolder { get; private set; } = "";
    public IReadOnlyList<string> CandidateImageFolders { get; private set; } = [];

    public void OnGet()
    {
        ImageFolder = _store.ImageFolder;
        CandidateImageFolders = _store.CandidateImageFolders;
        Documents = _store.GetDocuments();
    }
}
