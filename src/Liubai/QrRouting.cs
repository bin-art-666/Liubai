namespace Liubai;

internal enum QrRouteKind { Web, WeChatAuthorization, QqAuthorization, ClientOnly, Text }
internal sealed record QrRoute(QrRouteKind Kind, string Title, string Description, Uri? BrowserUri, string DisplayText);

internal static class QrRouting
{
    internal static QrRoute Analyze(string text)
    {
        if (text.Length > 16384 || text != text.Trim() || text.Any(char.IsControl)) return Plain(text);
        if (!Uri.TryCreate(text,UriKind.Absolute,out var uri)) return Plain(text);
        if (uri.Scheme is "weixin" or "mqqapi" or "mqq" or "tencent")
            return new(QrRouteKind.ClientOnly,uri.Scheme=="weixin"?"微信客户端链接":"QQ 客户端链接",
                "这类链接需要对应客户端支持。当前尚未接入该客户端处理路径，没有自动执行链接。",null,text);
        if (uri.Scheme is not ("http" or "https") || uri.Host.Length==0 || uri.UserInfo.Length!=0) return Plain(text);
        string host=uri.IdnHost.ToLowerInvariant().TrimEnd('.');
        string path=Uri.UnescapeDataString(uri.AbsolutePath).TrimEnd('/');
        bool WeChatPath(string expected) => path.Equals(expected,StringComparison.OrdinalIgnoreCase) || path.StartsWith(expected+"/",StringComparison.OrdinalIgnoreCase);
        if (host is "login.weixin.qq.com" or "login.wechat.com" || host=="open.weixin.qq.com" &&
            (WeChatPath("/connect/confirm")||WeChatPath("/connect/qrconnect")||WeChatPath("/connect/oauth2/authorize")))
            return new(QrRouteKind.WeChatAuthorization,"微信登录授权",
                "这是微信授权链接，不能直接交给普通浏览器。当前尚未验证电脑版微信能完成此网站授权；请用手机微信扫一扫原二维码，并在微信中确认。",null,Redacted(uri));
        if (host is "ptlogin2.qq.com" or "login.qq.com" || host.EndsWith(".ptlogin2.qq.com",StringComparison.Ordinal))
            return new(QrRouteKind.QqAuthorization,"QQ 登录授权",
                "这是 QQ 授权链接，不能当普通网页完成扫码。当前未验证电脑版 QQ 的授权处理路径；请使用原登录页提供的快捷登录，或用手机 QQ 扫描原二维码确认。",null,Redacted(uri));
        return new(QrRouteKind.Web,"网页链接",uri.IdnHost,uri,text);
    }
    internal static Uri? BrowserUri(string text) => Analyze(text).BrowserUri;
    static string Redacted(Uri uri) => uri.GetLeftPart(UriPartial.Authority)+"\n（临时授权地址已隐藏）";
    static QrRoute Plain(string text) => new(QrRouteKind.Text,"二维码内容","可以复制内容；未自动执行。",null,text);
}
