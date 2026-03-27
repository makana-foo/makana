(() => {
    const darkQuery = window.matchMedia("(prefers-color-scheme: dark)");
    const themeConfig = window.makanaThemeConfig || {
        initialTheme: "default",
        defaultTheme: "default",
        defaultStylesheetPath: "/css/themes/default.css",
        availableThemes: [],
        updateUrl: null,
        modeStorageKey: "makana:theme-mode"
    };
    const themeModeStorageKey = typeof themeConfig.modeStorageKey === "string" && themeConfig.modeStorageKey.length > 0
        ? themeConfig.modeStorageKey
        : "makana:theme-mode";

    function getThemeOptions() {
        return Array.isArray(themeConfig.availableThemes) ? themeConfig.availableThemes : [];
    }

    function getThemeOption(theme) {
        if (typeof theme !== "string") {
            return null;
        }

        return getThemeOptions().find((option) => option?.id === theme) ?? null;
    }

    function getStoredThemeMode() {
        try {
            const mode = window.localStorage.getItem(themeModeStorageKey);
            return mode === "light" || mode === "dark" ? mode : null;
        } catch {
            return null;
        }
    }

    function setStoredThemeMode(mode) {
        try {
            if (mode === "light" || mode === "dark") {
                window.localStorage.setItem(themeModeStorageKey, mode);
            } else {
                window.localStorage.removeItem(themeModeStorageKey);
            }
        } catch {
            // Ignore storage failures; the mode still applies for the current page.
        }
    }

    function resolveDefaultThemeMode() {
        return darkQuery.matches ? "dark" : "light";
    }

    function resolveInitialThemeFamily() {
        return typeof themeConfig.initialTheme === "string" && themeConfig.initialTheme.length > 0
            ? themeConfig.initialTheme
            : (typeof themeConfig.defaultTheme === "string" && themeConfig.defaultTheme.length > 0
                ? themeConfig.defaultTheme
                : "default");
    }

    function resolveInitialThemeMode() {
        return getStoredThemeMode() ?? resolveDefaultThemeMode();
    }

    function getEffectiveThemeFamily() {
        return document.documentElement.getAttribute("data-theme-family") ?? resolveInitialThemeFamily();
    }

    function getEffectiveThemeMode() {
        const mode = document.documentElement.getAttribute("data-theme-mode");
        return mode === "light" || mode === "dark" ? mode : resolveInitialThemeMode();
    }

    function buildThemeToken(themeFamily, themeMode) {
        return themeFamily === "island"
            ? `island-${themeMode}`
            : themeMode;
    }

    function resolveThemeStylesheet(themeFamily) {
        return getThemeOption(themeFamily)?.stylesheetPath
            ?? themeConfig.defaultStylesheetPath
            ?? "/css/themes/default.css";
    }

    function updateThemeStylesheet(themeFamily) {
        const themeStylesheet = document.querySelector("[data-theme-stylesheet]");
        if (!(themeStylesheet instanceof HTMLLinkElement)) {
            return;
        }

        const href = resolveThemeStylesheet(themeFamily);
        if (themeStylesheet.getAttribute("href") !== href) {
            themeStylesheet.setAttribute("href", href);
        }
    }

    function updateToggleLabels(root = document) {
        const queryRoot = root instanceof Element || root instanceof Document ? root : document;
        const nextModeLabel = getEffectiveThemeMode() === "dark" ? "Light mode" : "Dark mode";
        for (const label of queryRoot.querySelectorAll("[data-theme-mode-toggle-label]")) {
            label.textContent = nextModeLabel;
        }
    }

    function applyTheme(themeFamily, themeMode, persistMode = false) {
        const normalizedMode = themeMode === "dark" ? "dark" : "light";
        const normalizedFamily = getThemeOption(themeFamily)?.id ?? resolveInitialThemeFamily();
        const themeToken = buildThemeToken(normalizedFamily, normalizedMode);

        updateThemeStylesheet(normalizedFamily);
        document.documentElement.style.colorScheme = normalizedMode;
        document.documentElement.setAttribute("data-theme-family", normalizedFamily);
        document.documentElement.setAttribute("data-theme-mode", normalizedMode);
        document.documentElement.setAttribute("data-theme", themeToken);
        updateToggleLabels();

        if (persistMode) {
            setStoredThemeMode(normalizedMode);
        }
    }

    function initializeThemeControls(root = document) {
        updateToggleLabels(root);
    }

    const makanaTheme = window.makanaTheme || (window.makanaTheme = {});

    makanaTheme.initialize = initializeThemeControls;
    makanaTheme.getThemeFamily = getEffectiveThemeFamily;
    makanaTheme.set = (themeFamily) => {
        if (getThemeOptions().length > 0 && !getThemeOption(themeFamily)) {
            return false;
        }

        applyTheme(themeFamily, getEffectiveThemeMode());
        return true;
    };

    makanaTheme.toggleMode = () => {
        const nextMode = getEffectiveThemeMode() === "dark" ? "light" : "dark";
        applyTheme(getEffectiveThemeFamily(), nextMode, true);
        return nextMode;
    };

    applyTheme(resolveInitialThemeFamily(), resolveInitialThemeMode());

    document.addEventListener("click", (event) => {
        const button = event.target instanceof Element
            ? event.target.closest("[data-theme-mode-toggle]")
            : null;
        if (!button) {
            return;
        }

        event.preventDefault();
        makanaTheme.toggleMode();
    });

    document.addEventListener("DOMContentLoaded", () => {
        initializeThemeControls(document);
    });

    darkQuery.addEventListener("change", () => {
        if (getStoredThemeMode() === null) {
            applyTheme(getEffectiveThemeFamily(), resolveDefaultThemeMode());
        } else {
            updateToggleLabels();
        }
    });
})();
