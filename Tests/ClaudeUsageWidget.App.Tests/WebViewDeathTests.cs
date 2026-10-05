using System.Runtime.InteropServices;
using ClaudeUsageWidget.App.Web;

namespace ClaudeUsageWidget.App.Tests;

/// <summary>
/// Which exceptions mean the fetch webview is dead and has to be recreated.
/// The WebView2 wrapper rethrows a COMException 0x8007139F as
/// InvalidOperationException with the COMException inside; matching only the
/// outer types left that case looking like a network error until a restart.
/// </summary>
public sealed class WebViewDeathTests
{
    private const int InvalidState = unchecked((int)0x8007139F);
    private const int Disconnected = unchecked((int)0x80010108);

    [Fact]
    public void TheWrappersDisposedErrorIsADeadWebView()
    {
        var ex = new InvalidOperationException(
            "CoreWebView2 members cannot be accessed after the WebView2 control is disposed.",
            new COMException("The group or resource is not in the correct state.", InvalidState));

        Assert.True(ClaudeWebSession.IsWebViewDead(ex));
    }

    [Fact]
    public void ABareInvalidOperationExceptionIsNot()
    {
        Assert.False(ClaudeWebSession.IsWebViewDead(new InvalidOperationException("Collection was modified.")));
    }

    [Fact]
    public void AnInvalidOperationExceptionOverAnotherHResultIsNot()
    {
        var ex = new InvalidOperationException("wrapped", new COMException("E_FAIL", unchecked((int)0x80004005)));

        Assert.False(ClaudeWebSession.IsWebViewDead(ex));
    }

    [Theory]
    [InlineData(InvalidState)]
    [InlineData(Disconnected)]
    public void ARawCOMExceptionOfADeadProcessIsADeadWebView(int hresult)
    {
        Assert.True(ClaudeWebSession.IsWebViewDead(new COMException("dead", hresult)));
    }

    [Fact]
    public void AnObjectDisposedExceptionIsADeadWebView()
    {
        Assert.True(ClaudeWebSession.IsWebViewDead(new ObjectDisposedException("CoreWebView2")));
    }

    [Fact]
    public void AMissingInterfaceIsNot()
    {
        // What the wrapper throws on a runtime without ICoreWebView2_19: an
        // old runtime, not a dead one — recreating the webview would not help.
        Assert.False(ClaudeWebSession.IsWebViewDead(
            new NotImplementedException("Unable to cast to Microsoft.Web.WebView2.Core.Raw.ICoreWebView2_19.")));
    }
}
