/* =========================================================
   MARKETLINK CART JAVASCRIPT
========================================================= */

document.addEventListener("DOMContentLoaded", function () {

    /* =====================================================
       ELEMENTS
    ====================================================== */

    const cartItemsContainer =
        document.getElementById("cartItems");

    const emptyCart =
        document.getElementById("emptyCart");

    const cartBottomActions =
        document.getElementById("cartBottomActions");

    const cartSummaryCard =
        document.getElementById("cartSummaryCard");

    const cartCount =
        document.getElementById("cartCount");

    const summaryItems =
        document.getElementById("summaryItems");

    const subtotal =
        document.getElementById("subtotal");

    const grandTotal =
        document.getElementById("grandTotal");

    const clearCartBtn =
        document.getElementById("clearCartBtn");

    const checkoutBtn =
        document.getElementById("checkoutBtn");


    /* =====================================================
       FORMAT PRICE
    ====================================================== */

    function formatPrice(value) {

        return "Rs. " + value.toLocaleString("en-PK");

    }


    /* =====================================================
       GET CART ITEMS
    ====================================================== */

    function getCartItems() {

        if (!cartItemsContainer) {
            return [];
        }

        return Array.from(
            cartItemsContainer.querySelectorAll(".cart-item")
        );

    }


    /* =====================================================
       UPDATE ITEM TOTAL
    ====================================================== */

    function updateItemTotal(item) {

        const price =
            parseFloat(item.dataset.price) || 0;

        const quantityElement =
            item.querySelector(".quantity-value");

        const totalElement =
            item.querySelector(".item-total-value");

        if (!quantityElement || !totalElement) {
            return;
        }

        const quantity =
            parseInt(quantityElement.textContent) || 1;

        const total =
            price * quantity;

        totalElement.textContent =
            formatPrice(total);

    }


    /* =====================================================
       UPDATE CART SUMMARY
    ====================================================== */

    function updateCartSummary() {

        const items =
            getCartItems();

        let totalQuantity = 0;
        let totalPrice = 0;


        items.forEach(function (item) {

            const price =
                parseFloat(item.dataset.price) || 0;

            const quantityElement =
                item.querySelector(".quantity-value");

            const quantity =
                parseInt(quantityElement.textContent) || 1;


            totalQuantity += quantity;

            totalPrice +=
                price * quantity;


            updateItemTotal(item);

        });


        /* Update count */

        if (cartCount) {

            cartCount.textContent =
                totalQuantity;

        }


        /* Update summary item count */

        if (summaryItems) {

            summaryItems.textContent =
                totalQuantity;

        }


        /* Update subtotal */

        if (subtotal) {

            subtotal.textContent =
                formatPrice(totalPrice);

        }


        /* Update grand total */

        if (grandTotal) {

            grandTotal.textContent =
                formatPrice(totalPrice);

        }


        /* Empty cart */

        if (items.length === 0) {

            if (emptyCart) {
                emptyCart.style.display = "block";
            }

            if (cartBottomActions) {
                cartBottomActions.style.display = "none";
            }

            if (cartSummaryCard) {
                cartSummaryCard.style.display = "none";
            }

        }
        else {

            if (emptyCart) {
                emptyCart.style.display = "none";
            }

            if (cartBottomActions) {
                cartBottomActions.style.display = "flex";
            }

            if (cartSummaryCard) {
                cartSummaryCard.style.display = "block";
            }

        }

    }


    /* =====================================================
       INCREASE / DECREASE QUANTITY
    ====================================================== */

    if (cartItemsContainer) {

        cartItemsContainer.addEventListener(
            "click",
            function (event) {

                /* =========================================
                   PLUS BUTTON
                ========================================== */

                const plusButton =
                    event.target.closest(".plus-btn");

                if (plusButton) {

                    const item =
                        plusButton.closest(".cart-item");

                    if (!item) {
                        return;
                    }


                    const quantityElement =
                        item.querySelector(".quantity-value");

                    if (!quantityElement) {
                        return;
                    }


                    let quantity =
                        parseInt(
                            quantityElement.textContent
                        ) || 1;


                    /* Maximum quantity */

                    if (quantity < 99) {

                        quantity++;

                        quantityElement.textContent =
                            quantity;

                    }


                    updateItemTotal(item);

                    updateCartSummary();

                    return;
                }


                /* =========================================
                   MINUS BUTTON
                ========================================== */

                const minusButton =
                    event.target.closest(".minus-btn");

                if (minusButton) {

                    const item =
                        minusButton.closest(".cart-item");

                    if (!item) {
                        return;
                    }


                    const quantityElement =
                        item.querySelector(".quantity-value");

                    if (!quantityElement) {
                        return;
                    }


                    let quantity =
                        parseInt(
                            quantityElement.textContent
                        ) || 1;


                    /* Minimum quantity = 1 */

                    if (quantity > 1) {

                        quantity--;

                        quantityElement.textContent =
                            quantity;

                    }


                    updateItemTotal(item);

                    updateCartSummary();

                    return;
                }


                /* =========================================
                   REMOVE ITEM
                ========================================== */

                const removeButton =
                    event.target.closest(".remove-item");

                if (removeButton) {

                    const item =
                        removeButton.closest(".cart-item");

                    if (!item) {
                        return;
                    }


                    item.classList.add("removing");


                    setTimeout(function () {

                        item.remove();

                        updateCartSummary();

                    }, 250);

                }

            }
        );

    }


    /* =====================================================
       CLEAR CART
    ====================================================== */

    if (clearCartBtn) {

        clearCartBtn.addEventListener(
            "click",
            function () {

                const items =
                    getCartItems();


                if (items.length === 0) {
                    return;
                }


                items.forEach(function (item) {

                    item.classList.add("removing");

                });


                setTimeout(function () {

                    items.forEach(function (item) {

                        item.remove();

                    });

                    updateCartSummary();

                }, 250);

            }
        );

    }


    /* =====================================================
       CHECKOUT / PRE-ORDER
    ====================================================== */

    if (checkoutBtn) {

        checkoutBtn.addEventListener(
            "click",
            function () {

                const items =
                    getCartItems();


                if (items.length === 0) {

                    return;

                }


                /*
                 * Abhi frontend/static stage hai.
                 * Backend Order/Pre-Order complete hone
                 * ke baad isko actual checkout route
                 * par redirect karenge.
                 */

                window.location.href =
                    "/Orders/Create";

            }
        );

    }


    /* =====================================================
       INITIAL CALCULATION
    ====================================================== */

    getCartItems().forEach(function (item) {

        updateItemTotal(item);

    });


    updateCartSummary();

});