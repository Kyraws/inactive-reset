# Chrome Modern Web Guidance for Inactive Reset

Reviewed 2026-11-25. Google’s Modern Web Guidance is a collection of coding-agent skills and a guide catalog for HTML, CSS, JavaScript, accessibility, security, and performance. The useful scope here is the Practice workspace shared by the Windows WebView2 host and `cli serve`. The guides are available without installing anything. [Chrome overview](https://developer.chrome.com/docs/modern-web-guidance) · [Project architecture](../ARCHITECTURE.md)

## Where the guidance fits

| Area | Application to Inactive Reset |
| --- | --- |
| Dialogs and keyboard access | The workspace already uses native `<dialog>` and `showModal()`. The Return to LMU’s main menu, Use track defaults, Save session preset, and Preset details dialogs lack `aria-labelledby` or `aria-label`. Link each to its visible heading and verify initial focus, Escape, and focus return in WebView2. Plan review already handles Escape specially while applying a preset. [Workspace markup](../../src/InactiveReset.Ui/Lab.html) · [Chrome accessibility guide](https://developer.chrome.com/docs/modern-web-guidance/guides/accessibility/accessibility) · [W3C modal dialog pattern](https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/) |
| Content Security Policy | [Server.cs](../../src/InactiveReset.Ui/Server.cs) serves the workspace over loopback without a CSP header. `Lab.html` has deferred local scripts, inline event handlers, and an inline style block. Inventory those and generated styles before exercising a report-only policy in the test host. Trusted Types enforcement would also require reviewing HTML string rendering. Review embedded navigation alongside CSP. [MainWindow.cs](../../src/InactiveReset.App/MainWindow.cs) checks the source of incoming native messages and routes popups externally. Microsoft also recommends checking top-level and frame navigation. [Chrome security guide](https://developer.chrome.com/docs/modern-web-guidance/guides/security/security) · [Microsoft WebView2 security guide](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security) |
| Responsive layout | Try a size container query if a component needs to adapt to its allocated width. The lap picker and its two columns in [Lab.workspace.css](../../src/InactiveReset.Ui/Lab.workspace.css) are a candidate if labels crowd at narrow panel widths. Keep viewport media queries for overall layout changes and reduced-motion preferences. Verify at the 1040×680 desktop minimum and at a narrower browser viewport. [Chrome size-aware styling guide](https://developer.chrome.com/docs/modern-web-guidance/guides/css/size-aware-styling) · [Project testing guide](../TESTING.md) |
| Performance | If recorded-lap scrubbing or a refresh feels slow, trace renderer work and HTTP wait before changing polling or rendering. Use Edge DevTools in a development WebView2 host for results that include the embedded surface. The shipped host disables its DevTools UI. [Microsoft WebView2 performance guide](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/performance) · [Chrome performance reference](https://developer.chrome.com/docs/devtools/performance/reference) |

## Compatibility and optional Codex setup

Use the supported WebView2 Runtime and the intended `cli serve` browsers as compatibility targets. Baseline tracks desktop Edge and other mainstream browsers. WebView2 has its own runtime distribution, and the [browser test harness](../../tests/InactiveReset.BrowserTests/Program.cs) already prints the runtime version it exercises. Use that test path for desktop behavior and verify any new feature against the intended browser targets. The default Modern Web Guidance target, Baseline Widely available, is a conservative filter for shared web features. [Baseline definition](https://web.dev/baseline) · [Microsoft Runtime distribution](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution) · [Project QA](../QA_AUTOMATION.md)

Chrome lists the following commands for Codex. Its alternative installer, `npx modern-web-guidance@latest install`, enables automatic updates. The individual guides can also be read directly in the catalog. [Chrome installation instructions](https://developer.chrome.com/docs/modern-web-guidance/get-started#codex) · [Guide catalog](https://developer.chrome.com/docs/modern-web-guidance/guides)

```powershell
codex plugin marketplace add GoogleChrome/modern-web-guidance
codex plugin add modern-web-guidance@googlechrome
```

Start with the four dialog names and a focused keyboard check. The public guides cover that work directly. Consider installing the Codex plugin if it will be used for recurring frontend work.

This review used source inspection. Runtime behavior, performance, and the scanner were outside its scope.
