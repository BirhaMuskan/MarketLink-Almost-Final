document.addEventListener("DOMContentLoaded", function () {

    // =========================================================
    // ELEMENTS
    // =========================================================

    const cartItemsContainer = document.getElementById("cartItems");
    const emptyCart = document.getElementById("emptyCart");
    const cartBottomActions = document.getElementById("cartBottomActions");

    const cartCount = document.getElementById("cartCount");
    const summaryItems = document.getElementById("summaryItems");

    const subtotalElement = document.getElementById("subtotal");
    const grandTotalElement = document.getElementById("grandTotal");

    const clearCartBtn = document.getElementById("clearCartBtn");
    const checkoutBtn = document.getElementById("checkoutBtn");

    const tokenInput =
        document.querySelector("#cartAntiForgeryForm input[name='__RequestVerificationToken']");


    // =========================================================
    // ANTI FORGERY TOKEN
    // =========================================================

    function getToken() {

        if (!tokenInput) {
            return "";
        }

        return tokenInput.value;
    }


    // =========================================================
    // FORMAT CURRENCY
    // =========================================================

    function formatCurrency(value) {

        return "Rs. " + Number(value).toLocaleString("en-PK", {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        });

    }


    // =========================================================
    // GET CART ITEMS
    // =========================================================

    function getCartItems() {

        return Array.from(
            document.querySelectorAll(".cart-item")
        );

    }


    // =========================================================
    // CALCULATE CART
    // =========================================================

    function calculateCart() {

        const items = getCartItems();

        let totalQuantity = 0;
        let subtotal = 0;

        items.forEach(item => {

            const price =
                parseFloat(item.dataset.price) || 0;

            const quantityElement =
                item.querySelector(".quantity-value");

            const quantity =
                parseFloat(quantityElement.textContent) || 0;

            const itemTotal =
                price * quantity;

            const totalElement =
                item.querySelector(".item-total-value");

            if (totalElement) {

                totalElement.textContent =
                    formatCurrency(itemTotal);

            }

            totalQuantity += quantity;

            subtotal += itemTotal;

        });


        // =====================================================
        // UPDATE SUMMARY
        // =====================================================

        if (cartCount) {

            cartCount.textContent =
                formatNumber(totalQuantity);

        }

        if (summaryItems) {

            summaryItems.textContent =
                formatNumber(totalQuantity);

        }

        if (subtotalElement) {

            subtotalElement.textContent =
                formatCurrency(subtotal);

        }

        if (grandTotalElement) {

            grandTotalElement.textContent =
                formatCurrency(subtotal);

        }


        // =====================================================
        // EMPTY / NOT EMPTY
        // =====================================================

        if (items.length === 0) {

            showEmptyCart();

        }
        else {

            showCart();

        }

    }


    // =========================================================
    // FORMAT NUMBER
    // =========================================================

    function formatNumber(value) {

        return Number(value).toLocaleString("en-PK", {
            maximumFractionDigits: 3
        });

    }


    // =========================================================
    // SHOW EMPTY CART
    // =========================================================

    function showEmptyCart() {

        if (emptyCart) {
            emptyCart.style.display = "block";
        }

        if (cartBottomActions) {
            cartBottomActions.style.display = "none";
        }

        if (checkoutBtn) {

            checkoutBtn.style.pointerEvents = "none";
            checkoutBtn.style.opacity = "0.5";

        }

    }


    // =========================================================
    // SHOW CART
    // =========================================================

    function showCart() {

        if (emptyCart) {
            emptyCart.style.display = "none";
        }

        if (cartBottomActions) {
            cartBottomActions.style.display = "flex";
        }

        if (checkoutBtn) {

            checkoutBtn.style.pointerEvents = "auto";
            checkoutBtn.style.opacity = "1";

        }

    }


    // =========================================================
    // UPDATE SERVER QUANTITY
    // =========================================================

    async function updateQuantity(cartItemId, quantity) {

        const formData = new FormData();

        formData.append(
            "cartItemId",
            cartItemId
        );

        formData.append(
            "quantity",
            quantity
        );

        formData.append(
            "__RequestVerificationToken",
            getToken()
        );


        try {

            const response = await fetch(
                "/Cart/UpdateQuantity",
                {
                    method: "POST",
                    body: formData
                }
            );


            if (!response.ok) {

                throw new Error(
                    "Unable to update cart."
                );

            }


            const result =
                await response.json();


            if (!result.success) {

                showMessage(
                    result.message ||
                    "Unable to update cart."
                );

                return false;

            }

            return true;

        }
        catch (error) {

            console.error(error);

            showMessage(
                "Something went wrong while updating the cart."
            );

            return false;

        }

    }


    // =========================================================
    // REMOVE ITEM
    // =========================================================

    async function removeItem(cartItem) {

        const cartItemId =
            cartItem.dataset.id;


        const formData = new FormData();

        formData.append(
            "cartItemId",
            cartItemId
        );

        formData.append(
            "__RequestVerificationToken",
            getToken()
        );


        try {

            const response = await fetch(
                "/Cart/RemoveItem",
                {
                    method: "POST",
                    body: formData
                }
            );


            const result =
                await response.json();


            if (!result.success) {

                showMessage(
                    result.message ||
                    "Unable to remove item."
                );

                return;

            }


            // Remove from page

            cartItem.remove();


            calculateCart();

        }
        catch (error) {

            console.error(error);

            showMessage(
                "Something went wrong while removing the item."
            );

        }

    }


    // =========================================================
    // CLEAR CART
    // =========================================================

    async function clearCart() {

        const items =
            getCartItems();


        if (items.length === 0) {
            return;
        }


        const confirmation =
            confirm(
                "Are you sure you want to clear your cart?"
            );


        if (!confirmation) {
            return;
        }


        const formData = new FormData();

        formData.append(
            "__RequestVerificationToken",
            getToken()
        );


        try {

            const response = await fetch(
                "/Cart/ClearCart",
                {
                    method: "POST",
                    body: formData
                }
            );


            const result =
                await response.json();


            if (!result.success) {

                showMessage(
                    result.message ||
                    "Unable to clear cart."
                );

                return;

            }


            // Remove all items

            items.forEach(item => {
                item.remove();
            });


            calculateCart();

        }
        catch (error) {

            console.error(error);

            showMessage(
                "Something went wrong while clearing the cart."
            );

        }

    }


    // =========================================================
    // PLUS / MINUS BUTTONS
    // =========================================================

    document.addEventListener(
        "click",
        async function (event) {

            const plusButton =
                event.target.closest(".plus-btn");

            const minusButton =
                event.target.closest(".minus-btn");

            const removeButton =
                event.target.closest(".remove-item");


            // =================================================
            // REMOVE
            // =================================================

            if (removeButton) {

                const cartItem =
                    removeButton.closest(".cart-item");

                if (cartItem) {

                    await removeItem(cartItem);

                }

                return;

            }


            // =================================================
            // PLUS / MINUS
            // =================================================

            if (!plusButton && !minusButton) {
                return;
            }


            const cartItem =
                (plusButton || minusButton)
                    .closest(".cart-item");


            if (!cartItem) {
                return;
            }


            const quantityElement =
                cartItem.querySelector(".quantity-value");


            let quantity =
                parseFloat(
                    quantityElement.textContent
                ) || 0;


            // =================================================
            // CHANGE
            // =================================================

            if (plusButton) {

                quantity += 1;

            }
            else if (minusButton) {

                quantity -= 1;

            }


            // Prevent negative quantity

            if (quantity < 0) {
                quantity = 0;
            }


            const cartItemId =
                cartItem.dataset.id;


            // =================================================
            // SERVER UPDATE
            // =================================================

            const success =
                await updateQuantity(
                    cartItemId,
                    quantity
                );


            if (!success) {
                return;
            }


            // =================================================
            // ZERO = REMOVE
            // =================================================

            if (quantity === 0) {

                cartItem.remove();

                calculateCart();

                return;

            }


            quantityElement.textContent =
                formatNumber(quantity);


            calculateCart();

        }
    );


    // =========================================================
    // CLEAR CART
    // =========================================================

    if (clearCartBtn) {

        clearCartBtn.addEventListener(
            "click",
            clearCart
        );

    }


    // =========================================================
    // MESSAGE
    // =========================================================

    function showMessage(message) {

        alert(message);

    }


    // =========================================================
    // INITIAL CALCULATION
    // =========================================================

    calculateCart();

});