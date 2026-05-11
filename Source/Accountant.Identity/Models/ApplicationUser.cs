using Microsoft.AspNetCore.Identity;

namespace Accountant.Identity.Models;

/// Application-specific user. Extends the framework's IdentityUser with
/// integer keys (rather than the default string GUIDs) so user IDs are
/// readable in URLs / logs and join naturally with future business tables
/// keyed on int.
///
/// Add domain-relevant profile fields here (display name, locale,
/// last-active-at, etc.) as the product grows. Roles will be added in a
/// later task; for now keep this minimal so Phase A's migration stays small.
public class ApplicationUser : IdentityUser<int>
{
}
