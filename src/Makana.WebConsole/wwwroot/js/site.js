(() => {
    const storageKey = "theme";
    const darkQuery = window.matchMedia("(prefers-color-scheme: dark)");

    function getStoredTheme() {
        const theme = window.localStorage.getItem(storageKey);
        return theme === "light" || theme === "dark" ? theme : null;
    }

    function getEffectiveTheme() {
        return getStoredTheme() ?? (darkQuery.matches ? "dark" : "light");
    }

    function applyTheme(theme) {
        document.documentElement.style.colorScheme = theme;
        document.documentElement.setAttribute("data-theme", theme);
    }

    function updateToggleLabels() {
        const nextTheme = getEffectiveTheme() === "dark" ? "light" : "dark";
        for (const button of document.querySelectorAll("[data-theme-toggle-label]")) {
            button.textContent = `Use ${nextTheme} mode`;
        }
    }

    const makanaTheme = window.makanaTheme || (window.makanaTheme = {});
    makanaTheme.toggle = () => {
        const nextTheme = getEffectiveTheme() === "dark" ? "light" : "dark";
        window.localStorage.setItem(storageKey, nextTheme);
        applyTheme(nextTheme);
        updateToggleLabels();
    };

    document.addEventListener("DOMContentLoaded", () => {
        const storedTheme = getStoredTheme();
        if (storedTheme) {
            applyTheme(storedTheme);
        }

        updateToggleLabels();
    });

    darkQuery.addEventListener("change", () => {
        if (getStoredTheme() === null) {
            updateToggleLabels();
        }
    });
})();
