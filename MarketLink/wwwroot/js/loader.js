document.addEventListener("DOMContentLoaded", function () {

    const loader = document.getElementById("marketLinkLoader");

    if (!loader) {
        return;
    }

    setTimeout(function () {

        loader.classList.add("loader-hidden");

    }, 700);

});