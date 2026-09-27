
(function () {
    const thread = document.getElementById("assistantThread");
    const input = document.getElementById("assistantQuestion");

    function scrollToBottom() {
        if (thread) {
            thread.scrollTop = thread.scrollHeight;
        }
    }

    function resizeInput() {
        if (!input) return;
        input.style.height = "auto";
        input.style.height =
            Math.min(input.scrollHeight, 120) + "px";
    }

    document.querySelectorAll(
        ".assistant-prompt-chip"
    ).forEach(function (button) {
        button.addEventListener("click", function () {
            if (!input) return;
            input.value =
                button.dataset.question || "";
            resizeInput();
            input.focus();
        });
    });

    input?.addEventListener(
        "input",
        resizeInput
    );

    input?.addEventListener(
        "keydown",
        function (event) {
            if (event.key === "Enter" &&
                !event.shiftKey) {
                event.preventDefault();
                input.closest("form")?.requestSubmit();
            }
        }
    );

    resizeInput();
    scrollToBottom();
})();
