import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const config = dotnetRuntime.getConfig();

await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);

// ─── WebView iframe appearance fix ──────────────────────────
// Browser-side Avalonia Compositor inserts iframe as an independent DOM layer,
// above the canvas-rendered Avalonia UI. Use z-index to lower the layer +
// raise the #out stacking context to restore correct layer order.
(function fixWebViewIframeAppearance() {
    function patchIframe(iframe) {
        if (iframe.dataset._webviewPatched) return;
        iframe.dataset._webviewPatched = '1';
        iframe.style.border = 'none';
        iframe.style.outline = 'none';
        iframe.style.zIndex = '0';
    }

    document.querySelectorAll('iframe').forEach(patchIframe);

    const mo = new MutationObserver(() => {
        document.querySelectorAll('iframe').forEach(patchIframe);
    });
    mo.observe(document.body, { childList: true, subtree: true });
})();
