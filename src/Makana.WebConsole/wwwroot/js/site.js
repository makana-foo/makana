(() => {
    const storageKey = "theme";
    const tabStoragePrefix = "makana:tabs:";
    const scrollStoragePrefix = "makana:stack-scroll:";
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

    function getStackTabStorageKey(tabContainer) {
        const key = tabContainer?.getAttribute("data-stack-tab-state");
        return key ? `${tabStoragePrefix}${key}` : null;
    }

    function restoreTabs(root = document) {
        for (const tabContainer of root.querySelectorAll("ot-tabs[data-stack-tab-state]")) {
            const storageKey = getStackTabStorageKey(tabContainer);
            if (!storageKey) {
                continue;
            }

            const storedIndex = Number.parseInt(window.sessionStorage.getItem(storageKey) ?? "", 10);
            if (!Number.isInteger(storedIndex) || storedIndex < 0) {
                continue;
            }

            const tabs = Array.from(tabContainer.querySelectorAll(":scope > [role='tablist'] > [role='tab']"));
            const tab = tabs[storedIndex];
            if (!tab || tab.getAttribute("aria-selected") === "true") {
                continue;
            }

            tab.click();
        }
    }

    function getStackViewScrollKey(container) {
        const key = container?.getAttribute("data-stack-view-key");
        return key ? `${scrollStoragePrefix}${key}` : null;
    }

    function getActiveConsoleLines(container) {
        return container?.querySelector("ot-tabs [role='tabpanel']:not([hidden]) .stack-console-lines") ?? null;
    }

    function isLiveUpdateRefreshAnchor(target) {
        return target instanceof HTMLElement && target.id === "stack-live-panel-refresh-anchor";
    }

    function isLiveRefreshEnabled() {
        const tokenInput = document.getElementById("stack-live-panel-token");
        return tokenInput?.getAttribute("data-live-enabled") === "true";
    }

    function setLiveLoadingState(isBusy) {
        void isBusy;
    }

    function disableLiveRefresh() {
        const tokenInput = document.getElementById("stack-live-panel-token");
        if (tokenInput) {
            tokenInput.setAttribute("data-live-enabled", "false");
        }
    }

    function isLoginRedirectResponse(xhr) {
        if (!xhr?.responseURL) {
            return false;
        }

        try {
            const url = new URL(xhr.responseURL, window.location.origin);
            return url.pathname === "/Login";
        } catch {
            return false;
        }
    }

    function buildReauthRedirectUrl(explicitRedirectUrl) {
        if (explicitRedirectUrl) {
            return explicitRedirectUrl;
        }

        return `/Login?returnUrl=${encodeURIComponent(window.location.pathname + window.location.search)}`;
    }

    function buildReauthPromptMarkup(redirectUrl) {
        return `
<article class="card" id="live-update-auth-expired">
  <div role="alert" data-variant="warning">
    <strong>You need to authenticate again.</strong> Live update polling has stopped.
  </div>
  <p><a class="button" href="${redirectUrl}">Log in again</a></p>
</article>`;
    }

    function renderLiveUpdateAuthExpired(explicitRedirectUrl) {
        const redirectUrl = buildReauthRedirectUrl(explicitRedirectUrl);
        const detailPanel = document.getElementById("live-update-panel");
        if (detailPanel) {
            detailPanel.outerHTML = buildReauthPromptMarkup(redirectUrl);
            return;
        }

        const latestCard = document.getElementById("stack-latest-update");
        if (latestCard) {
            latestCard.outerHTML = `
<div id="stack-latest-update">
  ${buildReauthPromptMarkup(redirectUrl)}
</div>`;
            return;
        }

        const container = document.getElementById("stack-live-panel-container");
        if (container) {
            container.innerHTML = buildReauthPromptMarkup(redirectUrl);
        }
    }

    function saveActiveConsoleScroll(container) {
        const storageKey = getStackViewScrollKey(container);
        const consoleLines = getActiveConsoleLines(container);
        if (!storageKey || !consoleLines) {
            return;
        }

        window.sessionStorage.setItem(storageKey, String(consoleLines.scrollTop));
    }

    function restoreActiveConsoleScroll(container) {
        const storageKey = getStackViewScrollKey(container);
        const consoleLines = getActiveConsoleLines(container);
        if (!storageKey || !consoleLines) {
            return;
        }

        const scrollTop = Number.parseFloat(window.sessionStorage.getItem(storageKey) ?? "");
        if (!Number.isFinite(scrollTop)) {
            return;
        }

        consoleLines.scrollTop = scrollTop;
    }

    document.addEventListener("DOMContentLoaded", () => {
        const storedTheme = getStoredTheme();
        if (storedTheme) {
            applyTheme(storedTheme);
        }

        setLiveLoadingState(false);
        updateToggleLabels();
        restoreTabs(document);
        restoreActiveConsoleScroll(document.getElementById("stack-live-panel-container"));
    });

    document.addEventListener("ot-tab-change", (event) => {
        const tabContainer = event.target?.closest?.("ot-tabs[data-stack-tab-state]");
        const storageKey = getStackTabStorageKey(tabContainer);
        const index = event.detail?.index;
        if (!storageKey || !Number.isInteger(index)) {
            return;
        }

        window.sessionStorage.setItem(storageKey, String(index));
    });

    document.addEventListener("htmx:beforeSwap", (event) => {
        const target = event.detail?.target;
        if (!isLiveUpdateRefreshAnchor(target)) {
            return;
        }

        if (isLoginRedirectResponse(event.detail?.xhr)) {
            event.preventDefault();
            setLiveLoadingState(false);
            disableLiveRefresh();
            renderLiveUpdateAuthExpired();
            return;
        }

        saveActiveConsoleScroll(document.getElementById("stack-live-panel-container"));
    });

    document.addEventListener("htmx:beforeRequest", (event) => {
        const target = event.detail?.target;
        if (!isLiveUpdateRefreshAnchor(target) || isLiveRefreshEnabled()) {
            return;
        }

        event.preventDefault();
    });

    document.addEventListener("htmx:beforeRequest", (event) => {
        const target = event.detail?.target;
        if (!isLiveUpdateRefreshAnchor(target) || !isLiveRefreshEnabled()) {
            return;
        }

        setLiveLoadingState(true);
    });

    document.addEventListener("htmx:afterOnLoad", (event) => {
        const target = event.detail?.target;
        if (!isLiveUpdateRefreshAnchor(target)) {
            return;
        }

        setLiveLoadingState(false);
        const container = document.getElementById("stack-live-panel-container");
        restoreTabs(container ?? document);
        window.requestAnimationFrame(() => restoreActiveConsoleScroll(container));
    });

    document.addEventListener("htmx:responseError", (event) => {
        const target = event.detail?.target;
        if (!isLiveUpdateRefreshAnchor(target)) {
            return;
        }

        setLiveLoadingState(false);
        if (event.detail?.xhr?.status === 401) {
            disableLiveRefresh();
            renderLiveUpdateAuthExpired(event.detail.xhr.getResponseHeader("X-Makana-Reauth-Redirect"));
        }
    });

    document.addEventListener("htmx:sendError", (event) => {
        const target = event.detail?.target;
        if (!isLiveUpdateRefreshAnchor(target)) {
            return;
        }

        setLiveLoadingState(false);
    });

    darkQuery.addEventListener("change", () => {
        if (getStoredTheme() === null) {
            updateToggleLabels();
        }
    });
})();
