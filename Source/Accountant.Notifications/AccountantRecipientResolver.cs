using Accountant.DataAccess;
using Braikov.Notifications.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accountant.Notifications;

/// Maps an integer-keyed `ApplicationUser` (encoded as a string recipient id)
/// to the `NotificationRecipient` shape Braikov.Notifications expects. Email
/// is the only delivery channel today; push / SMS / in-app are out of scope
/// for Phase B.
public sealed class AccountantRecipientResolver : IRecipientResolver
{
    private readonly AccountantDbContext dbContext;
    private readonly AccountantNotificationOptions options;

    public AccountantRecipientResolver(
        AccountantDbContext dbContext,
        IOptions<AccountantNotificationOptions> options)
    {
        this.dbContext = dbContext;
        this.options = options.Value;
    }

    public async Task<NotificationRecipient?> ResolveAsync(
        string recipientId,
        CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(recipientId, out var userId))
        {
            return null;
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Email,
                candidate.EmailConfirmed,
                candidate.LockoutEnd
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return null;
        }

        return new NotificationRecipient
        {
            RecipientId = user.Id.ToString(),
            Email = user.Email,
            EmailConfirmed = user.EmailConfirmed,
            Culture = options.DefaultCulture,
            Status = ResolveStatus(user.LockoutEnd),
            DeliveryTargets = []
        };
    }

    private static NotificationRecipientStatus ResolveStatus(DateTimeOffset? lockoutEnd)
    {
        if (lockoutEnd.HasValue && lockoutEnd.Value > DateTimeOffset.UtcNow)
        {
            return NotificationRecipientStatus.Locked;
        }

        return NotificationRecipientStatus.Active;
    }
}
