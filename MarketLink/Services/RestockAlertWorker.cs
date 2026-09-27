using MarketLink.Models;
using Microsoft.EntityFrameworkCore;

namespace MarketLink.Services
{
    public sealed class RestockAlertWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RestockAlertWorker> _logger;

        public RestockAlertWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<RestockAlertWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAlertsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Restock alert worker failed.");
                }

                await Task.Delay(
                    TimeSpan.FromMinutes(1),
                    stoppingToken);
            }
        }

        private async Task CheckAlertsAsync(
            CancellationToken cancellationToken)
        {
            using var scope =
                _scopeFactory.CreateScope();

            var db =
                scope.ServiceProvider
                    .GetRequiredService<ApplicationDbContext>();

            var favorites = await db.favoriteproducts
                .Where(f => f.RestockAlert)
                .ToListAsync(cancellationToken);

            if (favorites.Count == 0)
                return;

            var customerIds =
                favorites
                    .Select(f => f.CustomerId)
                    .Distinct()
                    .ToList();

            var customers = await db.customers
                .Where(c => customerIds.Contains(c.CustomerId))
                .ToDictionaryAsync(
                    c => c.CustomerId,
                    cancellationToken);

            var listingIds =
                favorites
                    .Select(f => f.FarmerProductId)
                    .Distinct()
                    .ToList();

            var listings = await db.farmerproducts
                .Where(fp =>
                    listingIds.Contains(fp.FarmerProductId) &&
                    fp.IsActive &&
                    fp.IsApproved &&
                    fp.IsAvailable)
                .ToDictionaryAsync(
                    fp => fp.FarmerProductId,
                    cancellationToken);

            var inventoryRows = await db.inventories
                .AsNoTracking()
                .Where(i =>
                    listingIds.Contains(i.FarmerProductId) &&
                    i.InventoryDate >= DateTime.Today &&
                    i.IsAvailable &&
                    !i.IsSoldOut)
                .OrderBy(i => i.InventoryDate)
                .ToListAsync(cancellationToken);

            foreach (var favorite in favorites)
            {
                if (!customers.TryGetValue(
                        favorite.CustomerId,
                        out var customer))
                {
                    continue;
                }

                if (!listings.TryGetValue(
                        favorite.FarmerProductId,
                        out var listing))
                {
                    continue;
                }

                var available = inventoryRows
                    .Where(i =>
                        i.FarmerProductId ==
                        favorite.FarmerProductId)
                    .FirstOrDefault(i =>
                        i.AvailableQuantity > 0);

                if (available == null)
                    continue;

                var preference =
                    await db.notificationpreferences
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            p => p.UserId == customer.UserId,
                            cancellationToken);

                if (preference != null &&
                    (!preference.InAppEnabled ||
                     !preference.RestockAlerts))
                {
                    continue;
                }

                var productName =
                    await db.products
                        .Where(p =>
                            p.ProductId == listing.ProductId)
                        .Select(p => p.ProductName)
                        .FirstOrDefaultAsync(cancellationToken)
                    ?? "A saved product";

                var farmerName =
                    await db.farmers
                        .Where(f =>
                            f.FarmerId == listing.FarmerId)
                        .Select(f => f.BusinessName)
                        .FirstOrDefaultAsync(cancellationToken)
                    ?? "a farmer";

                db.notifications.Add(
                    new Notification
                    {
                        UserId = customer.UserId,
                        Title = "Product back in stock",
                        Message =
                            $"{productName} from {farmerName} is available on {available.InventoryDate:dd MMM yyyy}. Available quantity: {available.AvailableQuantity:N3}.",
                        NotificationType = "Restock",
                        ActionUrl =
                            $"/CustomerProducts/Details/{listing.FarmerProductId}",
                        IsRead = false,
                        CreatedAt = DateTime.Now
                    });

                // One-shot alert. The customer can ask the assistant to enable it again.
                favorite.RestockAlert = false;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
