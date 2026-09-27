
(function () {
    const root = document.getElementById("mlAssistantWidget");
    if (!root || root.dataset.initialized === "true") return;

    root.dataset.initialized = "true";

    const launcher = document.getElementById("mlAssistantLauncher");
    const panel = document.getElementById("mlAssistantPanel");
    const close = document.getElementById("mlAssistantClose");
    const form = document.getElementById("mlAssistantForm");
    const input = document.getElementById("mlAssistantInput");
    const send = document.getElementById("mlAssistantSend");
    const messages = document.getElementById("mlAssistantMessages");

    function setOpen(open) {
        panel.classList.toggle("open", open);
        panel.setAttribute("aria-hidden", open ? "false" : "true");
        launcher.setAttribute("aria-expanded", open ? "true" : "false");

        if (open) {
            setTimeout(function () {
                input?.focus();
                scrollBottom();
            }, 40);
        }
    }

    function scrollBottom() {
        if (messages) {
            messages.scrollTop = messages.scrollHeight;
        }
    }

    function addMessage(role, text) {
        const wrap = document.createElement("div");
        wrap.className = "ml-widget-message " + role;

        const bubble = document.createElement("div");
        bubble.className = "ml-widget-bubble";
        bubble.textContent = text;

        wrap.appendChild(bubble);
        messages.appendChild(wrap);
        scrollBottom();
    }

    function addActions(actions) {
        if (!actions || actions.length === 0) return;

        const box = document.createElement("div");
        box.className = "ml-assistant-actions";

        actions.forEach(function (action) {
            if (!action.url) return;

            const link = document.createElement("a");
            link.href = action.url;
            link.textContent = action.label || "Open";
            box.appendChild(link);
        });

        messages.appendChild(box);
        scrollBottom();
    }

    function addTyping() {
        const typing = document.createElement("div");
        typing.className = "ml-assistant-typing";
        typing.id = "mlAssistantTyping";
        typing.textContent = "MarketLink is checking live data...";
        messages.appendChild(typing);
        scrollBottom();
    }

    function removeTyping() {
        document.getElementById("mlAssistantTyping")?.remove();
    }

    async function ask(question) {
        question = (question || "").trim();
        if (!question) return;

        addMessage("user", question);
        input.value = "";
        send.disabled = true;
        addTyping();

        const token =
            form.querySelector('input[name="__RequestVerificationToken"]')?.value || "";

        const body = new URLSearchParams();
        body.append("question", question);
        body.append("__RequestVerificationToken", token);

        try {
            const response = await fetch("/AssistantWidget/Ask", {
                method: "POST",
                headers: {
                    "Content-Type": "application/x-www-form-urlencoded;charset=UTF-8"
                },
                body: body.toString()
            });

            const data = await response.json();
            removeTyping();

            if (!response.ok || !data.ok) {
                addMessage(
                    "assistant",
                    data.message || "I could not process that question right now."
                );
                return;
            }

            addMessage("assistant", data.message);
            addActions(data.actions);
        }
        catch (error) {
            removeTyping();
            addMessage(
                "assistant",
                "I could not reach the MarketLink assistant service. Please try again."
            );
        }
        finally {
            send.disabled = false;
            input.focus();
        }
    }

    launcher?.addEventListener("click", function () {
        setOpen(!panel.classList.contains("open"));
    });

    close?.addEventListener("click", function () {
        setOpen(false);
    });

    form?.addEventListener("submit", function (event) {
        event.preventDefault();
        ask(input.value);
    });

    document.querySelectorAll(
        "#mlAssistantWidget .ml-assistant-suggestions button"
    ).forEach(function (button) {
        button.addEventListener("click", function () {
            setOpen(true);
            ask(button.dataset.question || "");
        });
    });
})();
