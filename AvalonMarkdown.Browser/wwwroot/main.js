import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const config = dotnetRuntime.getConfig();

// WebView 内容由 MarkdownView 控件通过 about:blank + document.write 注入，
// 不再需要 main.js 干预 iframe 导航。
await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
