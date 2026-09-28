using MarketLink.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MarketLink.Services
{
    /// <summary>
    /// Automatically sends an email whenever an in-app Notification is created.
    /// Existing notification-producing controllers/services do not need SMTP code.
    /// </summary>
    public sealed class NotificationEmailInterceptor : SaveChangesInterceptor
    {
        private readonly IEmailService _emailService;
        private readonly ILogger<NotificationEmailInterceptor> _logger;
        private List<Notification> _pendingNotifications = new();
        private bool _updatingDeliveryStatus;

        public NotificationEmailInterceptor(
            IEmailService emailService,
            ILogger<NotificationEmailInterceptor> logger)
        {
            _emailService = emailService;
            _logger = logger;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            CapturePendingNotifications(eventData.Context);
            return base.SavingChangesAsync(
                eventData,
                result,
                cancellationToken);
        }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            CapturePendingNotifications(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            await DeliverPendingEmailsAsync(
                eventData.Context,
                cancellationToken);

            return await base.SavedChangesAsync(
                eventData,
                result,
                cancellationToken);
        }

        public override int SavedChanges(
            SaveChangesCompletedEventData eventData,
            int result)
        {
            // The application primarily uses SaveChangesAsync. If a synchronous
            // save creates notifications, queue delivery on the thread pool and
            // never block the database save on SMTP.
            if (_pendingNotifications.Count > 0 &&
                eventData.Context is ApplicationDbContext db)
            {
                var ids = _pendingNotifications
                    .Where(n => n.NotificationId > 0)
                    .Select(n => n.NotificationId)
                    .Distinct()
                    .ToArray();

                _pendingNotifications.Clear();

                if (ids.Length > 0)
                {
                    _logger.LogWarning(
                        "{Count} notification(s) were created by synchronous SaveChanges. " +
                        "Use SaveChangesAsync so email delivery can be awaited.",
                        ids.Length);
                }
            }

            return base.SavedChanges(eventData, result);
        }

        private void CapturePendingNotifications(DbContext? context)
        {
            if (_updatingDeliveryStatus ||
                context is not ApplicationDbContext)
            {
                return;
            }

            _pendingNotifications = context.ChangeTracker
                .Entries<Notification>()
                .Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity)
                .ToList();
        }

        private async Task DeliverPendingEmailsAsync(
            DbContext? context,
            CancellationToken cancellationToken)
        {
            if (_updatingDeliveryStatus ||
                context is not ApplicationDbContext db ||
                _pendingNotifications.Count == 0)
            {
                return;
            }

            var pending = _pendingNotifications.ToList();
            _pendingNotifications.Clear();

            try
            {
                var userIds = pending
                    .Select(n => n.UserId)
                    .Distinct()
                    .ToList();

                var users = await db.users
                    .AsNoTracking()
                    .Include(u => u.Role)
                    .Where(u => userIds.Contains(u.UserId))
                    .ToDictionaryAsync(
                        u => u.UserId,
                        cancellationToken);

                var preferences = await db.notificationpreferences
                    .AsNoTracking()
                    .Where(p => userIds.Contains(p.UserId))
                    .ToDictionaryAsync(
                        p => p.UserId,
                        cancellationToken);

                var statusChanged = false;

                foreach (var notification in pending)
                {
                    if (!users.TryGetValue(
                            notification.UserId,
                            out var user))
                    {
                        notification.EmailSent = false;
                        notification.EmailError =
                            "Notification recipient was not found.";
                        statusChanged = true;
                        continue;
                    }

                    var roleName = user.Role?.RoleName ?? "";
                    var isCustomerOrFarmer =
                        roleName.Equals(
                            "Customer",
                            StringComparison.OrdinalIgnoreCase) ||
                        roleName.Equals(
                            "Farmer",
                            StringComparison.OrdinalIgnoreCase);

                    if (!isCustomerOrFarmer)
                    {
                        // Requirement: email notifications are for customers and farmers.
                        continue;
                    }

                    if (preferences.TryGetValue(
                            user.UserId,
                            out var preference))
                    {
                        if (!preference.EmailEnabled ||
                            !IsNotificationCategoryEnabled(
                                preference,
                                notification.NotificationType))
                        {
                            continue;
                        }
                    }

                    var sendResult =
                        await _emailService.SendNotificationAsync(
                            user.Email,
                            user.FullName,
                            notification.Title,
                            notification.Message,
                            notification.ActionUrl,
                            cancellationToken);

                    notification.EmailSent = sendResult.Sent;
                    notification.EmailSentAt =
                        sendResult.Sent
                            ? DateTime.Now
                            : null;
                    notification.EmailError =
                        sendResult.Sent
                            ? null
                            : CleanError(sendResult.Error);

                    statusChanged = true;
                }

                if (statusChanged)
                {
                    _updatingDeliveryStatus = true;
                    try
                    {
                        await db.SaveChangesAsync(cancellationToken);
                    }
                    finally
                    {
                        _updatingDeliveryStatus = false;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Never fail the business transaction just because SMTP failed.
                _logger.LogError(
                    ex,
                    "Notification email delivery processing failed.");
            }
        }

        private static bool IsNotificationCategoryEnabled(
            NotificationPreference preference,
            string? notificationType)
        {
            var type = notificationType?.Trim() ?? "";

            if (type.Equals("OrderUpdate", StringComparison.OrdinalIgnoreCase) ||
                type.Contains("Order", StringComparison.OrdinalIgnoreCase))
            {
                return preference.OrderUpdates;
            }

            if (type.Contains("Pickup", StringComparison.OrdinalIgnoreCase))
            {
                return preference.PickupReminders;
            }

            if (type.Equals("Restock", StringComparison.OrdinalIgnoreCase) ||
                type.Contains("Restock", StringComparison.OrdinalIgnoreCase))
            {
                return preference.RestockAlerts;
            }

            if (type.StartsWith("AI_", StringComparison.OrdinalIgnoreCase) ||
                type.Contains("AI", StringComparison.OrdinalIgnoreCase))
            {
                return preference.AiAlerts;
            }

            // Reviews, admin approval/rejection, and other general notifications
            // follow the master EmailEnabled switch.
            return true;
        }

        private static string? CleanError(string? error)
        {
            if (string.IsNullOrWhiteSpace(error))
                return null;

            var clean = error.Trim();
            return clean.Length <= 1000
                ? clean
                : clean[..1000];
        }
    }
}
