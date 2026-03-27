(() => {
    const state = window.makanaStackDetails || (window.makanaStackDetails = {});
    const liveControllers = state.liveControllers instanceof WeakMap ? state.liveControllers : new WeakMap();
    const tabStoragePrefix = "makana:tabs:";
    const scrollStoragePrefix = "makana:stack-scroll:";

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
        return `<div class="${escapeHtml(line.cssClass ?? "")}">${body}</div>`;
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

    function renderChangesFeed(entries) {
        return `<section class="stack-change-feed" aria-live="polite">
    <header class="stack-change-feed__header">
        <h4>Recent activity</h4>
        <p class="text-light">Incremental update rows materialized from the live stream.</p>
    </header>
    <div id="live-update-changes-feed-entries" class="stack-change-feed__entries">
        ${normalizeArray(entries).length === 0
            ? '<p id="live-update-changes-feed-empty" class="stack-change-feed__empty">No change entries yet.</p>'
            : normalizeArray(entries).map(renderChangeFeedEntry).join("")}
    </div>
</section>`;
    }

    function renderChangesTab(container, updateState) {
        if (!updateState.changes?.hasEvents) {
            return "<p>No update events recorded yet.</p>";
        }

        const tabKey = `${container.getAttribute("data-stack-view-key") ?? "stack"}:changes`;
        return `<ot-tabs class="stack-update-tabs" data-stack-tab-state="${escapeHtml(tabKey)}">
    <div role="tablist" class="stack-tablist stack-tablist--end">
        <button type="button" role="tab" aria-selected="true">Summary</button>
        <button type="button" role="tab">Diff</button>
        <button type="button" role="tab">Diagnostic</button>
    </div>
    <div role="tabpanel">
        <div id="live-update-summary-view">${renderConsoleBlock("summary", updateState.changes.summaryLines)}</div>
    </div>
    <div role="tabpanel">
        ${renderConsoleBlock("diff", updateState.changes.diffLines)}
    </div>
    <div role="tabpanel">
        ${renderConsoleBlock("diagnostic", updateState.changes.diagnosticLines)}
    </div>
</ot-tabs>`;
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

    function renderDetailGrid(items) {
        if (normalizeArray(items).length === 0) {
            return "";
        }

        return `<dl class="console-detail-grid">
    ${normalizeArray(items).map((item) => `<div class="console-detail-grid__item">
        <dt>${escapeHtml(item.label)}</dt>
        <dd>${escapeHtml(item.value)}</dd>
    </div>`).join("")}
</dl>`;
    }

    function renderMetadataTab(updateState) {
        const rows = [];
        const header = updateState.header ?? {};
        const summary = updateState.summary ?? {};
        const info = updateState.info ?? {};

        if (header.kind || info.version != null || header.message) {
            rows.push(`<div class="stack-live-metadata">${renderDetailGrid([
                { label: "Kind", value: header.kindLabel ?? "n/a" },
                { label: "Version", value: info.version ?? "n/a" },
                { label: "Message", value: header.message ?? "n/a" }
            ])}</div>`);
        }

        if (info.startTime || info.endTime || header.resourceCount != null || summary) {
            rows.push(`<div class="stack-live-metadata">
    ${renderDetailGrid([
        { label: "Preview", value: header.isPreview ? "Yes" : "No" },
        { label: "Resource count", value: header.resourceCount ?? "n/a" },
        { label: "Result", value: header.status ?? info.status ?? "n/a" }
    ])}
    ${renderDetailGrid([
        { label: "Started", value: formatExactTime(info.startTime) },
        { label: "Completed", value: formatExactTime(info.endTime) },
        { label: "Maybe corrupt", value: summary?.maybeCorrupt ? "Yes" : "No" }
    ])}
</div>`);
        }

        return rows.join("");
    }

    function renderLiveUpdateHeader(updateState) {
        const header = updateState.header ?? {};
        const info = updateState.info ?? {};
        const duration = formatDuration(info.startTime, info.endTime);
        const resourceChanges = renderResourceChanges(header.resourceChanges);
        const hasMeta = resourceChanges || header.resourceCount != null || duration;

        return `<header>
    <h2>Updates</h2>
    <p class="stack-status-line">
        Status:
        <strong>${escapeHtml(header.status ?? info.status ?? "")}</strong>
        ${renderSpinner(updateState)}
        ${header.runLabel ? `<span> · ${escapeHtml(header.runLabel)}</span>` : ""}
        ${info.startTime ? `<span> · <time title="${escapeHtml(formatExactTime(info.startTime))}">${escapeHtml(formatRelativeTime(info.startTime))}</time></span>` : ""}
    </p>
    ${header.message ? `<p><strong>${escapeHtml(header.message)}</strong></p>` : ""}
    ${hasMeta ? `<p>${resourceChanges}${header.resourceCount != null ? `<span> · Resources <strong>${escapeHtml(header.resourceCount)}</strong></span>` : ""}${duration ? `<span> · Duration <strong>${escapeHtml(duration)}</strong></span>` : ""}</p>` : ""}
</header>`;
    }

    function renderLivePanel(container, updateState) {
        const primaryTabKey = `${container.getAttribute("data-stack-view-key") ?? "stack"}:primary`;
        return `<article id="live-update-panel" class="card">
    <div id="live-update-panel-header">${renderLiveUpdateHeader(updateState)}</div>
    <ot-tabs data-stack-tab-state="${escapeHtml(primaryTabKey)}">
        <div role="tablist" class="stack-tablist">
            <button type="button" role="tab" aria-selected="true">Changes</button>
            <button type="button" role="tab">Timeline</button>
            <button type="button" role="tab">Configuration</button>
            <button type="button" role="tab">Outputs</button>
            <button type="button" role="tab">Resources</button>
            <button type="button" role="tab">Metadata</button>
        </div>
        <div role="tabpanel"><div id="live-update-panel-changes">${renderChangesTab(container, updateState)}</div></div>
        <div role="tabpanel"><div id="live-update-panel-timeline">${renderChangesFeed(updateState.changes.entries)}</div></div>
        <div role="tabpanel"><div id="live-update-panel-configuration">${renderConfigurationTab(updateState.configuration)}</div></div>
        <div role="tabpanel"><div id="live-update-panel-outputs">${renderOutputsTab(updateState.outputs)}</div></div>
        <div role="tabpanel"><div id="live-update-panel-resources">${renderResourcesTab(updateState.resources)}</div></div>
        <div role="tabpanel"><div id="live-update-panel-metadata">${renderMetadataTab(updateState)}</div></div>
    </ot-tabs>
</article>`;
    }

    function renderLatestUpdate(container, updateState) {
        const latestUpdate = normalizeArray(updateState.history)[0];
        if (!latestUpdate) {
            return "";
        }

        const detailsHref = buildStackRunUrl(container, latestUpdate.routeRunType, latestUpdate.routeReference);
        return `<div id="stack-latest-update">
    <article class="card" id="stack-latest-update-card">
        <header>
            <div class="stack-latest-update__header">
                <div>
                    <h3>Latest update</h3>
                    <p class="text-light">Most recent activity for this stack.</p>
                </div>
                ${latestUpdate.routeRunType && latestUpdate.routeReference
                    ? `<a class="button outline small" href="${escapeHtml(detailsHref)}">Open details</a>`
                    : ""}
            </div>
        </header>
        <div class="stack-latest-update__summary">
            <p><strong>${escapeHtml(latestUpdate.title)}</strong></p>
            <p class="text-light">
                <span>${escapeHtml(latestUpdate.kindLabel)}</span>
                <span> · </span>
                <span>${escapeHtml(latestUpdate.status)}</span>
                ${renderSpinner(updateState)}
                ${updateState.header?.resourceCount != null ? `<span> · </span><span>${escapeHtml(updateState.header.resourceCount)} resource${updateState.header.resourceCount === 1 ? "" : "s"}</span>` : ""}
                <span> · </span>
                <time title="${escapeHtml(formatExactTime(latestUpdate.startTime))}">${escapeHtml(formatRelativeTime(latestUpdate.startTime))}</time>
            </p>
        </div>
    </article>
</div>`;
    }

    function renderHistory(container, updateState) {
        const history = normalizeArray(updateState.history).slice(1);
        if (history.length === 0) {
            return '<div id="stack-update-history"></div>';
        }

        const rows = history.map((entry) => `<tr>
    <th scope="row">
        ${entry.routeRunType && entry.routeReference
            ? `<a href="${escapeHtml(buildStackRunUrl(container, entry.routeRunType, entry.routeReference))}"${entry.isSelected ? ' aria-current="page"' : ""}>${escapeHtml(entry.title)}</a>`
            : `<span>${escapeHtml(entry.title)}</span>`}
    </th>
    <td>${escapeHtml(entry.kindLabel)}</td>
    <td>${escapeHtml(entry.status)}</td>
    <td><time title="${escapeHtml(formatExactTime(entry.startTime))}">${escapeHtml(formatRelativeTime(entry.startTime))}</time></td>
    <td>${escapeHtml(entry.message ?? "n/a")}</td>
</tr>`).join("");

        return `<div id="stack-update-history">
    <article class="card" id="stack-update-history-card">
        <header>
            <h3>Updates</h3>
            <p class="text-light">Previous updates for this stack.</p>
        </header>
        <div class="table">
            <table>
                <thead><tr><th scope="col">Update</th><th scope="col">Kind</th><th scope="col">Status</th><th scope="col">Started</th><th scope="col">Message</th></tr></thead>
                <tbody>${rows}</tbody>
            </table>
        </div>
    </article>
</div>`;
    }

    class LiveUpdateController {
        constructor(container) {
            this.container = container;
            this.viewUrl = container.getAttribute("data-live-update-view-url");
            this.hubUrl = container.getAttribute("data-live-update-hub-url");
            this.updateId = container.getAttribute("data-live-update-id");
            this.showHistory = container.getAttribute("data-live-update-show-history") === "true";
            this.liveEnabled = container.getAttribute("data-live-update-enabled") === "true";
            this.disposed = false;
            this.connection = null;
            this.state = null;
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
                .withUrl(hubUrl.toString())
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
                console.warn("Failed to start live update connection", error);
                renderLiveUpdateNotice(
                    this.container,
                    "warning",
                    "Live streaming is unavailable.",
                    "The page loaded its initial state, but the realtime SignalR connection could not be established.");
            }
        }

        async refreshSnapshot() {
            const response = await fetch(this.viewUrl, {
                credentials: "same-origin",
                headers: {
                    Accept: "application/vnd.pulumi+8"
                }
            });

            if (response.status === 401) {
                this.stop();
                renderLiveUpdateAuthExpired(this.container);
                return;
            }

            if (!response.ok) {
                throw new Error(`Snapshot request failed: ${response.status}`);
            }

            this.state = normalizeViewState(await response.json());
            this.render();
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

            if (this.showHistory) {
                const latestMarkup = renderLatestUpdate(this.container, this.state);
                const historyMarkup = renderHistory(this.container, this.state);
                const latest = this.container.querySelector("#stack-latest-update");
                const history = this.container.querySelector("#stack-update-history");
                if (latest) {
                    latest.outerHTML = latestMarkup;
                } else if (latestMarkup) {
                    this.container.insertAdjacentHTML("beforeend", latestMarkup);
                }

                if (history) {
                    history.outerHTML = historyMarkup;
                } else if (historyMarkup) {
                    this.container.insertAdjacentHTML("beforeend", historyMarkup);
                }
            } else {
                const panel = this.container.querySelector("#live-update-panel");
                if (panel) {
                    panel.outerHTML = renderLivePanel(this.container, this.state);
                } else {
                    this.container.insertAdjacentHTML("beforeend", renderLivePanel(this.container, this.state));
                }
            }

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
        return !!controller?.state;
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
