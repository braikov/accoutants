using Braikov.Identity.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Accountant.Web.Controllers;

/// Dev-only smoke endpoints used during Phase B build-out. These exercise
/// the email pipeline without needing a registered user / live SMTP. The
/// route is wired only when `IsDevelopment()` is true (see Program.cs), so
/// it never ships to production.
[Route("dev")]
public sealed class DevDiagnosticsController : Controller
{
    private readonly IEmailSender emailSender;
    private readonly IEmailTemplateRenderer renderer;
    private readonly ILogger<DevDiagnosticsController> log;

    public DevDiagnosticsController(
        IEmailSender emailSender,
        IEmailTemplateRenderer renderer,
        ILogger<DevDiagnosticsController> log)
    {
        this.emailSender = emailSender;
        this.renderer = renderer;
        this.log = log;
    }

    /// GET /dev/test-email?to=foo@bar.com&template=EmailConfirmation
    /// Sends a test mail through the configured `IEmailSender`. With
    /// `Email:Enabled=false` (default in dev) the sender logs and skips
    /// real SMTP, so this just exercises the Razor template + DI graph.
    [HttpGet("test-email")]
    public async Task<IActionResult> TestEmail(
        [FromQuery] string to = "smoke-test@accountant.local",
        [FromQuery] string template = "EmailConfirmation",
        CancellationToken cancellationToken = default)
    {
        log.LogInformation("Dev smoke email: to={To} template={Template}", to, template);

        switch (template)
        {
            case "EmailConfirmation":
                await emailSender.SendEmailConfirmationAsync(
                    to,
                    "https://accountant.local/Identity/Account/ConfirmEmail?token=smoke",
                    shortCode: "123456",
                    culture: null,
                    cancellationToken);
                break;
            case "PasswordReset":
                await emailSender.SendPasswordResetAsync(
                    to,
                    "https://accountant.local/Identity/Account/ResetPassword?token=smoke",
                    shortCode: "654321",
                    culture: null,
                    cancellationToken);
                break;
            case "PasswordChanged":
                await emailSender.SendPasswordChangedAsync(
                    to,
                    DateTime.UtcNow,
                    "127.0.0.1",
                    culture: null,
                    cancellationToken);
                break;
            default:
                return BadRequest(new
                {
                    error = $"Unknown template '{template}'. Valid: EmailConfirmation | PasswordReset | PasswordChanged."
                });
        }

        return Ok(new
        {
            sent = true,
            to,
            template,
            note = "If Email:Enabled=false the sender just logs; check stdout for the warning line."
        });
    }

    /// GET /dev/render-template?template=EmailConfirmation
    /// Renders the template with a sample model and returns the raw HTML
    /// so you can preview it in the browser.
    [HttpGet("render-template")]
    public async Task<IActionResult> RenderTemplate(
        [FromQuery] string template = "EmailConfirmation",
        CancellationToken cancellationToken = default)
    {
        object model = template switch
        {
            "EmailConfirmation" => new Email.Models.EmailConfirmationModel
            {
                Email = "preview@accountant.local",
                CallbackUrl = "https://accountant.local/Identity/Account/ConfirmEmail?token=preview"
            },
            "PasswordReset" => new Email.Models.PasswordResetModel
            {
                Email = "preview@accountant.local",
                CallbackUrl = "https://accountant.local/Identity/Account/ResetPassword?token=preview"
            },
            "PasswordChanged" => new Email.Models.PasswordChangedModel
            {
                Email = "preview@accountant.local",
                ChangedAtUtc = DateTime.UtcNow,
                IpAddress = "127.0.0.1",
                FormattedChangedAt = DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm 'UTC'")
            },
            _ => null!
        };

        if (model is null)
        {
            return BadRequest(new
            {
                error = $"Unknown template '{template}'. Valid: EmailConfirmation | PasswordReset | PasswordChanged."
            });
        }

        var html = await renderer.RenderAsync(template, model, culture: null, cancellationToken);
        return Content(html, "text/html");
    }
}
