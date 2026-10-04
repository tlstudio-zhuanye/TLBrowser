using System.Reflection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace TLBrowser;

/// <summary>
/// Microsoft.Web.WebView2.WinForms 只公开了 ZoomFactor，没公开 CoreWebView2Controller。
/// 但拦截网页内的按键（AcceleratorKeyPressed）必须拿到 Controller，
/// 所以这里反射读它的私有字段。版本锁定在 csproj 里，若日后升级包先跑一次自检。
/// </summary>
internal static class WebView2Internals
{
    private static readonly FieldInfo? ControllerField =
        typeof(WebView2).GetField("_coreWebView2Controller",
            BindingFlags.NonPublic | BindingFlags.Instance);

    public static bool Available => ControllerField is not null;

    public static CoreWebView2Controller? GetController(WebView2? view)
    {
        if (view is null || ControllerField is null) return null;
        try { return ControllerField.GetValue(view) as CoreWebView2Controller; }
        catch { return null; }
    }
}
