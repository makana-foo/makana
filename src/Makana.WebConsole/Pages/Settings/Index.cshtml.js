(() => {
    const state = window.makanaSettingsTheme ?? (window.makanaSettingsTheme = {});
    if (state.initialized) {
        return;
    }

    state.initialized = true;

    const themeConfig = window.makanaThemeConfig ?? {};
    const makanaTheme = window.makanaTheme ?? {};

    function getCurrentThemeFamily() {
        return document.documentElement.getAttribute("data-theme-family")
            ?? themeConfig.initialTheme
            ?? themeConfig.defaultTheme
            ?? "default";
    }

    function syncThemeSelectors(root = document) {
        const queryRoot = root instanceof Element || root instanceof Document ? root : document;
        const currentTheme = getCurrentThemeFamily();
        for (const select of queryRoot.querySelectorAll("[data-theme-select]")) {
            if (!(select instanceof HTMLSelectElement)) {
                continue;
            }

            if (Array.from(select.options).some((option) => option.value === currentTheme)) {
                select.value = currentTheme;
            }
        }
    }

    function updateThemeFeedback(root, message, stateValue = "idle") {
        const queryRoot = root instanceof Element || root instanceof Document ? root : document;
        for (const feedback of queryRoot.querySelectorAll("[data-theme-feedback]")) {
            feedback.textContent = message;
            feedback.setAttribute("data-state", stateValue);
        }
    }

    async function persistThemeSelection(select) {
        if (!(select instanceof HTMLSelectElement)) {
            return false;
        }

        const root = select.closest(".settings-page") ?? document;
        const previousThemeFamily = getCurrentThemeFamily();
        const nextThemeFamily = select.value;

        if (typeof makanaTheme.set === "function" && makanaTheme.set(nextThemeFamily) === false) {
            return false;
        }

        updateThemeFeedback(root, "Saving theme preference...", "saving");

        if (typeof themeConfig.updateUrl !== "string" || themeConfig.updateUrl.length === 0) {
            themeConfig.initialTheme = nextThemeFamily;
            syncThemeSelectors(root);
            updateThemeFeedback(root, "Theme changes apply immediately and are saved to your account.");
            return true;
        }

        try {
            const response = await fetch(themeConfig.updateUrl, {
                method: "PUT",
                credentials: "same-origin",
                headers: {
                    "Content-Type": "application/json",
                    Accept: "application/json"
                },
                body: JSON.stringify({ theme: nextThemeFamily })
            });

            if (!response.ok) {
                throw new Error(`Theme update failed: ${response.status}`);
            }

            themeConfig.initialTheme = nextThemeFamily;
            syncThemeSelectors(root);
            updateThemeFeedback(root, "Theme changes apply immediately and are saved to your account.");
            return true;
        } catch (error) {
            if (typeof makanaTheme.set === "function") {
                makanaTheme.set(previousThemeFamily);
            }

            syncThemeSelectors(root);
            updateThemeFeedback(root, "Unable to save your theme preference. Try again.", "error");
            console.error("Failed to update theme preference.", error);
            return false;
        }
    }

    function initialize(root = document) {
        syncThemeSelectors(root);
    }

    document.addEventListener("change", (event) => {
        const select = event.target instanceof HTMLSelectElement && event.target.matches("[data-theme-select]")
            ? event.target
            : null;
        if (!select) {
            return;
        }

        void persistThemeSelection(select);
    });

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", () => {
            initialize(document);
        }, { once: true });
    } else {
        initialize(document);
    }

    document.addEventListener("htmx:load", (event) => {
        initialize(event.target);
    });
})();
