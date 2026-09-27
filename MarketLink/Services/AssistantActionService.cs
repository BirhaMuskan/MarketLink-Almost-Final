using MarketLink.Models;
using MarketLink.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace MarketLink.Services
{
    public interface IAssistantActionService
    {
        Task<MarketLinkAssistantReply?> TryExecuteAsync(
            int? userId,
            GroqIntentResult intent,
            CancellationToken cancellationToken = default);
    }

    public sealed class AssistantActionService : IAssistantActionService
    {
        private readonly ApplicationDbContext _context;

        public AssistantActionService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<MarketLinkAssistantReply?> TryExecuteAsync(
            int? userId,
            GroqIntentResult intent,
            CancellationToken cancellationToken = default)
        {
            var actionIntents = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                "EnableRestockAlert",
                "DisableRestockAlert",
                "FavoriteFarmer",
                "FavoriteProduct",
                "FavoriteMarket",
                "ShowNotifications"
            };

            if (!actionIntents.Contains(intent.Intent))
                return null;

            if (!userId.HasValue)
            {
                return Reply(
                    intent.Intent,
                    "Please sign in as a customer before I make account changes for you.");
            }

            var customer = await _context.customers
                .FirstOrDefaultAsync(
                    c => c.UserId == userId.Value,
                    cancellationToken);

            if (customer == null)
            {
                return Reply(
                    intent.Intent,
                    "I could not find a customer profile for this account, so I did not make any changes.");
            }

            return intent.Intent switch
            {
                "EnableRestockAlert" =>
                    await EnableRestockAlertAsync(
                        customer,
                        intent,
                        cancellationToken),

                "DisableRestockAlert" =>
                    await DisableRestockAlertAsync(
                        customer,
                        intent,
                        cancellationToken),

                "FavoriteFarmer" =>
                    await FavoriteFarmerAsync(
                        customer,
                        intent,
                        cancellationToken),

                "FavoriteProduct" =>
                    await FavoriteProductAsync(
                        customer,
                        intent,
                        restockAlert: false,
                        cancellationToken),

                "FavoriteMarket" =>
                    await FavoriteMarketAsync(
                        customer,
                        intent,
                        cancellationToken),

                "ShowNotifications" =>
                    await ShowNotificationsAsync(
                        userId.Value,
                        cancellationToken),

                _ => null
            };
        }

        private async Task<MarketLinkAssistantReply> EnableRestockAlertAsync(
            Customer customer,
            GroqIntentResult intent,
            CancellationToken cancellationToken)
        {
            var listing = await ResolveListingAsync(
                intent,
                cancellationToken);

            if (listing == null)
            {
                return Reply(
                    "EnableRestockAlert",
                    "I could not identify the exact product listing, so no restock alert was created.");
            }

            var favorite = await _context.favoriteproducts
                .FirstOrDefaultAsync(
                    x =>
                        x.CustomerId == customer.CustomerId &&
                        x.FarmerProductId == listing.FarmerProductId,
                    cancellationToken);

            if (favorite == null)
            {
                favorite = new FavoriteProduct
                {
                    CustomerId = customer.CustomerId,
                    FarmerProductId = listing.FarmerProductId,
                    RestockAlert = true,
                    CreatedAt = DateTime.Now
                };

                _context.favoriteproducts.Add(favorite);
            }
            else
            {
                favorite.RestockAlert = true;
            }

            var preference = await _context.notificationpreferences
                .FirstOrDefaultAsync(
                    x => x.UserId == customer.UserId,
                    cancellationToken);

            if (preference == null)
            {
                _context.notificationpreferences.Add(
                    new NotificationPreference
                    {
                        UserId = customer.UserId,
                        InAppEnabled = true,
                        EmailEnabled = true,
                        OrderUpdates = true,
                        PickupReminders = true,
                        RestockAlerts = true,
                        AiAlerts = true
                    });
            }
            else
            {
                preference.RestockAlerts = true;
                preference.InAppEnabled = true;
            }

            await _context.SaveChangesAsync(cancellationToken);

            var names = await GetListingNamesAsync(
                listing,
                cancellationToken);

            return Reply(
                "EnableRestockAlert",
                $"Done — a restock alert is now enabled for {names.ProductName} from {names.FarmerName}. I’ll create an in-app notification when MarketLink detects available stock.",
                new CustomerAssistantActionViewModel
                {
                    Label = "View Favorites",
                    Controller = "CustomerFavorites",
                    Action = "Index"
                });
        }

        private async Task<MarketLinkAssistantReply> DisableRestockAlertAsync(
            Customer customer,
            GroqIntentResult intent,
            CancellationToken cancellationToken)
        {
            var listing = await ResolveListingAsync(
                intent,
                cancellationToken);

            if (listing == null)
            {
                return Reply(
                    "DisableRestockAlert",
                    "I could not identify the exact product alert to cancel, so nothing was changed.");
            }

            var favorite = await _context.favoriteproducts
                .FirstOrDefaultAsync(
                    x =>
                        x.CustomerId == customer.CustomerId &&
                        x.FarmerProductId == listing.FarmerProductId,
                    cancellationToken);

            if (favorite == null || !favorite.RestockAlert)
            {
                return Reply(
                    "DisableRestockAlert",
                    "There is no active restock alert for that product.");
            }

            favorite.RestockAlert = false;

            await _context.SaveChangesAsync(cancellationToken);

            var names = await GetListingNamesAsync(
                listing,
                cancellationToken);

            return Reply(
                "DisableRestockAlert",
                $"Restock alerts are now off for {names.ProductName} from {names.FarmerName}.");
        }

        private async Task<MarketLinkAssistantReply> FavoriteFarmerAsync(
            Customer customer,
            GroqIntentResult intent,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(intent.Farmer))
            {
                return Reply(
                    "FavoriteFarmer",
                    "I could not identify which farmer you want to save, so nothing was changed.");
            }

            var farmer = await _context.farmers
                .FirstOrDefaultAsync(
                    f =>
                        f.IsActive &&
                        f.IsApproved &&
                        f.BusinessName == intent.Farmer,
                    cancellationToken);

            if (farmer == null)
            {
                return Reply(
                    "FavoriteFarmer",
                    "I could not find that active farmer, so nothing was saved.");
            }

            var exists = await _context.favoritefarmers
                .AnyAsync(
                    x =>
                        x.CustomerId == customer.CustomerId &&
                        x.FarmerId == farmer.FarmerId,
                    cancellationToken);

            if (!exists)
            {
                _context.favoritefarmers.Add(
                    new FavoriteFarmer
                    {
                        CustomerId = customer.CustomerId,
                        FarmerId = farmer.FarmerId,
                        CreatedAt = DateTime.Now
                    });

                await _context.SaveChangesAsync(cancellationToken);
            }

            return Reply(
                "FavoriteFarmer",
                exists
                    ? $"{farmer.BusinessName} is already in your favorite farmers."
                    : $"{farmer.BusinessName} has been added to your favorite farmers.",
                new CustomerAssistantActionViewModel
                {
                    Label = "View Favorites",
                    Controller = "CustomerFavorites",
                    Action = "Index"
                });
        }

        private async Task<MarketLinkAssistantReply> FavoriteProductAsync(
            Customer customer,
            GroqIntentResult intent,
            bool restockAlert,
            CancellationToken cancellationToken)
        {
            var listing = await ResolveListingAsync(
                intent,
                cancellationToken);

            if (listing == null)
            {
                return Reply(
                    "FavoriteProduct",
                    "I could not identify the exact product listing to save, so nothing was changed.");
            }

            var favorite = await _context.favoriteproducts
                .FirstOrDefaultAsync(
                    x =>
                        x.CustomerId == customer.CustomerId &&
                        x.FarmerProductId == listing.FarmerProductId,
                    cancellationToken);

            if (favorite == null)
            {
                favorite = new FavoriteProduct
                {
                    CustomerId = customer.CustomerId,
                    FarmerProductId = listing.FarmerProductId,
                    RestockAlert = restockAlert,
                    CreatedAt = DateTime.Now
                };

                _context.favoriteproducts.Add(favorite);

                await _context.SaveChangesAsync(cancellationToken);
            }

            var names = await GetListingNamesAsync(
                listing,
                cancellationToken);

            return Reply(
                "FavoriteProduct",
                favorite.FavoriteProductId > 0
                    ? $"{names.ProductName} from {names.FarmerName} is saved in your favorites."
                    : $"{names.ProductName} from {names.FarmerName} has been saved.",
                new CustomerAssistantActionViewModel
                {
                    Label = "View Favorites",
                    Controller = "CustomerFavorites",
                    Action = "Index"
                });
        }

        private async Task<MarketLinkAssistantReply> FavoriteMarketAsync(
            Customer customer,
            GroqIntentResult intent,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(intent.Market))
            {
                return Reply(
                    "FavoriteMarket",
                    "I could not identify which market you want to save, so nothing was changed.");
            }

            var market = await _context.markets
                .FirstOrDefaultAsync(
                    m =>
                        m.IsActive &&
                        m.MarketName == intent.Market,
                    cancellationToken);

            if (market == null)
            {
                return Reply(
                    "FavoriteMarket",
                    "I could not find that active market, so nothing was saved.");
            }

            var exists = await _context.favoritemarkets
                .AnyAsync(
                    x =>
                        x.CustomerId == customer.CustomerId &&
                        x.MarketId == market.MarketId,
                    cancellationToken);

            if (!exists)
            {
                _context.favoritemarkets.Add(
                    new FavoriteMarket
                    {
                        CustomerId = customer.CustomerId,
                        MarketId = market.MarketId,
                        CreatedAt = DateTime.Now
                    });

                await _context.SaveChangesAsync(cancellationToken);
            }

            return Reply(
                "FavoriteMarket",
                exists
                    ? $"{market.MarketName} is already one of your saved markets."
                    : $"{market.MarketName} has been added to your saved markets.",
                new CustomerAssistantActionViewModel
                {
                    Label = "View Saved Markets",
                    Controller = "CustomerFavorites",
                    Action = "Index"
                });
        }

        private async Task<MarketLinkAssistantReply> ShowNotificationsAsync(
            int userId,
            CancellationToken cancellationToken)
        {
            var items = await _context.notifications
                .AsNoTracking()
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(5)
                .ToListAsync(cancellationToken);

            if (items.Count == 0)
            {
                return Reply(
                    "ShowNotifications",
                    "You do not have any notifications yet.");
            }

            var lines = string.Join(
                "\n",
                items.Select(n =>
                    $"• {n.Title} — {n.Message}"));

            return Reply(
                "ShowNotifications",
                $"Here are your latest notifications:\n{lines}",
                new CustomerAssistantActionViewModel
                {
                    Label = "Open Notifications",
                    Controller = "CustomerNotifications",
                    Action = "Index"
                });
        }

        private async Task<FarmerProduct?> ResolveListingAsync(
            GroqIntentResult intent,
            CancellationToken cancellationToken)
        {
            IQueryable<FarmerProduct> query =
                _context.farmerproducts
                    .Where(fp =>
                        fp.IsActive &&
                        fp.IsApproved);

            if (!string.IsNullOrWhiteSpace(intent.Farmer))
            {
                var farmerIds = _context.farmers
                    .Where(f =>
                        f.IsActive &&
                        f.IsApproved &&
                        f.BusinessName == intent.Farmer)
                    .Select(f => f.FarmerId);

                query = query.Where(fp =>
                    farmerIds.Contains(fp.FarmerId));
            }

            if (!string.IsNullOrWhiteSpace(intent.Product))
            {
                var productIds = _context.products
                    .Where(p =>
                        p.IsActive &&
                        p.ProductName == intent.Product)
                    .Select(p => p.ProductId);

                query = query.Where(fp =>
                    productIds.Contains(fp.ProductId));
            }

            var matches = await query
                .Take(2)
                .ToListAsync(cancellationToken);

            return matches.Count == 1
                ? matches[0]
                : null;
        }

        private async Task<(string ProductName, string FarmerName)> GetListingNamesAsync(
            FarmerProduct listing,
            CancellationToken cancellationToken)
        {
            var productName = await _context.products
                .Where(p => p.ProductId == listing.ProductId)
                .Select(p => p.ProductName)
                .FirstOrDefaultAsync(cancellationToken)
                ?? "this product";

            var farmerName = await _context.farmers
                .Where(f => f.FarmerId == listing.FarmerId)
                .Select(f => f.BusinessName)
                .FirstOrDefaultAsync(cancellationToken)
                ?? "this farmer";

            return (productName, farmerName);
        }

        private static MarketLinkAssistantReply Reply(
            string intent,
            string text,
            params CustomerAssistantActionViewModel[] actions)
        {
            return new MarketLinkAssistantReply
            {
                Intent = intent,
                Text = text,
                Actions = actions.ToList(),
                Metadata = new Dictionary<string, object?>
                {
                    ["actionExecuted"] = true
                }
            };
        }
    }
}
