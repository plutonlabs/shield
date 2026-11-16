using System.Net;

namespace PlutonShield.Core.Proxy;

/// <summary>The two pages the proxy shows itself (blocked page, blocked pop-up), in Vietnamese or English.</summary>
public static class PageText
{
    private const string Style =
        "body{font:16px/1.5 system-ui,Segoe UI,sans-serif;background:#f4f6f8;color:#1b2430;margin:0;display:grid;place-items:center;min-height:100vh}"
        + "main{max-width:560px;margin:24px;padding:32px;background:#fff;border-radius:12px;box-shadow:0 2px 12px #0002}"
        + "h1{margin:0 0 8px;font-size:22px;color:#c0392b}code{word-break:break-all;font-size:13px;color:#555}";

    private static string Short(string url) => WebUtility.HtmlEncode(url.Length > 300 ? url[..300] + "…" : url);

    public static string BlockedPage(string url, string language)
    {
        var vi = language == "vi";
        return "<!doctype html><html lang=\"" + (vi ? "vi" : "en") + "\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
               + "<title>" + (vi ? "Đã bị PlutonShield chặn" : "Blocked by PlutonShield") + "</title><style>" + Style + "</style></head><body><main>"
               + "<h1>" + (vi ? "Trang này đã bị chặn" : "This page was blocked") + "</h1>"
               + "<p>" + (vi
                   ? "PlutonShield đã chặn địa chỉ này vì nó nằm trong một danh sách bộ lọc (mã độc, lừa đảo hoặc theo dõi)."
                   : "PlutonShield stopped this address because it is on a filter list (malware, phishing or tracking).") + "</p>"
               + "<p><code>" + Short(url) + "</code></p>"
               + "<p>" + (vi
                   ? "Nếu bạn tin tưởng trang này, hãy thêm tên miền vào danh sách loại trừ trong Cài đặt của PlutonShield."
                   : "If you trust this site, add its host to the exclusions in PlutonShield settings.") + "</p></main></body></html>";
    }

    /// <summary>The page for a website blocked by the adult-content filter. It names only the site, not the whole address.</summary>
    public static string AdultBlockedPage(string url, string language)
    {
        var vi = language == "vi";
        var host = HostRules.HostOf(url);
        return "<!doctype html><html lang=\"" + (vi ? "vi" : "en") + "\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
               + "<title>" + (vi ? "Đã bị PlutonShield chặn" : "Blocked by PlutonShield") + "</title><style>" + Style + "</style></head><body><main>"
               + "<h1>" + (vi ? "Trang web nội dung người lớn đã bị chặn" : "Adult website blocked") + "</h1>"
               + "<p>" + (vi
                   ? "PlutonShield đã chặn trang web này vì nó nằm trong danh sách nội dung người lớn."
                   : "PlutonShield blocked this website because it is on the adult-content list.") + "</p>"
               + "<p><code>" + Short(host.Length > 0 ? host : url) + "</code></p>"
               + "<p>" + (vi
                   ? "Bộ lọc này có thể tắt trong PlutonShield, mục Danh sách chặn (Filter lists)."
                   : "You can switch this filter off in PlutonShield, under Filter lists.") + "</p>"
               + "<p><a href=\"javascript:history.back()\">" + (vi ? "Quay lại" : "Go back") + "</a></p></main></body></html>";
    }

    public static string PopupPage(string url, string language)
    {
        var vi = language == "vi";
        return "<!doctype html><html lang=\"" + (vi ? "vi" : "en") + "\"><head><meta charset=\"utf-8\"><title>"
               + (vi ? "Đã chặn popup" : "Pop-up blocked") + "</title><style>" + Style + "</style></head><body><main>"
               + "<h1>" + (vi ? "Đã chặn popup bởi PlutonShield" : "Pop-up blocked by PlutonShield") + "</h1><p><code>" + Short(url) + "</code></p>"
               + "<p>" + (vi
                   ? "Địa chỉ này nằm trong danh sách popup. Nếu bạn mở nó có chủ đích, hãy thêm tên miền vào danh sách loại trừ trong Cài đặt của PlutonShield."
                   : "This address is on the pop-up list. If you opened it on purpose, add its host to the exclusions in PlutonShield settings.") + "</p>"
               + "<p><a href=\"javascript:history.back()\">" + (vi ? "Quay lại" : "Go back") + "</a></p>"
               + "<script>try{window.close()}catch(e){}</script></main></body></html>";
    }
}
