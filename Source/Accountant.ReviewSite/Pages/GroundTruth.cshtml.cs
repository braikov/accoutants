using Accountant.ReviewSite.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Accountant.ReviewSite.Pages;

public class GroundTruthModel : PageModel
{
    private readonly DocumentStore _store;

    public GroundTruthModel(DocumentStore store) { _store = store; }

    public GroundTruthEditDetail? Detail { get; private set; }
    public string? FlashMessage { get; private set; }

    public IActionResult OnGet(string file)
    {
        Detail = _store.GetGroundTruthEdit(file);
        return Detail is null ? NotFound() : Page();
    }

    public IActionResult OnPost(string file)
    {
        Detail = _store.GetGroundTruthEdit(file);
        if (Detail is null) return NotFound();

        var updates = new Dictionary<string, string?>(StringComparer.Ordinal);
        const string prefix = "row[";
        foreach (var key in Request.Form.Keys)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal) || !key.EndsWith("]", StringComparison.Ordinal))
                continue;
            var path = key[prefix.Length..^1];
            updates[path] = Request.Form[key];
        }

        _store.SaveGroundTruth(Detail.Stem, updates);
        Detail = _store.GetGroundTruthEdit(file);
        FlashMessage = $"Saved {updates.Count} field(s) to ground_truth/{Detail!.Stem}.ground_truth.json";
        return Page();
    }
}
