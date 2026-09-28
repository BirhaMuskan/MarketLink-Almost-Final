using System.Security.Claims;
using MarketLink.Models;
using MarketLink.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLink.Controllers
{
    [Authorize(Roles = "CUSTOMER")]
    public class CartController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CartController(ApplicationDbContext context)
        {
            _context = context;
        }


        // =========================================================
        // GET: /Cart/Index
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            int customerId = GetCustomerId();

            if (customerId <= 0)
            {
                return RedirectToAction("Login", "Auth");
            }

            var cart = await _context.carts
                .Include(c => c.Farmer)
                .Include(c => c.FarmerMarket)
                .Include(c => c.Customer)
                .FirstOrDefaultAsync(c =>
                    c.CustomerId == customerId &&
                    c.IsActive);

            var viewModel = new CartViewModel
            {
                Cart = cart
            };

            if (cart == null)
            {
                return View(viewModel);
            }

            var cartItems = await _context.cartitems
                .Include(ci => ci.FarmerProduct)
                    .ThenInclude(fp => fp!.Product)
                        .ThenInclude(p => p!.Category)
                .Include(ci => ci.FarmerProduct)
                    .ThenInclude(fp => fp!.UnitOfMeasure)
                .Where(ci => ci.CartId == cart.CartId)
                .OrderBy(ci => ci.AddedAt)
                .ToListAsync();

            foreach (var item in cartItems)
            {
                if (item.FarmerProduct == null ||
                    item.FarmerProduct.Product == null)
                {
                    continue;
                }

                viewModel.Items.Add(new CartItemViewModel
                {
                    CartItemId = item.CartItemId,

                    FarmerProductId = item.FarmerProductId,

                    ProductName =
                        item.FarmerProduct.Product.ProductName,

                    ProductDescription =
                        item.FarmerProduct.Product.Description,

                    CategoryName =
                        item.FarmerProduct.Product.Category?.CategoryName
                        ?? "Fresh Produce",

                    ImageUrl =
                        item.FarmerProduct.Product.DefaultImageUrl,

                    UnitName =
                        item.FarmerProduct.UnitOfMeasure?.UnitName,

                    Quantity = item.Quantity,

                    UnitPrice = item.UnitPrice,

                    FarmerId =
                        item.FarmerProduct.FarmerId,

                    FarmerMarketId =
                        cart.FarmerMarketId
                });
            }

            viewModel.TotalQuantity =
                viewModel.Items.Sum(x => x.Quantity);

            viewModel.Subtotal =
                viewModel.Items.Sum(x => x.TotalPrice);

            viewModel.GrandTotal =
                viewModel.Subtotal;

            return View(viewModel);
        }


        // =========================================================
        // POST: /Cart/AddToCart
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddToCart(
            int farmerProductId,
            int farmerMarketId,
            decimal quantity = 1)
        {
            int customerId = GetCustomerId();

            if (customerId <= 0)
            {
                return Json(new
                {
                    success = false,
                    message = "Please login as a customer first.",
                    redirect = Url.Action("Login", "Auth")
                });
            }

            if (quantity <= 0)
            {
                return Json(new
                {
                    success = false,
                    message = "Quantity must be greater than zero."
                });
            }


            // =====================================================
            // GET FARMER PRODUCT
            // =====================================================

            var farmerProduct = await _context.farmerproducts
                .Include(fp => fp.Product)
                .FirstOrDefaultAsync(fp =>
                    fp.FarmerProductId == farmerProductId &&
                    fp.IsAvailable &&
                    fp.IsApproved &&
                    fp.IsActive);

            if (farmerProduct == null)
            {
                return Json(new
                {
                    success = false,
                    message = "This product is currently unavailable."
                });
            }


            // =====================================================
            // CHECK CART
            // =====================================================

            var cart = await _context.carts
                .FirstOrDefaultAsync(c =>
                    c.CustomerId == customerId &&
                    c.FarmerId == farmerProduct.FarmerId &&
                    c.FarmerMarketId == farmerMarketId &&
                    c.IsActive);


            // =====================================================
            // CREATE CART IF NOT EXISTS
            // =====================================================

            if (cart == null)
            {
                cart = new Cart
                {
                    CustomerId = customerId,
                    FarmerId = farmerProduct.FarmerId,
                    FarmerMarketId = farmerMarketId,
                    IsActive = true,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                _context.carts.Add(cart);

                await _context.SaveChangesAsync();
            }


            // =====================================================
            // CHECK EXISTING CART ITEM
            // =====================================================

            var existingItem = await _context.cartitems
                .FirstOrDefaultAsync(ci =>
                    ci.CartId == cart.CartId &&
                    ci.FarmerProductId == farmerProductId);


            if (existingItem != null)
            {
                existingItem.Quantity += quantity;

                // Keep latest farmer price
                existingItem.UnitPrice = farmerProduct.Price;
            }
            else
            {
                var cartItem = new CartItem
                {
                    CartId = cart.CartId,
                    FarmerProductId = farmerProductId,
                    Quantity = quantity,
                    UnitPrice = farmerProduct.Price,
                    AddedAt = DateTime.Now
                };

                _context.cartitems.Add(cartItem);
            }


            cart.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();


            // =====================================================
            // GET CART COUNT
            // =====================================================

            var cartCount = await _context.cartitems
                .Where(ci => ci.CartId == cart.CartId)
                .SumAsync(ci => ci.Quantity);


            return Json(new
            {
                success = true,
                message = "Product added to cart successfully.",
                cartCount = cartCount
            });
        }


        // =========================================================
        // POST: /Cart/UpdateQuantity
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateQuantity(
            int cartItemId,
            decimal quantity)
        {
            int customerId = GetCustomerId();

            if (customerId <= 0)
            {
                return Json(new
                {
                    success = false,
                    message = "Please login first."
                });
            }

            var cartItem = await _context.cartitems
                .Include(ci => ci.Cart)
                .FirstOrDefaultAsync(ci =>
                    ci.CartItemId == cartItemId &&
                    ci.Cart!.CustomerId == customerId &&
                    ci.Cart.IsActive);

            if (cartItem == null)
            {
                return Json(new
                {
                    success = false,
                    message = "Cart item not found."
                });
            }


            // =====================================================
            // IF QUANTITY IS ZERO, REMOVE ITEM
            // =====================================================

            if (quantity <= 0)
            {
                _context.cartitems.Remove(cartItem);

                cartItem.Cart!.UpdatedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    removed = true
                });
            }


            cartItem.Quantity = quantity;

            cartItem.Cart!.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();


            return Json(new
            {
                success = true,
                removed = false,
                quantity = quantity
            });
        }


        // =========================================================
        // POST: /Cart/RemoveItem
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveItem(int cartItemId)
        {
            int customerId = GetCustomerId();

            if (customerId <= 0)
            {
                return Json(new
                {
                    success = false,
                    message = "Please login first."
                });
            }

            var cartItem = await _context.cartitems
                .Include(ci => ci.Cart)
                .FirstOrDefaultAsync(ci =>
                    ci.CartItemId == cartItemId &&
                    ci.Cart!.CustomerId == customerId &&
                    ci.Cart.IsActive);

            if (cartItem == null)
            {
                return Json(new
                {
                    success = false,
                    message = "Cart item not found."
                });
            }

            var cart = cartItem.Cart;

            _context.cartitems.Remove(cartItem);

            if (cart != null)
            {
                cart.UpdatedAt = DateTime.Now;
            }

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = "Item removed from cart."
            });
        }


        // =========================================================
        // POST: /Cart/ClearCart
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearCart()
        {
            int customerId = GetCustomerId();

            if (customerId <= 0)
            {
                return Json(new
                {
                    success = false,
                    message = "Please login first."
                });
            }

            var cart = await _context.carts
                .FirstOrDefaultAsync(c =>
                    c.CustomerId == customerId &&
                    c.IsActive);

            if (cart == null)
            {
                return Json(new
                {
                    success = true
                });
            }

            var items = await _context.cartitems
                .Where(ci => ci.CartId == cart.CartId)
                .ToListAsync();

            if (items.Any())
            {
                _context.cartitems.RemoveRange(items);
            }

            cart.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = "Cart cleared successfully."
            });
        }


        // =========================================================
        // GET CUSTOMER ID FROM LOGIN CLAIM
        // =========================================================

        private int GetCustomerId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(claim))
            {
                return 0;
            }

            return int.TryParse(claim, out int customerId)
                ? customerId
                : 0;
        }
    }
}