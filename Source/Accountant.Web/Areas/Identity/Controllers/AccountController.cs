using Accountant.Identity.Models;
using Braikov.Identity.Core.Abstractions;
using Braikov.Identity.Core.Controllers;
using Braikov.Identity.Core.Resources;
using Braikov.Identity.Core.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace Accountant.Web.Areas.Identity.Controllers;

/// Thin derived controller. All auth flow logic lives in
/// `BaseAccountController<TUser, TKey>` from `Braikov.Identity.Core`.
/// Override individual virtual actions here if Accountant needs to diverge
/// from the default flow.
[Area("Identity")]
public sealed class AccountController : BaseAccountController<ApplicationUser, int>
{
    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IIdentityEmailDispatcher emailDispatcher,
        IAccountEventLog eventLog,
        IEmailRateLimiter rateLimiter,
        IShortCodeTokenService shortCodeTokenService,
        IStringLocalizer<SharedResource> localizer,
        ILogger<BaseAccountController<ApplicationUser, int>> logger)
        : base(userManager, signInManager, emailDispatcher, eventLog, rateLimiter, shortCodeTokenService, localizer, logger)
    {
    }

    protected override ApplicationUser CreateUser(RegisterViewModel model) =>
        new()
        {
            UserName = model.Email,
            Email = model.Email
        };
}
