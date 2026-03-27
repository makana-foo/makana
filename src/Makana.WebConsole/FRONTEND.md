# Makana Web Console frontend guide

This guide defines the frontend rules for `src/Makana.WebConsole`. Follow it for every page, partial, component, stylesheet, and script change in the Web Console.

## Source precedence

When guidance conflicts, use this order:

1. Product requirements and approved OpenSpec changes.
2. This guide.
3. Existing Makana Web Console patterns that already follow this guide.
4. The closest Oat example or semantic pattern.
5. ASP.NET Core Razor Pages and HTMX/Htmx.Net reference guidance.

If existing code conflicts with this guide, either align the implementation or record a narrow exception in the change note for that work.

## 1. UI composition

### Oat is the UI framework

- Treat Oat as the default and authoritative UI framework for the Web Console.
- Start from the closest Oat example or semantic HTML pattern before introducing custom styling.
- Do not mix in Tailwind, Bootstrap, React component libraries, or other competing styling systems.
- Prefer semantic HTML that Oat styles directly over utility-heavy or framework-specific markup.

### Prefer Oat built-ins before custom classes

- Use this styling order: semantic HTML that Oat styles directly, then built-in Oat component behavior, then Oat utility classes, then local scoped CSS, and finally truly global CSS only as a last resort.
- Do not add a custom class or scoped CSS rule when Oat already provides the needed spacing, layout, alignment, menu, button, or navigation behavior.
- When touching older frontend code, remove redundant custom classes or CSS that merely restate Oat behavior instead of preserving them by default.
- If a custom override is still necessary, keep it local to the owning page, component, or shell surface and limited to the missing behavior.

### Razor Pages stay thin

- Razor Pages own routes, page-model binding, and high-level composition.
- Repeated or non-trivial UI belongs in reusable Razor components, partials, or tag-helper-backed building blocks before it is copied across pages.
- Keep page markup focused on page structure. Extract shared shells, repeated cards, data tables, and complex interactive regions into reusable units.

Current examples:

- `Pages/Shared/_Layout.cshtml` owns the shared shell.
- `Components/ConsoleSidebar.razor` and `Components/ConsoleUserAvatar.razor` provide reusable shell UI.
- Stack details render shared live-update surfaces with partials such as `Pages/Stacks/_LiveUpdatePanel.cshtml`.

### Scoped styling before global styling

- Use Razor CSS isolation first for page- or component-specific styling.
- Only add CSS to `wwwroot/css/site.css` when the behavior is truly global across the application shell or multiple unrelated pages.
- Keep Oat customization narrow and local. Prefer small scoped overrides over creating a second styling layer.

Current examples:

- `Pages/Stacks/Details.cshtml.css` owns stack-details styling, including live console views that can rerender from page JavaScript.
- `Pages/Shared/_Layout.cshtml.css` is appropriate for shared shell-only styling.
- `wwwroot/css/site.css` is reserved for the remaining truly global page background treatment used across the application shell.

## 2. JavaScript and progressive enhancement

### HTML-first, JavaScript-second

- Every flow must remain usable through normal links, forms, and full-page responses when JavaScript is unavailable.
- JavaScript enhances working server-rendered behavior. It must not replace the non-JavaScript path.
- When adding interactivity, verify the route still works as a normal page load or form post.

### HTMX is the default interaction layer

- Prefer HTMX attributes for boosted navigation, partial updates, and HTML-over-the-wire behavior.
- Use custom JavaScript only when HTMX alone cannot provide the required experience.
- Pair frontend HTMX behavior with Htmx.Net-aware backend responses and redirects.

### Collocate page-specific scripts

- `wwwroot/js/site.js` is for shell-wide behavior only.
- Page- or view-specific JavaScript belongs in a collocated `.cshtml.js` file.
- Reference the collocated file by the page path, following the Razor Pages pattern from Microsoft docs, for example `~/Pages/Stacks/Details.cshtml.js`.
- The layout loads collocated page scripts through the page path so boosted navigation can update page-specific scripts out-of-band.

Current examples:

- `wwwroot/js/site.js` is limited to shared theme bootstrap, theme mode toggling, and other shell-wide concerns.
- `Pages/Settings/Index.cshtml.js` owns theme preference persistence because that behavior only exists on the settings page.
- `Pages/Stacks/Details.cshtml.js` owns stack-details live update behavior because that logic is specific to the stack details page.

### Approved custom JavaScript exceptions

Custom JavaScript is allowed when the feature needs richer client coordination than HTMX alone can provide. Current approved exceptions are:

- `Pages/Stacks/Details.cshtml.js` for live stack update streaming, SignalR reconnect handling, incremental view materialization, and tab/scroll state restoration.
- The small theme bootstrap in `Pages/Shared/_Layout.cshtml` to apply the initial theme before first paint.

Keep exceptions local to the owning page or shell and document new exceptions in the related change note.

## 3. Full-page and HTMX fragment rendering

- Design page content so the same underlying markup can serve both a direct page request and an HTMX request.
- Reuse partials, components, and shared rendering paths instead of duplicating markup for "full page" and "fragment" versions of the same feature.
- Keep Htmx.Net handling on the server side for HTMX-aware redirects and response behavior.

Current examples:

- `Program.cs` uses `context.Request.IsHtmx()` and `context.Response.Htmx(...)` for HTMX-aware authentication redirects.
- The authenticated shell in `_Layout.cshtml` uses boosted navigation while pages still render as ordinary Razor Pages on direct requests.
- Stack details reuse partials for live update content instead of keeping separate page-only and fragment-only HTML trees.

## 4. Review checklist

Before merging Web Console frontend work, confirm:

- The UI starts from Oat and semantic HTML.
- The implementation uses Oat semantics, built-ins, or utilities before adding new custom classes.
- Any touched custom CSS that duplicates Oat behavior has been removed or collapsed back to Oat.
- Repeated or complex UI is extracted out of the page.
- Styling lives in CSS isolation unless it is truly global.
- Any remaining custom styling is minimal and justified by behavior Oat does not already cover.
- JavaScript is unnecessary, HTMX-first, or a documented local exception.
- Links and forms still work without JavaScript.
- Full-page and HTMX rendering paths reuse the same underlying markup.
