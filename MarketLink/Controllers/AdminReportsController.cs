using System.Text;
using MarketLink.Models;
using MarketLink.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLink.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminReportsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminReportsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            DateTime? fromDate,
            DateTime? toDate,
            CancellationToken cancellationToken)
        {
            var to = (toDate ?? DateTime.Today).Date;
            var from = (fromDate ?? to.AddMonths(-5).AddDays(1 - to.Day)).Date;

            if (from > to)
            {
                var temp = from;
                from = to;
                to = temp;
            }

            var endExclusive = to.AddDays(1);

            var orders = await _context.orders
                .AsNoTracking()
                .Where(o =>
                    o.OrderDate >= from &&
                    o.OrderDate < endExclusive)
                .ToListAsync(cancellationToken);

            var orderIds = orders
                .Select(o => o.OrderId)
                .ToList();

            var orderItems = await _context.orderitems
                .AsNoTracking()
                .Where(oi => orderIds.Contains(oi.OrderId))
                .ToListAsync(cancellationToken);

            var farmerIds = orders
                .Select(o => o.FarmerId)
                .Distinct()
                .ToList();

            var farmers = await _context.farmers
                .AsNoTracking()
                .Where(f => farmerIds.Contains(f.FarmerId))
                .ToDictionaryAsync(
                    f => f.FarmerId,
                    cancellationToken);

            var farmerMarketIds = orders
                .Select(o => o.FarmerMarketId)
                .Distinct()
                .ToList();

            var farmerMarkets = await _context.farmermarkets
                .AsNoTracking()
                .Where(fm => farmerMarketIds.Contains(fm.FarmerMarketId))
                .ToDictionaryAsync(
                    fm => fm.FarmerMarketId,
                    cancellationToken);

            var marketIds = farmerMarkets.Values
                .Select(fm => fm.MarketId)
                .Distinct()
                .ToList();

            var markets = await _context.markets
                .AsNoTracking()
                .Where(m => marketIds.Contains(m.MarketId))
                .ToDictionaryAsync(
                    m => m.MarketId,
                    cancellationToken);

            var completedOrders = orders
                .Where(o => o.OrderStatus == "Completed")
                .ToList();

            var completedOrderIds = completedOrders
                .Select(o => o.OrderId)
                .ToHashSet();

            var revenue = completedOrders.Sum(o => o.TotalAmount);

            var statusMix = orders
                .GroupBy(o => o.OrderStatus)
                .Select(g => new AdminOrderStatusMetricViewModel
                {
                    Status = g.Key,
                    Count = g.Count(),
                    Percentage = orders.Count == 0
                        ? 0
                        : Math.Round(
                            (decimal)g.Count() / orders.Count * 100m,
                            1)
                })
                .OrderByDescending(x => x.Count)
                .ToList();

            var topFarmers = orders
                .GroupBy(o => o.FarmerId)
                .Select(g =>
                {
                    farmers.TryGetValue(g.Key, out var farmer);

                    var completed = g
                        .Where(o => o.OrderStatus == "Completed")
                        .ToList();

                    return new AdminFarmerPerformanceViewModel
                    {
                        FarmerId = g.Key,
                        FarmerName = farmer?.BusinessName ?? "Farmer",
                        OrderCount = g.Count(),
                        CompletedOrders = completed.Count,
                        Revenue = completed.Sum(o => o.TotalAmount)
                    };
                })
                .OrderByDescending(x => x.Revenue)
                .ThenByDescending(x => x.CompletedOrders)
                .Take(10)
                .ToList();

            var marketGroups = orders
                .Select(o =>
                {
                    int marketId = 0;
                    string marketName = "Market";

                    if (farmerMarkets.TryGetValue(
                            o.FarmerMarketId,
                            out var fm))
                    {
                        marketId = fm.MarketId;

                        if (markets.TryGetValue(
                            fm.MarketId,
                            out var market))
                        {
                            marketName = market.MarketName;
                        }
                    }

                    return new
                    {
                        Order = o,
                        MarketId = marketId,
                        MarketName = marketName
                    };
                })
                .GroupBy(x => new
                {
                    x.MarketId,
                    x.MarketName
                });

            var marketPerformance = marketGroups
                .Select(g =>
                {
                    var completed = g
                        .Where(x =>
                            x.Order.OrderStatus == "Completed")
                        .ToList();

                    return new AdminMarketPerformanceViewModel
                    {
                        MarketId = g.Key.MarketId,
                        MarketName = g.Key.MarketName,
                        OrderCount = g.Count(),
                        CompletedOrders = completed.Count,
                        Revenue = completed.Sum(x => x.Order.TotalAmount)
                    };
                })
                .OrderByDescending(x => x.Revenue)
                .ThenByDescending(x => x.OrderCount)
                .Take(10)
                .ToList();

            var topProducts = orderItems
                .Where(oi => completedOrderIds.Contains(oi.OrderId))
                .GroupBy(oi => oi.ProductName)
                .Select(g => new AdminProductPerformanceViewModel
                {
                    ProductName = g.Key,
                    QuantitySold = g.Sum(x => x.Quantity),
                    Revenue = g.Sum(x => x.TotalPrice),
                    OrderCount = g
                        .Select(x => x.OrderId)
                        .Distinct()
                        .Count()
                })
                .OrderByDescending(x => x.Revenue)
                .ThenByDescending(x => x.QuantitySold)
                .Take(10)
                .ToList();

            var monthly = orders
                .GroupBy(o => new
                {
                    o.OrderDate.Year,
                    o.OrderDate.Month
                })
                .Select(g =>
                {
                    var completed = g
                        .Where(o => o.OrderStatus == "Completed")
                        .ToList();

                    return new AdminMonthlyPerformanceViewModel
                    {
                        MonthLabel =
                            new DateTime(
                                g.Key.Year,
                                g.Key.Month,
                                1)
                            .ToString("MMM yyyy"),
                        OrderCount = g.Count(),
                        CompletedOrders = completed.Count,
                        Revenue = completed.Sum(o => o.TotalAmount)
                    };
                })
                .OrderBy(x =>
                    DateTime.ParseExact(
                        x.MonthLabel,
                        "MMM yyyy",
                        System.Globalization.CultureInfo.InvariantCulture))
                .ToList();

            return View(new AdminReportsPageViewModel
            {
                FromDate = from,
                ToDate = to,
                TotalOrders = orders.Count,
                CompletedOrders = completedOrders.Count,
                CancelledOrders = orders.Count(o =>
                    o.OrderStatus == "Cancelled"),
                ActiveFarmers = orders
                    .Select(o => o.FarmerId)
                    .Distinct()
                    .Count(),
                Revenue = revenue,
                AverageOrderValue =
                    completedOrders.Count == 0
                        ? 0
                        : revenue / completedOrders.Count,
                OrderStatusMix = statusMix,
                TopFarmers = topFarmers,
                MarketPerformance = marketPerformance,
                TopProducts = topProducts,
                MonthlyPerformance = monthly
            });
        }

        [HttpGet]
        public async Task<IActionResult> ExportCsv(
            DateTime? fromDate,
            DateTime? toDate,
            CancellationToken cancellationToken)
        {
            var to = (toDate ?? DateTime.Today).Date;
            var from = (fromDate ?? to.AddMonths(-5).AddDays(1 - to.Day)).Date;

            if (from > to)
            {
                var temp = from;
                from = to;
                to = temp;
            }

            var endExclusive = to.AddDays(1);

            var orders = await _context.orders
                .AsNoTracking()
                .Where(o =>
                    o.OrderDate >= from &&
                    o.OrderDate < endExclusive)
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync(cancellationToken);

            var farmerIds = orders
                .Select(o => o.FarmerId)
                .Distinct()
                .ToList();

            var farmers = await _context.farmers
                .AsNoTracking()
                .Where(f => farmerIds.Contains(f.FarmerId))
                .ToDictionaryAsync(
                    f => f.FarmerId,
                    cancellationToken);

            var farmerMarketIds = orders
                .Select(o => o.FarmerMarketId)
                .Distinct()
                .ToList();

            var farmerMarkets = await _context.farmermarkets
                .AsNoTracking()
                .Where(fm => farmerMarketIds.Contains(fm.FarmerMarketId))
                .ToDictionaryAsync(
                    fm => fm.FarmerMarketId,
                    cancellationToken);

            var marketIds = farmerMarkets.Values
                .Select(fm => fm.MarketId)
                .Distinct()
                .ToList();

            var markets = await _context.markets
                .AsNoTracking()
                .Where(m => marketIds.Contains(m.MarketId))
                .ToDictionaryAsync(
                    m => m.MarketId,
                    cancellationToken);

            static string Csv(string? value)
            {
                value ??= "";
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            var sb = new StringBuilder();

            sb.AppendLine(
                "OrderNo,OrderDate,PickupDate,Status,Farmer,Market,TotalAmount");

            foreach (var order in orders)
            {
                farmers.TryGetValue(order.FarmerId, out var farmer);

                string marketName = "";

                if (farmerMarkets.TryGetValue(
                        order.FarmerMarketId,
                        out var fm) &&
                    markets.TryGetValue(
                        fm.MarketId,
                        out var market))
                {
                    marketName = market.MarketName;
                }

                sb.AppendLine(string.Join(",",
                    Csv(order.OrderNo),
                    Csv(order.OrderDate.ToString("yyyy-MM-dd HH:mm")),
                    Csv(order.PickupDate.ToString("yyyy-MM-dd")),
                    Csv(order.OrderStatus),
                    Csv(farmer?.BusinessName ?? ""),
                    Csv(marketName),
                    order.TotalAmount.ToString(
                        "0.00",
                        System.Globalization.CultureInfo.InvariantCulture)));
            }

            var fileName =
                $"MarketLink_Admin_Report_{from:yyyyMMdd}_{to:yyyyMMdd}.csv";

            return File(
                Encoding.UTF8.GetBytes(sb.ToString()),
                "text/csv",
                fileName);
        }
    }
}
