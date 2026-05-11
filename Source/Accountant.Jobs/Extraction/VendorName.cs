namespace Accountant.Jobs.Extraction;

/// Canonical vendor names. Kept short and lowercase to match `appsettings`
/// values, `Document.Vendor` storage, and admin UI selection (Phase J).
public static class VendorName
{
    public const string Claude = "claude";
    public const string Codex = "codex";
    public const string Gemini = "gemini";
}
