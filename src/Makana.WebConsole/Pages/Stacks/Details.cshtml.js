(() => {
    const state = window.makanaStackDetails || (window.makanaStackDetails = {});
    const liveControllers = state.liveControllers instanceof WeakMap ? state.liveControllers : new WeakMap();
    const tabStoragePrefix = "makana:tabs:";
    const scrollStoragePrefix = "makana:stack-scroll:";
    const tokenRefreshSkewMs = 30_000;

    state.liveControllers = liveControllers;

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll("\"", "&quot;")
            .replaceAll("'", "&#39;");
    }

    function isInProgressStatus(status) {
        return typeof status === "string"
            && /^(in-progress|running)$/i.test(status);
    }

    function getStackTabStorageKey(tabContainer) {
        const key = tabContainer?.getAttribute("data-stack-tab-state");
        return key ? `${tabStoragePrefix}${key}` : null;
    }

    function restoreTabs(root = document) {
        for (const tabContainer of root.querySelectorAll?.("ot-tabs[data-stack-tab-state]") ?? []) {
            const key = getStackTabStorageKey(tabContainer);
            if (!key) {
                continue;
            }

            const storedIndex = Number.parseInt(window.sessionStorage.getItem(key) ?? "", 10);
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

    function captureTabState(root = document) {
        const tabState = new Map();
        for (const tabContainer of root.querySelectorAll?.("ot-tabs[data-stack-tab-state]") ?? []) {
            const key = tabContainer.getAttribute("data-stack-tab-state");
            if (!key) {
                continue;
            }

            const tabs = Array.from(tabContainer.querySelectorAll(":scope > [role='tablist'] > [role='tab']"));
            const selectedIndex = tabs.findIndex((tab) => tab.getAttribute("aria-selected") === "true");
            if (selectedIndex >= 0) {
                tabState.set(key, selectedIndex);
            }
        }

        return tabState;
    }

    function applyTabState(root = document, tabState = new Map()) {
        for (const tabContainer of root.querySelectorAll?.("ot-tabs[data-stack-tab-state]") ?? []) {
            const key = tabContainer.getAttribute("data-stack-tab-state");
            const selectedIndex = key ? tabState.get(key) : undefined;
            if (!Number.isInteger(selectedIndex) || selectedIndex < 0) {
                continue;
            }

            if ("activeIndex" in tabContainer) {
                tabContainer.activeIndex = selectedIndex;
                continue;
            }

            const tabs = Array.from(tabContainer.querySelectorAll(":scope > [role='tablist'] > [role='tab']"));
            tabs[selectedIndex]?.click();
        }
    }

    function getStackViewScrollKey(container) {
        const key = container?.getAttribute("data-stack-view-key");
        return key ? `${scrollStoragePrefix}${key}` : null;
    }

    function getActiveConsoleLines(container) {
        return container?.querySelector("ot-tabs [role='tabpanel']:not([hidden]) .stack-console-lines") ?? null;
    }

    function saveActiveConsoleScroll(container) {
        const key = getStackViewScrollKey(container);
        const consoleLines = getActiveConsoleLines(container);
        if (!key || !consoleLines) {
            return;
        }

        window.sessionStorage.setItem(key, String(consoleLines.scrollTop));
    }

    function restoreActiveConsoleScroll(container) {
        const key = getStackViewScrollKey(container);
        const consoleLines = getActiveConsoleLines(container);
        if (!key || !consoleLines) {
            return;
        }

        const scrollTop = Number.parseFloat(window.sessionStorage.getItem(key) ?? "");
        if (!Number.isFinite(scrollTop)) {
            return;
        }

        consoleLines.scrollTop = scrollTop;
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
    <strong>You need to authenticate again.</strong> Live update streaming has stopped.
  </div>
  <p><a class="button" href="${escapeHtml(redirectUrl)}">Log in again</a></p>
</article>`;
    }

    function buildLiveUpdateNoticeMarkup(variant, title, message) {
        return `
<article class="card" data-live-update-notice="${escapeHtml(variant)}">
  <div role="alert" data-variant="${escapeHtml(variant)}">
    <strong>${escapeHtml(title)}</strong> ${escapeHtml(message)}
  </div>
</article>`;
    }

    function renderLiveUpdateNotice(container, variant, title, message) {
        const markup = buildLiveUpdateNoticeMarkup(variant, title, message);
        const existing = container.querySelector("[data-live-update-notice]");
        if (existing) {
            existing.outerHTML = markup;
            return;
        }

        container.insertAdjacentHTML("afterbegin", markup);
    }

    function renderLiveUpdateAuthExpired(container, explicitRedirectUrl) {
        const redirectUrl = buildReauthRedirectUrl(explicitRedirectUrl);
        const detailPanel = container.querySelector("#live-update-panel");
        if (detailPanel) {
            detailPanel.outerHTML = buildReauthPromptMarkup(redirectUrl);
            return;
        }

        const latestCard = container.querySelector("#stack-latest-update");
        if (latestCard) {
            latestCard.outerHTML = `<div id="stack-latest-update">${buildReauthPromptMarkup(redirectUrl)}</div>`;
            return;
        }

        container.innerHTML = buildReauthPromptMarkup(redirectUrl);
    }

    function formatExactTime(unixSeconds) {
        if (!Number.isFinite(unixSeconds) || unixSeconds <= 0) {
            return "n/a";
        }

        return new Date(unixSeconds * 1000).toLocaleString();
    }

    function formatRelativeTime(unixSeconds, emptyText = "No updates") {
        if (!Number.isFinite(unixSeconds) || unixSeconds <= 0) {
            return emptyText;
        }

        const deltaSeconds = Math.round((unixSeconds * 1000 - Date.now()) / 1000);
        const absSeconds = Math.abs(deltaSeconds);
        const formatter = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });

        if (absSeconds < 60) {
            return formatter.format(deltaSeconds, "second");
        }

        const deltaMinutes = Math.round(deltaSeconds / 60);
        if (Math.abs(deltaMinutes) < 60) {
            return formatter.format(deltaMinutes, "minute");
        }

        const deltaHours = Math.round(deltaMinutes / 60);
        if (Math.abs(deltaHours) < 24) {
            return formatter.format(deltaHours, "hour");
        }

        const deltaDays = Math.round(deltaHours / 24);
        return formatter.format(deltaDays, "day");
    }

    function formatDuration(startTime, endTime) {
        if (!Number.isFinite(startTime) || !Number.isFinite(endTime) || endTime < startTime) {
            return null;
        }

        const durationSeconds = Math.max(0, Math.round(endTime - startTime));
        const hours = Math.floor(durationSeconds / 3600);
        const minutes = Math.floor((durationSeconds % 3600) / 60);
        const seconds = durationSeconds % 60;

        if (hours > 0) {
            return `${hours}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`;
        }

        return `${minutes}:${String(seconds).padStart(2, "0")}`;
    }

    function normalizeArray(value) {
        return Array.isArray(value) ? value : [];
    }

    function normalizeViewState(payload) {
        return {
            ...payload,
            history: normalizeArray(payload?.history),
            configuration: normalizeArray(payload?.configuration),
            outputs: normalizeArray(payload?.outputs),
            resources: normalizeArray(payload?.resources),
            changes: {
                ...(payload?.changes ?? {}),
                entries: normalizeArray(payload?.changes?.entries),
                deltaEntries: normalizeArray(payload?.changes?.deltaEntries),
                summaryLines: normalizeArray(payload?.changes?.summaryLines),
                diffLines: normalizeArray(payload?.changes?.diffLines),
                diagnosticLines: normalizeArray(payload?.changes?.diagnosticLines),
                summaryLineDeltas: normalizeArray(payload?.changes?.summaryLineDeltas),
                diffLineDeltas: normalizeArray(payload?.changes?.diffLineDeltas),
                diagnosticLineDeltas: normalizeArray(payload?.changes?.diagnosticLineDeltas)
            }
        };
    }

    function applyEntryDeltas(existingEntries, deltaEntries) {
        const entries = [...normalizeArray(existingEntries)];
        const byKey = new Map(entries.map((entry, index) => [entry.key, index]));

        for (const entry of normalizeArray(deltaEntries)) {
            const currentIndex = byKey.get(entry.key);
            if ((entry.action ?? "").toLowerCase() === "delete") {
                if (currentIndex !== undefined) {
                    entries.splice(currentIndex, 1);
                    byKey.clear();
                    for (let index = 0; index < entries.length; index++) {
                        byKey.set(entries[index].key, index);
                    }
                }

                continue;
            }

            if ((entry.action ?? "").toLowerCase() === "append" || currentIndex === undefined) {
                entries.push(entry);
                byKey.set(entry.key, entries.length - 1);
                continue;
            }

            entries[currentIndex] = entry;
        }

        return entries;
    }

    function applyLineDeltas(existingLines, fullLines, deltas) {
        let lines = normalizeArray(fullLines).length > 0
            ? [...fullLines]
            : [...normalizeArray(existingLines)];

        const findIndex = (key) => lines.findIndex((line) => line.key === key);

        for (const delta of normalizeArray(deltas)) {
            const action = (delta.action ?? "").toLowerCase();
            const currentIndex = findIndex(delta.key);

            if (action === "delete") {
                if (currentIndex >= 0) {
                    lines.splice(currentIndex, 1);
                }

                continue;
            }

            if (action === "append" && delta.line) {
                lines.push(delta.line);
                continue;
            }

            if (action === "insertbefore" && delta.line) {
                const anchorIndex = findIndex(delta.anchorKey);
                if (anchorIndex >= 0) {
                    lines.splice(anchorIndex, 0, delta.line);
                } else {
                    lines.push(delta.line);
                }

                continue;
            }

            if (delta.line) {
                if (currentIndex >= 0) {
                    lines[currentIndex] = delta.line;
                } else {
                    lines.push(delta.line);
                }
            }
        }

        return lines;
    }

    function mergeUpdateView(currentState, incomingPayload) {
        const incoming = normalizeViewState(incomingPayload);
        if (!currentState || !incoming.delta?.isIncremental) {
            return incoming;
        }

        const next = normalizeViewState({
            ...currentState,
            ...incoming,
            info: incoming.info ?? currentState.info,
            header: incoming.header ?? currentState.header,
            summary: incoming.summary ?? currentState.summary,
            stack: incoming.stack ?? currentState.stack,
            continuationToken: incoming.continuationToken ?? null
        });

        const delta = incoming.delta;
        if (delta.infoChanged) {
            next.info = incoming.info ?? currentState.info;
            next.header = incoming.header ?? currentState.header;
            next.history = normalizeArray(incoming.history).length > 0 ? incoming.history : currentState.history;
            next.summary = incoming.summary ?? currentState.summary;
        } else {
            next.history = currentState.history;
            next.summary = currentState.summary;
        }

        next.configuration = delta.configurationChanged ? incoming.configuration : currentState.configuration;
        next.outputs = delta.outputsChanged ? incoming.outputs : currentState.outputs;
        next.resources = delta.resourcesChanged ? incoming.resources : currentState.resources;

        next.changes.entries = normalizeArray(incoming.changes.entries).length > 0
            ? incoming.changes.entries
            : applyEntryDeltas(currentState.changes.entries, incoming.changes.deltaEntries);

        next.changes.summaryLines = applyLineDeltas(
            currentState.changes.summaryLines,
            incoming.changes.summaryLines,
            incoming.changes.summaryLineDeltas);
        next.changes.diffLines = applyLineDeltas(
            currentState.changes.diffLines,
            incoming.changes.diffLines,
            incoming.changes.diffLineDeltas);
        next.changes.diagnosticLines = applyLineDeltas(
            currentState.changes.diagnosticLines,
            incoming.changes.diagnosticLines,
            incoming.changes.diagnosticLineDeltas);

        next.changes.deltaEntries = incoming.changes.deltaEntries;
        next.changes.messageCount = incoming.changes.messageCount ?? currentState.changes.messageCount ?? 0;
        next.changes.hasEvents = incoming.changes.hasEvents ?? currentState.changes.hasEvents ?? false;

        return next;
    }

    function buildStackRunUrl(container, routeRunType, routeReference) {
        const basePath = container.getAttribute("data-live-update-route-url") ?? window.location.pathname;
        if (!routeRunType || !routeReference) {
            return basePath;
        }

        const trimmed = basePath.replace(/\/(updates|previews|refreshes|destroys)\/[^/]+$/, "");
        return `${trimmed}/${encodeURIComponent(routeRunType)}/${encodeURIComponent(routeReference)}`;
    }

    function renderSpinner(updateState) {
        return isInProgressStatus(updateState?.header?.status ?? updateState?.info?.status)
            ? '<span aria-busy="true" data-spinner="small" class="stack-status-spinner" aria-label="Update in progress"></span>'
            : "";
    }

    function renderResourceChanges(resourceChanges) {
        if (!resourceChanges || Object.keys(resourceChanges).length === 0) {
            return "";
        }

        const parts = Object.entries(resourceChanges)
            .sort((left, right) => left[0].localeCompare(right[0]))
            .map(([key, value]) => `<span><strong>${escapeHtml(key)}</strong> ${escapeHtml(value)}</span>`);
        return `<span>Changes:</span>${parts.join("")}`;
    }

    function setElementHidden(element, hidden) {
        if (element) {
            element.hidden = !!hidden;
        }
    }

    function setElementHtml(element, html, hidden = false) {
        if (!element) {
            return;
        }

        element.innerHTML = html;
        element.hidden = !!hidden;
    }

    function setElementText(element, value) {
        if (element) {
            element.textContent = value ?? "";
        }
    }

    function renderSegments(segments) {
        return normalizeArray(segments)
            .map((segment) => `<span class="${escapeHtml(segment.cssClass ?? "")}">${escapeHtml(segment.text ?? "")}</span>`)
            .join("");
    }

    function renderCell(cell) {
        const cssClass = escapeHtml(cell.cssClass ?? "");
        const segments = normalizeArray(cell.segments);
        const isSummaryType = cssClass.includes("stack-console-cell--summary-type");
        const prefixSegments = segments.filter((segment) => {
            const segmentClass = segment.cssClass ?? "";
            return segmentClass.includes("stack-console-segment--tree")
                || segmentClass.includes("stack-console-segment--marker");
        });

        if (isSummaryType && prefixSegments.length > 0) {
            const textSegments = segments.filter((segment) => {
                const segmentClass = segment.cssClass ?? "";
                return !segmentClass.includes("stack-console-segment--tree")
                    && !segmentClass.includes("stack-console-segment--marker");
            });

            return `<span class="${cssClass}">
                <span class="stack-console-summary-type-content stack-console-summary-type-content--tree">
                    <span class="stack-console-summary-type-prefix">${renderSegments(prefixSegments)}</span>
                    <span class="stack-console-summary-type-text">${renderSegments(textSegments)}</span>
                </span>
            </span>`;
        }

        return `<span class="${cssClass}">${renderSegments(segments)}</span>`;
    }

    function renderConsoleLine(line) {
        const cells = normalizeArray(line.cells);
        const body = cells.length > 0
            ? cells.map(renderCell).join("")
            : renderSegments(line.segments);
        return `<div class="${escapeHtml(line.cssClass ?? "")}" data-live-line-key="${escapeHtml(line.key ?? "")}">${body}</div>`;
    }

    function renderConsoleBlock(viewName, lines) {
        const containerId = `live-update-${viewName}-lines`;
        return `<section class="stack-console-block stack-live-console-view" aria-live="polite">
    <div id="${containerId}" class="stack-console-lines" data-console-view="${viewName}">
        ${normalizeArray(lines).map(renderConsoleLine).join("")}
    </div>
</section>`;
    }

    function mapChangeEntry(entry) {
        let title = "Change";
        let detail = entry.text ?? entry.info ?? entry.summary ?? "";

        switch (entry.kind) {
        case "resource":
            title = entry.operation
                ? `${entry.operation}: ${entry.resourceType ? `${entry.resourceType}${entry.resourceName ? ` ${entry.resourceName}` : ""}` : "resource"}`
                : "resource";
            detail = [entry.status, entry.info, entry.resourceUrn].filter(Boolean).join(" · ") || "Resource updated";
            break;
        case "diagnostic":
            title = entry.severity || "Diagnostic";
            detail = entry.text ?? "";
            break;
        case "text":
            title = entry.channel || "Output";
            detail = entry.text ?? "";
            break;
        case "configuration":
            title = "Configuration";
            detail = normalizeArray(entry.configEntries).length > 0
                ? entry.configEntries.map((config) => `${config.key}=${config.value}`).join(", ")
                : "Configuration updated";
            break;
        case "summary":
            title = "Summary";
            detail = entry.summary ?? "";
            break;
        default:
            title = entry.kind || "Change";
            break;
        }

        return { ...entry, title, detail };
    }

    function renderChangeFeedEntry(entry) {
        const mapped = mapChangeEntry(entry);
        return `<article class="stack-change-feed-entry${mapped.isError ? " stack-change-feed-entry--error" : ""}">
    <div class="stack-change-feed-entry__meta">
        <span class="stack-change-feed-entry__sequence">#${escapeHtml(mapped.sequence)}</span>
        <span class="stack-change-feed-entry__kind">${escapeHtml(mapped.kind)}</span>
        ${mapped.action ? `<span class="stack-change-feed-entry__action">${escapeHtml(mapped.action)}</span>` : ""}
    </div>
    <h5 class="stack-change-feed-entry__title">
        ${mapped.marker ? `<span class="stack-change-feed-entry__marker">${escapeHtml(mapped.marker)}</span>` : ""}
        ${escapeHtml(mapped.title)}
    </h5>
    <p class="stack-change-feed-entry__detail">${escapeHtml(mapped.detail)}</p>
</article>`;
    }

    function renderConfigurationTab(configuration) {
        if (normalizeArray(configuration).length === 0) {
            return "<p>No configuration was recorded for this update.</p>";
        }

        const rows = configuration.map((entry) => {
            let flags = "-";
            if (entry.secret || entry.object) {
                flags = `${entry.secret ? "secret" : ""}${entry.object ? `${entry.secret ? " " : ""}object` : ""}`;
            }

            return `<tr>
    <th scope="row"><code>${escapeHtml(entry.key)}</code></th>
    <td><code>${escapeHtml(entry.value)}</code></td>
    <td>${escapeHtml(flags)}</td>
</tr>`;
        }).join("");

        return `<div class="table"><table><thead><tr><th scope="col">Key</th><th scope="col">Value</th><th scope="col">Flags</th></tr></thead><tbody>${rows}</tbody></table></div>`;
    }

    function renderOutputsTab(outputs) {
        if (normalizeArray(outputs).length === 0) {
            return "<p>No stack outputs were recorded for this update.</p>";
        }

        const rows = outputs.map((entry) => `<tr>
    <th scope="row"><code>${escapeHtml(entry.name)}</code></th>
    <td><code>${escapeHtml(entry.oldValue ?? "n/a")}</code></td>
    <td><code>${escapeHtml(entry.newValue ?? "n/a")}</code></td>
</tr>`).join("");

        return `<div class="table"><table><thead><tr><th scope="col">Name</th><th scope="col">Previous</th><th scope="col">Current</th></tr></thead><tbody>${rows}</tbody></table></div>`;
    }

    function renderResourcesTab(resources) {
        if (normalizeArray(resources).length === 0) {
            return "<p>No resource operations were recorded for this update.</p>";
        }

        const rows = resources.map((entry) => `<tr>
    <td><code>${escapeHtml(entry.type ?? "resource")}</code></td>
    <th scope="row">
        <div>${escapeHtml(entry.name ?? "resource")}</div>
        ${entry.urn ? `<small><code>${escapeHtml(entry.urn)}</code></small>` : ""}
    </th>
    <td>${escapeHtml(entry.status ?? "")}</td>
    <td>${escapeHtml(entry.info || "n/a")}</td>
</tr>`).join("");

        return `<div class="table"><table><thead><tr><th scope="col">Type</th><th scope="col">Name</th><th scope="col">Status</th><th scope="col">Info</th></tr></thead><tbody>${rows}</tbody></table></div>`;
    }

    function findLineElement(linesElement, key) {
        for (const child of linesElement?.children ?? []) {
            if (child.getAttribute("data-live-line-key") === key) {
                return child;
            }
        }

        return null;
    }

    function patchConsoleLines(viewName, lines, deltas) {
        const linesElement = document.getElementById(`live-update-${viewName}-lines`);
        if (!linesElement) {
            return;
        }

        if (normalizeArray(deltas).length === 0 || linesElement.children.length === 0) {
            linesElement.innerHTML = normalizeArray(lines).map(renderConsoleLine).join("");
            return;
        }

        for (const delta of normalizeArray(deltas)) {
            const currentLine = findLineElement(linesElement, delta.key);
            const action = (delta.action ?? "").toLowerCase();

            if (action === "delete") {
                currentLine?.remove();
                continue;
            }

            if (!delta.line) {
                continue;
            }

            const renderedLine = renderConsoleLine(delta.line);
            if (action === "insertbefore" && delta.anchorKey) {
                const anchorLine = findLineElement(linesElement, delta.anchorKey);
                if (anchorLine) {
                    anchorLine.insertAdjacentHTML("beforebegin", renderedLine);
                    continue;
                }
            }

            if (action === "append" || !currentLine) {
                linesElement.insertAdjacentHTML("beforeend", renderedLine);
                continue;
            }

            currentLine.outerHTML = renderedLine;
        }
    }

    function updateConsoleView(viewName, lines, deltas) {
        patchConsoleLines(viewName, lines, deltas);
        setElementHidden(
            document.getElementById(`live-update-${viewName}-empty`),
            normalizeArray(lines).length > 0);
    }

    function updateHeader(updateState) {
        const header = updateState.header ?? {};
        const info = updateState.info ?? {};
        const duration = formatDuration(info.startTime, info.endTime);
        const resourceChanges = renderResourceChanges(header.resourceChanges);

        setElementText(
            document.getElementById("live-update-status-value"),
            header.status ?? info.status ?? "");
        setElementHtml(
            document.getElementById("live-update-status-spinner"),
            renderSpinner(updateState));
        setElementHtml(
            document.getElementById("live-update-run-label"),
            header.runLabel ? ` · ${escapeHtml(header.runLabel)}` : "",
            !header.runLabel);
        setElementHtml(
            document.getElementById("live-update-start-time"),
            Number.isFinite(info.startTime) && info.startTime > 0
                ? ` · <time title="${escapeHtml(formatExactTime(info.startTime))}">${escapeHtml(formatRelativeTime(info.startTime))}</time>`
                : "",
            !Number.isFinite(info.startTime) || info.startTime <= 0);
        setElementHtml(
            document.getElementById("live-update-message"),
            header.message ? `<strong>${escapeHtml(header.message)}</strong>` : "",
            !header.message);
        setElementHtml(
            document.getElementById("live-update-resource-changes"),
            resourceChanges,
            !resourceChanges);
        setElementHtml(
            document.getElementById("live-update-resource-count"),
            header.resourceCount != null ? ` · Resources <strong>${escapeHtml(header.resourceCount)}</strong>` : "",
            header.resourceCount == null);
        setElementHtml(
            document.getElementById("live-update-duration"),
            duration ? ` · Duration <strong>${escapeHtml(duration)}</strong>` : "",
            !duration);
        setElementHidden(
            document.getElementById("live-update-summary-meta"),
            !resourceChanges && header.resourceCount == null && !duration);
    }

    function updateMetadata(updateState) {
        const header = updateState.header ?? {};
        const info = updateState.info ?? {};
        const summary = updateState.summary ?? {};

        setElementText(document.getElementById("live-update-meta-kind"), header.kindLabel ?? "n/a");
        setElementText(document.getElementById("live-update-meta-version"), info.version ?? "n/a");
        setElementText(document.getElementById("live-update-meta-message"), header.message ?? "n/a");
        setElementText(document.getElementById("live-update-meta-preview"), header.isPreview ? "Yes" : "No");
        setElementText(document.getElementById("live-update-meta-resource-count"), header.resourceCount ?? "n/a");
        setElementText(document.getElementById("live-update-meta-result"), header.status ?? info.status ?? "n/a");
        setElementText(document.getElementById("live-update-meta-started"), formatExactTime(info.startTime));
        setElementText(document.getElementById("live-update-meta-completed"), formatExactTime(info.endTime));
        setElementText(document.getElementById("live-update-meta-maybe-corrupt"), summary?.maybeCorrupt ? "Yes" : "No");
    }

    function updateChangeFeed(entries) {
        const entriesElement = document.getElementById("live-update-changes-feed-entries");
        if (!entriesElement) {
            return;
        }

        const allEntries = normalizeArray(entries);
        entriesElement.innerHTML = allEntries.length === 0
            ? '<p id="live-update-changes-feed-empty" class="stack-change-feed__empty">No change entries yet.</p>'
            : allEntries.map(renderChangeFeedEntry).join("");
    }

    function updateLiveSections(updateState) {
        updateHeader(updateState);
        updateConsoleView("summary", updateState.changes.summaryLines, updateState.changes.summaryLineDeltas);
        updateConsoleView("diff", updateState.changes.diffLines, updateState.changes.diffLineDeltas);
        updateConsoleView("diagnostic", updateState.changes.diagnosticLines, updateState.changes.diagnosticLineDeltas);
        updateChangeFeed(updateState.changes.entries);

        const configurationPanel = document.getElementById("live-update-panel-configuration");
        const outputsPanel = document.getElementById("live-update-panel-outputs");
        const resourcesPanel = document.getElementById("live-update-panel-resources");

        if (configurationPanel) {
            configurationPanel.innerHTML = renderConfigurationTab(updateState.configuration);
        }

        if (outputsPanel) {
            outputsPanel.innerHTML = renderOutputsTab(updateState.outputs);
        }

        if (resourcesPanel) {
            resourcesPanel.innerHTML = renderResourcesTab(updateState.resources);
        }

        updateMetadata(updateState);
    }

    class LiveUpdateController {
        constructor(container) {
            this.container = container;
            this.viewUrl = container.getAttribute("data-live-update-view-url");
            this.hubUrl = container.getAttribute("data-live-update-hub-url");
            this.authUrl = container.getAttribute("data-live-update-auth-url");
            this.updateId = container.getAttribute("data-live-update-id");
            this.showHistory = container.getAttribute("data-live-update-show-history") === "true";
            this.liveEnabled = container.getAttribute("data-live-update-enabled") === "true";
            this.disposed = false;
            this.connection = null;
            this.state = null;
            this.apiToken = null;
            this.apiTokenExpiresAt = 0;
        }

        async start() {
            if (!this.liveEnabled || !this.updateId || !this.viewUrl) {
                return;
            }

            try {
                await this.refreshSnapshot();
            } catch (error) {
                console.warn("Failed to load live update snapshot", error);
                renderLiveUpdateNotice(
                    this.container,
                    "danger",
                    "Live updates are unavailable.",
                    "The page could not reach the API snapshot endpoint. Reload the page after verifying API connectivity and certificate trust.");
                return;
            }

            if (!this.liveEnabled || !window.signalR || !this.hubUrl) {
                return;
            }

            const hubUrl = new URL(this.hubUrl, window.location.origin);
            hubUrl.searchParams.set("org", this.container.getAttribute("data-live-update-owner") ?? "");
            hubUrl.searchParams.set("project", this.container.getAttribute("data-live-update-project") ?? "");
            hubUrl.searchParams.set("stack", this.container.getAttribute("data-live-update-stack") ?? "");
            hubUrl.searchParams.set("updateId", this.updateId);

            this.connection = new signalR.HubConnectionBuilder()
                .withUrl(hubUrl.toString(), {
                    accessTokenFactory: async () => {
                        const token = await this.getApiToken();
                        return token ?? "";
                    }
                })
                .withAutomaticReconnect()
                .build();

            this.connection.on("stack-update-view", (payload) => {
                if (this.disposed) {
                    return;
                }

                this.state = mergeUpdateView(this.state, payload);
                this.render();
            });

            this.connection.onreconnected(async () => {
                if (this.disposed) {
                    return;
                }

                await this.refreshSnapshot();
            });

            try {
                await this.connection.start();
            } catch (error) {
                try {
                    await this.getApiToken(true);
                    await this.connection.start();
                } catch (retryError) {
                    console.warn("Failed to start live update connection", retryError ?? error);
                    renderLiveUpdateNotice(
                        this.container,
                        "warning",
                        "Live streaming is unavailable.",
                        "The page loaded its initial state, but the realtime SignalR connection could not be established.");
                }
            }
        }

        async refreshSnapshot() {
            const response = await this.fetchAuthorized(this.viewUrl, {
                headers: {
                    Accept: "application/vnd.pulumi+8"
                }
            });
            if (!response) {
                return;
            }

            if (!response.ok) {
                throw new Error(`Snapshot request failed: ${response.status}`);
            }

            this.state = normalizeViewState(await response.json());
            this.render();
        }

        async getApiToken(forceRefresh = false) {
            if (!forceRefresh &&
                typeof this.apiToken === "string" &&
                this.apiToken.length > 0 &&
                (!Number.isFinite(this.apiTokenExpiresAt) || this.apiTokenExpiresAt <= 0 || Date.now() + tokenRefreshSkewMs < this.apiTokenExpiresAt)) {
                return this.apiToken;
            }

            if (!this.authUrl) {
                return null;
            }

            const response = await fetch(this.authUrl, {
                credentials: "same-origin",
                headers: {
                    Accept: "application/json"
                }
            });

            if (response.status === 401) {
                this.liveEnabled = false;
                this.apiToken = null;
                this.apiTokenExpiresAt = 0;
                await this.stop();
                renderLiveUpdateAuthExpired(this.container);
                return null;
            }

            if (!response.ok) {
                throw new Error(`Session-token request failed: ${response.status}`);
            }

            const payload = await response.json();
            this.apiToken = typeof payload?.accessToken === "string" ? payload.accessToken : null;
            this.apiTokenExpiresAt = payload?.expiresAt ? Date.parse(payload.expiresAt) : 0;
            return this.apiToken;
        }

        async fetchAuthorized(url, options = {}) {
            const headers = new Headers(options.headers ?? {});
            const token = await this.getApiToken();
            if (token) {
                headers.set("Authorization", `token ${token}`);
            }

            let response = await fetch(url, {
                ...options,
                credentials: "same-origin",
                headers
            });

            if (response.status !== 401) {
                return response;
            }

            const refreshedToken = await this.getApiToken(true);
            if (!refreshedToken) {
                return null;
            }

            const retryHeaders = new Headers(options.headers ?? {});
            retryHeaders.set("Authorization", `token ${refreshedToken}`);
            response = await fetch(url, {
                ...options,
                credentials: "same-origin",
                headers: retryHeaders
            });

            if (response.status === 401) {
                this.liveEnabled = false;
                await this.stop();
                renderLiveUpdateAuthExpired(this.container);
                return null;
            }

            return response;
        }

        render() {
            if (!this.state || this.disposed) {
                return;
            }

            const tokenInput = this.container.querySelector("#stack-live-panel-token");
            if (tokenInput) {
                tokenInput.value = this.state.continuationToken ?? "";
                tokenInput.setAttribute("data-live-enabled", isInProgressStatus(this.state.header?.status ?? this.state.info?.status) ? "true" : "false");
            }

            saveActiveConsoleScroll(this.container);
            const tabState = captureTabState(this.container);

            updateLiveSections(this.state);

            window.requestAnimationFrame(() => {
                applyTabState(this.container, tabState);
                restoreTabs(this.container);
                window.requestAnimationFrame(() => restoreActiveConsoleScroll(this.container));
            });

            if (!isInProgressStatus(this.state.header?.status ?? this.state.info?.status)) {
                this.liveEnabled = false;
                void this.stop();
            }
        }

        async stop() {
            if (this.connection) {
                try {
                    await this.connection.stop();
                } catch {
                    // Ignore shutdown errors during teardown.
                }
            }

            this.connection = null;
        }

        dispose() {
            this.disposed = true;
            void this.stop();
        }

        isReady() {
            if (!this.state) {
                return false;
            }

            if (!this.liveEnabled) {
                return true;
            }

            return !this.connection || this.connection.state === "Connected";
        }
    }

    function initializeRoot(root = document) {
        restoreTabs(root);
        restoreActiveConsoleScroll(root.querySelector?.("#stack-live-panel-container"));

        const container = root.querySelector?.("#stack-live-panel-container");
        if (!container || liveControllers.has(container)) {
            return;
        }

        const controller = new LiveUpdateController(container);
        liveControllers.set(container, controller);
        void controller.start();
    }

    function disposeRoot(root = document) {
        for (const container of root.querySelectorAll?.("#stack-live-panel-container") ?? []) {
            const controller = liveControllers.get(container);
            if (controller) {
                controller.dispose();
                liveControllers.delete(container);
            }
        }
    }

    function resolveLiveContainer(containerOrSelector) {
        if (containerOrSelector instanceof HTMLElement) {
            return containerOrSelector;
        }

        if (typeof containerOrSelector === "string") {
            return document.querySelector(containerOrSelector);
        }

        return document.querySelector("#stack-live-panel-container");
    }

    const makanaLive = window.makanaLive || (window.makanaLive = {});

    makanaLive.rerender = (containerOrSelector) => {
        const container = resolveLiveContainer(containerOrSelector);
        const controller = container ? liveControllers.get(container) : null;
        if (!controller?.state) {
            return false;
        }

        controller.render();
        return true;
    };

    makanaLive.isReady = (containerOrSelector) => {
        const container = resolveLiveContainer(containerOrSelector);
        const controller = container ? liveControllers.get(container) : null;
        return controller?.isReady?.() === true;
    };

    makanaLive.inject = (containerOrSelector, payload) => {
        const container = resolveLiveContainer(containerOrSelector);
        const controller = container ? liveControllers.get(container) : null;
        if (!controller?.state) {
            return false;
        }

        controller.state = mergeUpdateView(controller.state, payload);
        controller.render();
        return true;
    };

    makanaLive.refresh = async (containerOrSelector) => {
        const container = resolveLiveContainer(containerOrSelector);
        const controller = container ? liveControllers.get(container) : null;
        if (!controller) {
            return false;
        }

        await controller.refreshSnapshot();
        return true;
    };

    if (!state.listenersRegistered) {
        document.addEventListener("ot-tab-change", (event) => {
            const tabContainer = event.target?.closest?.("ot-tabs[data-stack-tab-state]");
            const key = getStackTabStorageKey(tabContainer);
            const index = event.detail?.index;
            if (!key || !Number.isInteger(index)) {
                return;
            }

            window.sessionStorage.setItem(key, String(index));
        });

        document.addEventListener("htmx:beforeSwap", (event) => {
            const target = event.detail?.target;
            if (!(target instanceof HTMLElement) || target.id !== "console-page") {
                return;
            }

            saveActiveConsoleScroll(target.querySelector?.("#stack-live-panel-container"));
            disposeRoot(target);
        });

        document.addEventListener("htmx:load", (event) => {
            initializeRoot(event.target);
        });

        state.listenersRegistered = true;
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", () => {
            initializeRoot(document);
        }, { once: true });
    } else {
        initializeRoot(document);
    }
})();
