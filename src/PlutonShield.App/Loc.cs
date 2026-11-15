using System.Globalization;
using System.Text.RegularExpressions;

namespace PlutonShield.App;

/// <summary>All texts of the window, in Vietnamese and English. <c>T("key")</c> gives the one for the language in use.</summary>
public static class Loc
{
    // key -> (vi, en)
    private static readonly Dictionary<string, (string vi, string en)> Texts = new()
    {
        ["on"] = ("Bật", "On"),
        ["off"] = ("Tắt", "Off"),

        ["nav.home"] = ("Trang chủ", "Home"),
        ["nav.filters"] = ("Bộ lọc", "Filters"),
        ["nav.lists"] = ("Danh sách chặn", "Filter lists"),
        ["nav.custom"] = ("Bộ lọc của tôi", "My filters"),
        ["nav.stats"] = ("Thống kê", "Statistics"),
        ["nav.settings"] = ("Cài đặt", "Settings"),
        ["nav.update"] = ("Có bản cập nhật mới.", "A new update is available."),

        ["update.title"] = ("Cập nhật ứng dụng", "App update"),
        ["update.versions"] = ("Phiên bản hiện tại: {0} · Phiên bản mới: {1}", "Current version: {0} · New version: {1}"),
        ["update.notes"] = ("Có gì mới", "What's new"),
        ["update.nonotes"] = ("Bản phát hành này chưa có ghi chú.", "This release has no notes."),
        ["update.now"] = ("Cập nhật ngay", "Update now"),
        ["update.later"] = ("Để sau", "Later"),
        ["update.github"] = ("Xem trên GitHub", "View on GitHub"),
        ["update.hint"] = ("Pluton Shield sẽ đóng, cài bản mới và tự mở lại. Nếu bảo vệ đang bật thì nó tự bật lại.", "Pluton Shield will close, install the update and open again. If protection is on, it comes back on by itself."),
        ["update.downloading"] = ("Đang tải bản cập nhật… {0}%", "Downloading the update… {0}%"),
        ["update.ready"] = ("Đã tải xong và kiểm tra. Sẵn sàng cài.", "Downloaded and verified. Ready to install."),
        ["update.waiting"] = ("Bản cập nhật sẽ được tải khi bạn bấm Cập nhật ngay.", "The update is downloaded when you press Update now."),
        ["update.installing"] = ("Đang mở trình cài đặt…", "Starting the installer…"),
        ["update.fail"] = ("Không tải được bản cập nhật: {0}", "Could not download the update: {0}"),
        ["update.installfail"] = ("Không mở được trình cài đặt: {0}", "Could not start the installer: {0}"),
        ["update.none"] = ("Hiện không có bản cập nhật nào.", "There is no update right now."),

        ["home.on"] = ("Bảo vệ đang bật", "Protection is on"),
        ["home.on.sub"] = ("Chặn quảng cáo và theo dõi cho trình duyệt và ứng dụng của bạn. Bộ lọc: {0}.", "Blocking ads and trackers for your browser and apps. Filters: {0}."),
        ["home.starting.title"] = ("Đang bật bảo vệ…", "Turning protection on…"),
        ["home.error"] = ("Không bật được bảo vệ", "Could not turn protection on"),
        ["home.off"] = ("Bảo vệ đang tắt", "Protection is off"),
        ["home.toggle"] = ("Bật hoặc tắt bảo vệ", "Turn protection on or off"),
        ["home.off.sub"] = ("Bật để chặn quảng cáo và theo dõi.", "Turn it on to block ads and trackers."),
        ["home.nofilters.value"] = ("chưa có", "none yet"),
        ["home.cert.title"] = ("Cần chứng chỉ Pluton Shield để lọc trang HTTPS", "The Pluton Shield certificate is needed to filter HTTPS pages"),
        ["home.cert.msg"] = ("Windows sẽ hỏi bạn xác nhận khi cài chứng chỉ. Chứng chỉ được tạo trên máy này và khóa riêng không rời khỏi máy.", "Windows will ask you to confirm the installation. The certificate is created on this computer and its private key never leaves it."),
        ["home.cert.button"] = ("Cài chứng chỉ", "Install certificate"),
        ["home.nofilters.title"] = ("Chưa có bộ lọc", "No filters yet"),
        ["home.nofilters.msg"] = ("Mở trang Bộ lọc và bấm Cập nhật ngay.", "Open the Filters page and press Update now."),
        ["home.blocked.run"] = ("Đã chặn (lần chạy này)", "Blocked (this run)"),
        ["home.blocked.total"] = ("Tổng số đã chặn", "Blocked in total"),
        ["home.pages"] = ("Trang đã dọn sạch", "Pages cleaned"),
        ["home.requests"] = ("Yêu cầu đã xử lý", "Requests handled"),
        ["home.test"] = ("Kiểm tra bảo vệ", "Test protection"),
        ["home.test.hint"] = ("Kiểm tra xem bộ lọc, proxy, chứng chỉ HTTPS và cài đặt Windows có hoạt động không. Không gửi gì ra Internet.", "Checks that the filters, the proxy, the HTTPS certificate and the Windows setting all work. Nothing is sent to the Internet."),
        ["home.test.off"] = ("Hãy bật bảo vệ trước rồi kiểm tra lại.", "Turn protection on first, then test again."),
        ["home.test.pass"] = ("Mọi thứ hoạt động tốt.", "Everything works."),
        ["home.test.fail"] = ("Có vấn đề, xem các dòng bên dưới. Bạn có thể sao chép báo cáo ở Cài đặt → Khắc phục sự cố.", "Something is wrong, see the lines below. You can copy a report in Settings → Troubleshooting."),
        ["selftest.filters"] = ("Đã nạp bộ lọc", "Filters loaded"),
        ["selftest.proxy"] = ("Proxy đang chạy", "Proxy running"),
        ["selftest.https"] = ("Kết nối HTTPS qua Pluton Shield", "HTTPS through Pluton Shield"),
        ["selftest.rules"] = ("Luật chặn quảng cáo", "Ad-blocking rules"),
        ["selftest.system"] = ("Windows dùng Pluton Shield", "Windows uses Pluton Shield"),

        ["lists.title"] = ("Danh sách chặn", "Filter lists"),
        ["lists.intro"] = ("Bật hoặc tắt từng danh sách bộ lọc. Danh sách đã tắt không được dùng để chặn. Thay đổi được áp dụng sau vài giây và được giữ lại khi bộ lọc cập nhật.", "Turn each filter list on or off. A list that is off is not used for blocking. Changes apply within a few seconds and are kept when the filters update."),
        ["lists.summary"] = ("{0} / {1} danh sách đang bật", "{0} of {1} lists on"),
        ["lists.applying"] = ("Đang áp dụng thay đổi…", "Applying changes…"),
        ["lists.applied"] = ("Đã áp dụng", "Applied"),
        ["lists.search"] = ("Tìm danh sách (tên, quốc gia, tên tệp)…", "Search lists (name, country, file name)…"),
        ["lists.reset"] = ("Bật tất cả", "Turn all on"),
        ["lists.group.on"] = ("Bật cả nhóm", "Turn group on"),
        ["lists.group.off"] = ("Tắt cả nhóm", "Turn group off"),
        ["lists.nomatch"] = ("Không có danh sách nào khớp.", "No list matches."),
        ["lists.warn.none"] = ("Tất cả danh sách đang tắt: ngoài luật riêng của bạn, sẽ không chặn gì.", "All lists are off: apart from your own rules, nothing is blocked."),
        ["lists.detail"] = ("{0} quy tắc mạng · {1} quy tắc ẩn phần tử · {2}", "{0} network rules · {1} element-hiding rules · {2}"),
        ["lists.strong"] = ("Mạnh (chặn nhiều hơn, có thể làm hỏng vài trang)", "Strong (blocks more, may break some sites)"),
        ["lists.role.popup"] = ("Chặn cửa sổ bật lên (pop-up)", "Pop-up window blocking"),
        ["lists.role.document"] = ("Luật cho cả trang (trang cảnh báo, trang bị chặn)", "Whole-page rules (warning and blocked pages)"),
        ["lists.custom.hint"] = ("Luật riêng của bạn nằm ở mục “Bộ lọc của tôi” và luôn được dùng.", "Your own rules are under “My filters” and are always used."),
        ["cat.ads"] = ("Quảng cáo", "Ads"),
        ["cat.privacy"] = ("Theo dõi và quyền riêng tư", "Tracking and privacy"),
        ["cat.security"] = ("Mã độc và lừa đảo", "Malware and scams"),
        ["cat.annoyances"] = ("Phiền toái (cookie, tiện ích, mạng xã hội…)", "Annoyances (cookies, widgets, social…)"),
        ["cat.fixes"] = ("Sửa lỗi trang", "Site fixes"),
        ["cat.regional"] = ("Theo ngôn ngữ / quốc gia", "By language / country"),
        ["cat.system"] = ("Danh sách hệ thống (pop-up, luật cả trang)", "System lists (pop-ups, whole-page rules)"),
        ["cat.other"] = ("Khác", "Other"),
        ["filters.title"] = ("Bộ lọc", "Filters"),
        ["filters.version"] = ("Phiên bản: {0}", "Version: {0}"),
        ["filters.none"] = ("Chưa có bộ lọc.", "No filters yet."),
        ["filters.rules"] = ("{0} quy tắc mạng · {1} quy tắc ẩn phần tử", "{0} network rules · {1} element-hiding rules"),
        ["filters.lastcheck"] = ("Kiểm tra lần cuối: {0}", "Last checked: {0}"),
        ["filters.nocheck"] = ("Chưa kiểm tra trong lần chạy này.", "Not checked yet in this run."),
        ["filters.update"] = ("Cập nhật ngay", "Update now"),
        ["filters.updated"] = ("Đã cập nhật", "Updated"),
        ["filters.updated.msg"] = ("Phiên bản {0}", "Version {0}"),
        ["filters.uptodate"] = ("Đã là bản mới nhất", "Already up to date"),
        ["filters.failed"] = ("Cập nhật thất bại", "Update failed"),
        ["filters.note"] = ("Bộ lọc được tải từ bản phát hành mới nhất của plutonlabs/neat-filters trên GitHub. Chỉ các tệp đã thay đổi mới được tải lại, và mỗi tệp được kiểm tra kích thước và SHA-256 trước khi dùng.", "Filters come from the latest release of plutonlabs/neat-filters on GitHub. Only changed files are downloaded again, and every file's size and SHA-256 are checked before use."),
        ["progress.manifest"] = ("Đang tải manifest.json...", "Downloading manifest.json..."),
        ["progress.file"] = ("Đang tải {0} ({1}/{2})...", "Downloading {0} ({1}/{2})..."),

        ["stats.title"] = ("Thống kê", "Statistics"),
        ["stats.summary"] = ("Từ {0}: {1} yêu cầu đã bị chặn. Lần chạy này: {2} yêu cầu, {3} bị chặn, {4} được thay thế an toàn, {5} bỏ tham số theo dõi, {6} trang đã ẩn quảng cáo, {7} popup bị chặn.", "Since {0}: {1} requests blocked. This run: {2} requests, {3} blocked, {4} replaced safely, {5} tracking parameters removed, {6} pages cleaned, {7} pop-ups blocked."),
        ["stats.top"] = ("Tên miền bị chặn nhiều nhất", "Most blocked hosts"),
        ["stats.recent"] = ("Gần đây", "Recent"),

        ["custom.title"] = ("Bộ lọc của tôi", "My filters"),
        ["custom.intro"] = ("Viết quy tắc của riêng bạn, mỗi dòng một quy tắc (cú pháp Adblock Plus / uBlock Origin). Dòng bắt đầu bằng ! là ghi chú. Dòng không hợp lệ bị bỏ qua.", "Write your own rules, one per line (Adblock Plus / uBlock Origin syntax). Lines starting with ! are comments. Invalid lines are ignored."),
        ["custom.examples"] = ("Ví dụ:", "Examples:"),
        ["custom.example1"] = ("||ads.example.com^                      chặn một tên miền", "||ads.example.com^                      block a host"),
        ["custom.example2"] = ("example.com##.banner                    ẩn phần tử trên example.com", "example.com##.banner                    hide an element on example.com"),
        ["custom.example3"] = ("example.com##div:has-text(Sponsored)    ẩn khối chứa chữ “Sponsored”", "example.com##div:has-text(Sponsored)    hide blocks that contain “Sponsored”"),
        ["custom.example4"] = ("@@||example.org^$document              không lọc trang này", "@@||example.org^$document              never filter this site"),
        ["custom.save"] = ("Lưu và áp dụng", "Save and apply"),
        ["custom.try"] = ("Thử một địa chỉ", "Try an address"),
        ["custom.try.hint"] = ("Xem các luật (kể cả luật của bạn) sẽ làm gì với một yêu cầu. Để trống ô trang = yêu cầu đến từ một trang web khác (bên thứ ba).", "See what the rules, yours included, would do with a request. Leave the page empty to test a request that comes from another website (third party)."),
        ["custom.try.url"] = ("Địa chỉ cần thử", "Address to test"),
        ["custom.try.page"] = ("Trang đang mở (không bắt buộc)", "Page it is loaded from (optional)"),
        ["custom.try.type"] = ("Loại", "Type"),
        ["custom.try.button"] = ("Thử", "Test"),
        ["custom.try.badurl"] = ("Hãy nhập địa chỉ đầy đủ, bắt đầu bằng http:// hoặc https://", "Enter a full address starting with http:// or https://"),
        ["custom.try.wait"] = ("Các luật vừa lưu đang được áp dụng, hãy thử lại sau ít giây.", "Your rules are still being applied, try again in a few seconds."),
        ["custom.try.noengine"] = ("Chưa có bộ lọc nào được nạp.", "No filters are loaded yet."),
        ["custom.try.allow"] = ("Cho qua", "Allowed"),
        ["custom.try.block"] = ("Bị chặn", "Blocked"),
        ["custom.try.redirect"] = ("Bị thay bằng nội dung vô hại", "Replaced with a harmless stand-in"),
        ["custom.try.rewrite"] = ("Cho qua nhưng đã bỏ bớt tham số: {0}", "Allowed, but parameters were removed: {0}"),
        ["custom.saved"] = ("Đã áp dụng lúc {0} · {1} dòng quy tắc", "Applied at {0} · {1} rule lines"),
        ["custom.box"] = ("Quy tắc của bạn", "Your rules"),

        ["settings.title"] = ("Cài đặt", "Settings"),
        ["settings.general"] = ("Chung", "General"),
        ["settings.language"] = ("Ngôn ngữ", "Language"),
        ["settings.language.auto"] = ("Tự động (theo Windows)", "Automatic (Windows language)"),
        ["settings.startwithwindows"] = ("Khởi động cùng Windows", "Start with Windows"),
        ["settings.startminimized"] = ("Khởi động thu nhỏ vào khay hệ thống", "Start minimized to the tray"),
        ["settings.filters"] = ("Bộ lọc", "Filters"),
        ["settings.autoupdate"] = ("Tự động cập nhật bộ lọc", "Update filters automatically"),
        ["settings.interval"] = ("Kiểm tra mỗi (giờ)", "Check every (hours)"),
        ["settings.popups"] = ("Chặn cửa sổ popup quảng cáo", "Block advertising pop-up windows"),
        ["settings.cosmetic"] = ("Ẩn các khối quảng cáo trong trang (lọc phần tử)", "Hide ad blocks inside pages (element hiding)"),
        ["settings.https"] = ("HTTPS", "HTTPS"),
        ["settings.filterhttps"] = ("Lọc trang HTTPS (cần chứng chỉ Pluton Shield)", "Filter HTTPS pages (needs the Pluton Shield certificate)"),
        ["settings.excluded"] = ("Trang không bị lọc (mỗi dòng một tên miền, ví dụ: ngan-hang.vn)", "Sites that are never filtered (one host per line, e.g. mybank.com)"),
        ["settings.network"] = ("Mạng", "Network"),
        ["settings.port"] = ("Cổng proxy cục bộ", "Local proxy port"),
        ["settings.save"] = ("Lưu cài đặt", "Save settings"),
        ["settings.saved"] = ("Đã lưu lúc {0}", "Saved at {0}"),
        ["settings.version"] = ("Phiên bản {0}", "Version {0}"),
        ["settings.update.check"] = ("Kiểm tra cập nhật", "Check for updates"),
        ["settings.update.checking"] = ("Đang kiểm tra…", "Checking…"),
        ["settings.update.uptodate"] = ("Bạn đang dùng bản mới nhất. Kiểm tra lúc {0}.", "You have the latest version. Checked at {0}."),
        ["settings.update.found"] = ("Có bản mới: {0}.", "A new version is available: {0}."),
        ["settings.update.view"] = ("Xem và cập nhật", "View and update"),
        ["settings.update.fail"] = ("Không kiểm tra được: {0}", "Could not check: {0}"),
        ["settings.github"] = ("Mở dự án trên GitHub", "Open the project on GitHub"),
        ["settings.appautoupdate"] = ("Tự kiểm tra và tải trước bản cập nhật của ứng dụng", "Automatically check for and download app updates"),
        ["settings.restart"] = ("Khởi động lại Pluton Shield", "Restart Pluton Shield"),
        ["settings.restart.hint"] = ("Thay đổi trong Cài đặt được lưu ngay khi bạn chọn. Nút này đóng và mở lại ứng dụng; bảo vệ tự bật lại nếu đang bật.", "Changes in Settings are saved as soon as you make them. This button closes and reopens the app; protection comes back on by itself if it was on."),
        ["settings.cert"] = ("Chứng chỉ", "Certificate"),
        ["settings.cert.none"] = ("Chưa tạo chứng chỉ. Nó sẽ được tạo khi bạn bật bảo vệ lần đầu.", "No certificate yet. It is created the first time you turn protection on."),
        ["settings.cert.trusted"] = ("Đã được Windows tin cậy · hết hạn {0}", "Trusted by Windows · expires {0}"),
        ["settings.cert.untrusted"] = ("CHƯA được tin cậy · hết hạn {0}", "NOT trusted · expires {0}"),
        ["settings.cert.install"] = ("Cài chứng chỉ", "Install certificate"),
        ["settings.cert.remove"] = ("Gỡ chứng chỉ", "Remove certificate"),
        ["settings.cert.folder"] = ("Mở thư mục chứng chỉ (cho Firefox)", "Open certificate folder (for Firefox)"),
        ["settings.cert.installfail"] = ("Không cài được: {0}", "Could not install: {0}"),
        ["settings.cert.removefail"] = ("Không gỡ được: {0}", "Could not remove: {0}"),
        ["settings.cert.folderfail"] = ("Không mở được thư mục: {0}", "Could not open the folder: {0}"),
        ["settings.cert.firefox"] = ("Chrome và Edge dùng kho chứng chỉ của Windows. Firefox có kho riêng: vào Settings → Privacy & Security → View Certificates → Authorities → Import, chọn tệp PlutonShield-Root-CA.cer, hoặc bật security.enterprise_roots.enabled trong about:config.", "Chrome and Edge use the Windows certificate store. Firefox has its own: Settings → Privacy & Security → View Certificates → Authorities → Import, choose PlutonShield-Root-CA.cer, or set security.enterprise_roots.enabled in about:config."),
        ["settings.trouble"] = ("Khắc phục sự cố", "Troubleshooting"),
        ["settings.resetproxy"] = ("Trả lại cài đặt proxy của Windows", "Restore the Windows proxy setting"),
        ["settings.diag.copy"] = ("Sao chép báo cáo chẩn đoán", "Copy diagnostics report"),
        ["settings.diag.folder"] = ("Mở thư mục nhật ký", "Open the log folder"),
        ["settings.diag.copied"] = ("Đã sao chép. Báo cáo không chứa mật khẩu hay luật riêng của bạn, nhưng hãy đọc lại trước khi gửi cho ai.", "Copied. The report holds no passwords and none of your own rules, but read it before you send it to anyone."),
        ["settings.diag.fail"] = ("Không làm được: {0}", "Could not do that: {0}"),
        ["settings.resetdone"] = ("Đã trả lại cài đặt proxy. Bảo vệ đang tắt.", "Proxy setting restored. Protection is off."),

        ["tray.open"] = ("Mở Pluton Shield", "Open Pluton Shield"),
        ["tray.pause"] = ("Tắt bảo vệ", "Turn protection off"),
        ["tray.resume"] = ("Bật bảo vệ", "Turn protection on"),
        ["tray.update"] = ("Cập nhật bộ lọc", "Update filters"),
        ["tray.quit"] = ("Thoát Pluton Shield", "Quit Pluton Shield"),
        ["tray.tip.on"] = ("Pluton Shield — đang bảo vệ", "Pluton Shield — protecting"),
        ["tray.tip.off"] = ("Pluton Shield — đã tắt", "Pluton Shield — off"),
    };

    private static string _language = "en";

    /// <summary>"vi" or "en".</summary>
    public static string Language
    {
        get => _language;
        set
        {
            var v = value == "vi" ? "vi" : "en";
            if (v == _language) return;
            _language = v;
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    public static event EventHandler? Changed;

    /// <summary>Culture for dates and numbers shown in the window (follows the language in use).</summary>
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(_language == "vi" ? "vi-VN" : "en-US");

    public static string T(string key)
    {
        if (!Texts.TryGetValue(key, out var t)) return key;
        return _language == "vi" ? t.vi : t.en;
    }

    public static string T(string key, params object[] args) =>
        string.Format(Culture, T(key), args);

    private static readonly Regex DownloadFile = new(@"^Downloading (.+) \((\d+)/(\d+)\)\.\.\.$", RegexOptions.Compiled);

    /// <summary>The progress lines of the filter updater come in English from the core; this shows them in the chosen language.</summary>
    public static string Progress(string coreMessage)
    {
        if (coreMessage == "Downloading manifest.json...") return T("progress.manifest");
        var m = DownloadFile.Match(coreMessage);
        return m.Success ? T("progress.file", m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value) : coreMessage;
    }
}
