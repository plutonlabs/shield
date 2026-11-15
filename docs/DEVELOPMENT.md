# Pluton Shield: tài liệu cho người phát triển

> Tài liệu kỹ thuật (tiếng Việt): cách hoạt động, build, phát hành, giới hạn và những gì đã kiểm tra. README cho người dùng nằm ở [../README.md](../README.md).

Ứng dụng Windows chặn quảng cáo và theo dõi, kiểu AdGuard, xây trên hai repo:

- [`plutonlabs/neat-adblock`](https://github.com/plutonlabs/neat-adblock) — engine lọc (`neat_adblock.dll`, Rust, bọc adblock-rust).
- [`plutonlabs/neat-filters`](https://github.com/plutonlabs/neat-filters) — bộ luật lọc, phát hành tự động trên GitHub Releases.

> **Trạng thái:** v0.3. Lõi (`PlutonShield.Core`, gồm cả proxy) đã được chạy trên Linux với **engine thật, bộ lọc neat-filters thật, thư viện Titanium.Web.Proxy thật và Chromium thật** (xem `tests/e2e`). **Ứng dụng WinUI, phần registry/DPAPI/cài chứng chỉ vào kho Windows và khay hệ thống chưa từng được biên dịch hay chạy** (môi trường viết code không có Windows), hãy coi chúng là code chưa kiểm thử. Xem mục “Đã kiểm tra gì”.

## Cách hoạt động

```
Trình duyệt / ứng dụng ──► proxy HTTP(S) cục bộ 127.0.0.1:8877 ──► Internet
                              │  (Titanium.Web.Proxy, chứng chỉ gốc riêng của máy này)
                              ▼
                         neat_adblock.dll  (neat_check_request, neat_cosmetic_resources, ...)
```

1. Bật bảo vệ → Pluton Shield mở proxy trên 127.0.0.1 và đặt nó làm proxy hệ thống của Windows (người dùng hiện tại). Cài đặt proxy cũ của bạn được lưu lại và **trả lại** khi tắt/thoát, và cả khi khởi động lại sau sự cố.
2. Với HTTPS, Pluton Shield giải mã bằng chứng chỉ gốc tạo trên chính máy này (khóa riêng được bảo vệ bằng DPAPI, không rời máy). Ngân hàng/đăng nhập Microsoft và các tên miền bạn loại trừ được cho đi thẳng, không giải mã.
3. Mỗi yêu cầu được engine kiểm tra: cho qua, chặn (trả phản hồi trống phù hợp loại: ảnh 1×1, script/CSS rỗng, 204…), hoặc thay bằng tài nguyên vô hại (luật `$redirect`). Tham số theo dõi (`$removeparam`) được bỏ khỏi URL.
4. Trang HTML được chèn CSS ẩn phần tử, script (scriptlet) của engine và một script nhỏ của Pluton Shield (`runtime.js`), kèm nonce vào Content-Security-Policy để trang không chặn chúng. Script này lo hai việc CSS không làm được:
   - **Luật ẩn dạng thủ tục** (`:has-text(...)`, `:upward(...)`, `:matches-path(...)`, `:matches-attr(...)`, `:remove()`, `:remove-attr(...)`): chạy phía trang, theo dõi thay đổi DOM (có giới hạn tần suất).
   - **Ẩn phần tử chung theo class/id**: script gom các class/id đang có trong trang, hỏi proxy (địa chỉ nội bộ `plutonshield.invalid`, do proxy tự trả lời, không ra Internet) danh sách selector chung tương ứng từ engine (`neat_hidden_class_id_selectors`) rồi ẩn chúng.
   Luật chỉ cần CSS (kể cả `:style(...)`) vẫn được đổ thẳng vào CSS, không cần script.
5. **Bộ lọc tùy chọn của bạn** (trang “Bộ lọc của tôi”) được ghép thành một danh sách `custom.txt` (không tin cậy: không được dùng luật script-injection quyền cao) cạnh bản phát hành, trong một thư mục ghép bằng hard link; bản phát hành không bị sửa. Lưu là áp dụng ngay, không cần tải lại.
6. Bộ lọc tự cập nhật từ `neat-filters` (mặc định mỗi 6 giờ): chỉ tải tệp đổi (theo SHA-256), kiểm tra kích thước + hash, xây xong mới thay thế; engine được nạp lại không làm gián đoạn duyệt web.

## Giao diện

Trang chủ (công tắc lớn “Bảo vệ đang bật”, số liệu, nút **Kiểm tra bảo vệ**), Bộ lọc (phiên bản, cập nhật ngay), **Danh sách chặn** (bật/tắt từng danh sách của neat-filters, xếp theo nhóm: quảng cáo, theo dõi, mã độc, phiền toái, sửa lỗi, theo quốc gia, hệ thống; có ô tìm kiếm), **Bộ lọc của tôi** (soạn luật riêng, ô **Thử một địa chỉ** để xem luật sẽ chặn hay cho qua), Thống kê (tên miền bị chặn nhiều nhất, nhật ký gần đây), Cài đặt (**phiên bản, nút Kiểm tra cập nhật và nút mở dự án trên GitHub** ngay dưới tiêu đề, ngôn ngữ, cổng, chặn popup, ẩn phần tử, HTTPS, loại trừ, tự khởi động, chứng chỉ, khắc phục sự cố: “trả lại proxy Windows”, **sao chép báo cáo chẩn đoán**, mở thư mục nhật ký) và biểu tượng khay hệ thống (mở, bật/tắt, cập nhật, thoát). Đóng cửa sổ chỉ thu nhỏ vào khay. **Cài đặt lưu ngay khi bạn đổi** (không có nút Lưu); nút **Khởi động lại Pluton Shield** đóng và mở lại app (bảo vệ tự bật lại).

**Kiểm tra bảo vệ** hỏi như một trình duyệt sẽ hỏi, nhưng không cần trình duyệt: đã nạp bộ lọc chưa, proxy có trả lời không, một kết nối HTTPS qua Pluton Shield có mở được không (chứng minh Windows tin chứng chỉ Pluton Shield), luật có chặn các địa chỉ quảng cáo nổi tiếng không, Windows có đang gửi lưu lượng tới Pluton Shield không. Không gửi gì ra Internet. **Báo cáo chẩn đoán** (phiên bản, trạng thái, chứng chỉ, cài đặt, 40 dòng nhật ký cuối) không chứa mật khẩu, luật riêng hay danh sách loại trừ (chỉ số lượng); đính kèm khi báo lỗi.

**Ngôn ngữ:** tiếng Việt và tiếng Anh (Cài đặt → Ngôn ngữ; mặc định theo ngôn ngữ giao diện Windows: tiếng Việt thì dùng tiếng Việt, còn lại tiếng Anh). Đổi là có hiệu lực ngay, kể cả menu khay và các trang “Đã bị chặn / Đã chặn popup” do proxy trả về. Mọi chữ nằm ở `src/PlutonShield.App/Loc.cs` (khóa → (vi, en)); thêm ngôn ngữ mới = thêm một phần tử vào mỗi dòng. Thông báo lỗi từ `PlutonShield.Core` (ví dụ lỗi chứng chỉ) vẫn là tiếng Anh.

**Bộ lọc của tôi** dùng cú pháp Adblock Plus / uBlock Origin, mỗi dòng một luật, `!` là ghi chú, dòng sai bị bỏ qua (không báo lỗi):

```
||ads.example.com^                      chặn một tên miền
example.com##.banner                    ẩn phần tử trên example.com
example.com##div:has-text(Sponsored)    ẩn khối chứa chữ “Sponsored”
@@||example.org^$document               không lọc trang này
```

## Cấu trúc

| Thư mục | Nội dung |
|---|---|
| `src/PlutonShield.Core` | Toàn bộ logic: P/Invoke engine, tải/kiểm tra bộ lọc, proxy, chứng chỉ, proxy hệ thống, thống kê, `ShieldRuntime` (điểm ghép chung). Không phụ thuộc giao diện. |
| `src/PlutonShield.App` | WinUI 3 (không đóng gói MSIX), các trang, khay hệ thống. |
| `src/PlutonShield.Cli` | `plutonshield run | status | update | check <url> | trust-ca | untrust-ca | reset-proxy`. |
| `tests/PlutonShield.Core.Tests` | Bộ kiểm thử dạng console (không cần framework): `dotnet run --project tests/PlutonShield.Core.Tests`. |
| `tests/e2e` | Kiểm thử đầu-cuối trên Linux: proxy thật + Chromium thật + site thử cục bộ (`bash tests/e2e/run.sh`, xem README trong thư mục). Có workflow chạy tay `e2e (manual)` trên GitHub (chưa chạy lần nào). |
| `tests/js` | Kiểm thử `runtime.js` (script chạy trong trang) bằng jsdom: `cd tests/js && npm install && npm test`. |
| `tools/` | `fetch-native.ps1`, `fetch-filters.ps1`, `build.ps1`. |

## Build (trên Windows)

Cần .NET SDK 10 (và Rust + MSVC nếu muốn tự build `neat_adblock.dll` bằng `-Build`).

```powershell
./tools/build.ps1            # tải neat_adblock.dll + bộ lọc, chạy test (C# và JS nếu có Node.js), publish ra dist\x64
./tools/build.ps1 -Arch arm64
```

Chạy `dist\x64\PlutonShield.exe`. Lần đầu bật bảo vệ, Windows sẽ hỏi xác nhận cài chứng chỉ gốc của Pluton Shield cho người dùng hiện tại.

`build.ps1` tạo bản **self-contained** (mang theo .NET, máy không cần cài .NET). Để tạo bộ cài `Setup.exe` từ `dist\<arch>`:

```powershell
.\tools\make-installer.ps1 -Arch x64 -Version 0.4.0   # cần Inno Setup 6 (tự cài bằng Chocolatey nếu thiếu); ra release\PlutonShield-v0.4.0-win-x64-Setup.exe
```

## Cài đặt (người dùng)

Tải `PlutonShield-v<số>-win-x64-Setup.exe` ở trang Releases (máy Intel/AMD; `arm64` chỉ cho Windows chạy chip ARM) và chạy. Cài cho **riêng người dùng hiện tại**, không cần quyền quản trị, vào `%LOCALAPPDATA%\Programs\PlutonShield`.

- Windows SmartScreen có thể cảnh báo vì bộ cài chưa ký số: bấm **More info → Run anyway**.
- Cài đè lên bản cũ được: bộ cài tự đóng Pluton Shield đang chạy (và trả lại cài đặt proxy của Windows) rồi thay tệp.
- Gỡ trong **Settings → Apps → Installed apps**: bộ cài đóng app, trả lại proxy, **xóa chứng chỉ gốc Pluton Shield** khỏi danh sách tin cậy (Windows sẽ hỏi xác nhận) và hỏi có xóa cài đặt/bộ lọc riêng ở `%LOCALAPPDATA%\PlutonShield` không.
- Bản `.zip` (giải nén và chạy `PlutonShield.exe`) vẫn có, kèm công cụ dòng lệnh `cli\` (cần .NET 10 runtime).

Nếu mất mạng sau khi app sập: chạy `plutonshield reset-proxy` (hoặc nút “Trả lại cài đặt proxy của Windows”).

**Tắt máy / khởi động lại / đăng xuất:** Windows kết thúc tiến trình ngay sau thông báo `WM_ENDSESSION` mà không chạy mã thoát của .NET, nên app lắng nghe thông báo đó và trả lại proxy trước. Nếu không, lần mở máy sau trình duyệt sẽ trỏ vào một proxy không ai trả lời (“mất mạng”) cho đến khi Pluton Shield chạy lại. Mất điện hoặc treo máy thì không có thông báo nào: mở Pluton Shield (app tự trả lại proxy cũ khi khởi động) hoặc dùng `reset-proxy`.

**Danh sách chặn:** bản neat-filters có khoảng 70 danh sách (EasyList, uBlock, AdGuard, danh sách theo nước…). Danh sách bị tắt được lưu **theo tên tệp** trong cài đặt (`DisabledLists`), nên bản phát hành sau thêm danh sách mới thì danh sách đó mặc định bật. Khi nạp engine, thư mục ghép (cùng cơ chế với “Bộ lọc của tôi”, hard link) có `manifest.json` đã bỏ các mục bị tắt; `resources.json` không tắt được. Bấm nhiều công tắc liên tiếp chỉ nạp lại một lần (chờ 0,4 giây, bỏ qua nếu engine đang dùng đã đúng). Nhóm do app tự suy ra từ vai trò, cờ quốc gia trong tên và từ khóa trong tên tệp (neat-filters chưa ghi mục đích của từng danh sách); danh sách không khớp vào “Khác” và vẫn hoạt động bình thường. Tắt hết thì chỉ còn luật riêng của bạn.

**Khởi động cùng Windows:** mục trong registry lưu **đường dẫn** của bản đang chạy; mỗi lần app mở nó tự sửa lại cho đúng (sau cài lại, đổi thư mục, di chuyển bản zip đường dẫn cũ sẽ sai và Windows im lặng không chạy gì). Bật công tắc trong app cũng bỏ trạng thái “Off” mà bạn từng đặt cho Pluton Shield trong Windows Settings → Apps → Startup. “Khởi động thu nhỏ vào khay” chỉ áp dụng cho lần chạy cùng Windows; tự mở app thì cửa sổ luôn hiện. Báo cáo chẩn đoán có dòng `Autostart` cho biết lệnh Windows sẽ chạy, tệp có còn không, và mục có bị tắt trong Windows không.

**Tự cập nhật ứng dụng:** app hỏi `https://api.github.com/repos/plutonlabs/shield/releases/latest` (bản chính thức mới nhất; bản nháp và pre-release bị bỏ qua) sau khoảng 45 giây kể từ lúc mở rồi cứ 12 giờ một lần (1 giờ sau một lần lỗi), và so với số phiên bản đã được ghi vào app lúc build (theo tag; `0.4.0-rc.1` cũ hơn `0.4.0`). Có bản mới thì app **tải sẵn** `PlutonShield-v<số>-win-<arch>-Setup.exe` vào `%LOCALAPPDATA%\PlutonShield\updates`, kiểm tra kích thước và SHA-256 trong tệp `.sha256` đi kèm, và hiện **biểu tượng chuông** “A new update is available.” trên mục Cài đặt (thanh bên thu gọn thì chỉ còn chuông có chấm, rê chuột để đọc chữ). Bấm vào đó mở trang cập nhật (phiên bản hiện tại/mới, ghi chú phát hành, **Cập nhật ngay**, Để sau). **Cập nhật ngay** chạy bộ cài ở chế độ im lặng (`/SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH=1`): bộ cài tự đóng app (trả lại proxy), thay tệp và mở lại app (`--restarted`; bảo vệ tự bật lại nếu trước đó đang bật). Không bao giờ cài mà bạn chưa bấm. Công tắc **Cài đặt → Tự kiểm tra và tải trước bản cập nhật của ứng dụng** tắt việc kiểm tra tự động (nút Kiểm tra cập nhật vẫn dùng được; khi đó bản cài được tải lúc bạn bấm Cập nhật ngay).

- Chỉ tải tệp có địa chỉ `https://github.com/plutonlabs/shield/releases/download/…`; tệp `.sha256` nằm cùng Release nên chỉ chống tải hỏng/cụt, **không** chống Release bị chiếm (chưa ký số; muốn chắc hơn thì ký bộ cài bằng một khóa riêng và nhúng khóa công khai vào app).
- Release vừa tạo mà chưa đủ tệp (workflow còn đang đính kèm) thì app coi là “chưa có bản mới” và hỏi lại lần sau.
- Muốn thử mà không cần phát hành: đặt biến môi trường `PLUTONSHIELD_FAKE_VERSION=0.0.1` rồi mở app (app tưởng mình là bản cũ nên bản phát hành mới nhất được coi là bản cập nhật).
- Bản chạy từ `.zip` cũng nhận thông báo, nhưng bộ cài sẽ cài một bản **riêng** vào `%LOCALAPPDATA%\Programs\PlutonShield` và mở bản đó (bản zip giữ nguyên).
- Cập nhật bộ lọc là việc khác, chạy riêng (trang Bộ lọc).

Dòng lệnh: `plutonshield run [--no-system-proxy]` (chỉ mở proxy trên `127.0.0.1:<cổng>`, không đụng cài đặt proxy Windows: dùng cho một trình duyệt riêng, ví dụ `chrome --proxy-server=127.0.0.1:8877`), `selftest`, `diag`, `status`, `update`, `check <url> [loại] [trang]`, `trust-ca`, `reset-proxy`.

## Phát hành bằng tag

Workflow `.github/workflows/release.yml` chạy khi bạn đẩy một **tag bắt đầu bằng `v`**: nó build x64 và arm64 (có chạy test), tạo bộ cài `PlutonShield-<tag>-win-<arch>-Setup.exe` và gói `PlutonShield-<tag>-win-<arch>.zip` (mỗi tệp kèm `.sha256`), rồi tạo GitHub Release với ghi chú tự sinh.

```bash
git add -A
git commit -m "Pluton Shield 0.3.0"
git push                                   # đẩy mã lên nhánh chính trước

git tag -a v0.3.0 -m "Pluton Shield 0.3.0"    # tạo tag
git push origin v0.3.0                     # đẩy tag => workflow "release" chạy, tạo Release sau vài phút
```

- Tên tag phải có dạng `vX.Y.Z` (bản chính thức) hoặc `vX.Y.Z-rc.1`, `vX.Y.Z-beta.1` (có dấu `-` thì được đánh dấu **pre-release**). Số phiên bản sau chữ `v` được ghi vào ứng dụng (hiện trong báo cáo chẩn đoán).
- Nên thử lần đầu bằng một tag pre-release (`v0.3.0-rc.1`); nếu ổn thì tạo `v0.3.0`.
- Tag sai hoặc build lỗi: xóa Release (trang Releases) rồi xóa tag, sửa, tạo lại:
  `git tag -d v0.3.0 && git push origin :refs/tags/v0.3.0`
- Mỗi lần phát hành tăng số: sửa lỗi nhỏ `v0.3.1`, thêm tính năng `v0.4.0`, thay đổi lớn `v1.0.0`.
- Có thể tạo Release trước trên trang GitHub (nút “Create a new release” + tag mới) hoặc chỉ đẩy tag: cả hai đều được. Nếu Release đã có, workflow giữ nguyên tiêu đề và ghi chú của bạn và chỉ **đính kèm** các tệp zip (`gh release upload --clobber`); nếu chưa có, nó tự tạo.
- Nếu `release` đỏ ở job `publish` mà `build` xanh: vào run đó bấm **Re-run failed jobs** (các tệp zip đã nằm trong Artifacts của run, không build lại).
- Workflow `build` vẫn chạy ở mỗi lần push và chỉ tạo artifact (không tạo Release): `PlutonShield-<arch>` (thư mục chạy được) và `PlutonShield-Setup-<arch>` (bộ cài thử, nén nhanh, phiên bản `0.0.0-ci`).

## Giới hạn đã biết

- **Firefox** dùng kho chứng chỉ riêng: nhập `PlutonShield-Root-CA.cer` thủ công (hướng dẫn trong Cài đặt) hoặc bật `security.enterprise_roots.enabled`.
- Ứng dụng ghim chứng chỉ (certificate pinning) sẽ lỗi khi bị giải mã: thêm tên miền vào danh sách loại trừ. Một số tên miền Microsoft/Dropbox/Webex đã được loại sẵn.
- **Chặn popup** là phỏng đoán ở phía proxy: proxy không thấy “cửa sổ mới”, nên coi mọi điều hướng trang từ site khác (Sec-Fetch-Site: cross-site, hoặc Referer khác site) là ứng viên và hỏi danh sách popup của bộ lọc (`popups.txt`). Cửa sổ bị chặn tự đóng; nếu thực ra là bấm link trong cùng tab tới một tên miền có trong danh sách popup, bạn thấy trang “Pop-up blocked” và có thể quay lại hoặc thêm tên miền vào loại trừ. Không chặn được popup do script mở bằng `about:blank` rồi gán địa chỉ sau, hay popup mở từ trang không đi qua proxy.
- **Ẩn phần tử thủ tục / chung theo class/id** cần script chạy trong trang. Đã thử trong Chromium thật (CSP chặt, CSP không có `connect-src`, CSP đặt qua thẻ `<meta>`, gzip/brotli/chunked đều chạy), **chưa thử trong Firefox/Safari**. Script bị vô hiệu ở trang chặn cả nonce (hiếm). Nếu endpoint `plutonshield.invalid` không tới được, luật thủ tục vẫn chạy, chỉ phần “chung theo class/id” bị mất.
- Ở trang không có `generichide`, engine trả hàng nghìn selector chung (~380 KB CSS) và Pluton Shield chèn tất cả vào mỗi trang HTML (tối đa 20 000 selector). Đo trên Linux: thêm khoảng 5–15 ms mỗi trang HTML; trình duyệt vẫn phải phân tích CSS đó. Nếu thấy chậm, tắt “Ẩn các khối quảng cáo trong trang” trong Cài đặt.
- Luật sai cú pháp trong “Bộ lọc của tôi” bị engine bỏ qua mà không báo (dùng ô “Thử một địa chỉ” để kiểm tra luật chặn; luật ẩn phần tử thì mở một trang thật).
- Chưa có: chạy như dịch vụ Windows, tự cài chứng chỉ cho Firefox, ngôn ngữ thứ ba.
- Chứng chỉ gốc có hiệu lực ~13 tháng; khi hết hạn app tạo cái mới và cần cài lại.
- Trình cập nhật không đi qua proxy hệ thống (để khỏi vòng lặp), nên mạng bắt buộc dùng proxy công ty sẽ không cập nhật được.

## Đã kiểm tra gì

| Phần | Cách kiểm tra |
|---|---|
| Đọc/kiểm tra manifest, tải bản phát hành thật, kiểm tra SHA-256, cache engine | Chạy thật với `neat-filters` release thật (lần 2 = “up to date”, nạp lại từ cache) |
| Engine qua P/Invoke | Chạy thật với `libneat_adblock.so` build từ repo (chặn/redirect/CSP/cosmetic) |
| Phân loại request, luật host, CSP nonce, chèn HTML (cả mức byte), data: URL, cài đặt, DPAPI-wrapper (nhánh không-Windows) | `tests/PlutonShield.Core.Tests` — chạy qua |
| Chặn popup (nhận diện điều hướng cross-site, danh sách `popups.txt` thật), luật ẩn thủ tục + selector chung (`CosmeticBuilder`) | `tests/PlutonShield.Core.Tests` + chạy với engine thật và bộ luật thật |
| `runtime.js` (`:has-text`, `:upward`, `:matches-path`, `:matches-attr`, `:remove`, `:remove-attr`, ẩn theo class/id, giới hạn tần suất, chạy một lần mỗi cửa sổ) | `tests/js` — 11 test trong jsdom; payload thật do engine thật sinh ra cũng được nạp vào jsdom |
| Bộ lọc tùy chọn (`ComposedRelease`, nạp lại engine, luật tùy chọn chặn/ẩn thật qua engine) | `tests/PlutonShield.Core.Tests` + chạy với engine thật |
| Văn bản vi/en (khóa đủ hai ngôn ngữ, tham số `{n}` khớp nhau, đổi ngôn ngữ, trang chặn/popup) | `tests/PlutonShield.Core.Tests` |
| Tự cập nhật: so phiên bản (rc, số, `+build`), đọc trả lời của GitHub (nháp/pre-release/thiếu tệp/địa chỉ lạ/kích thước), tải + kiểm tra SHA-256 (hỏng, cụt, dài quá, `.sha256` sai), `AppUpdateService` (trạng thái, lỗi, lịch, dọn tệp cũ), mọi khóa chữ dùng trong cửa sổ đều có vi và en | `tests/PlutonShield.Core.Tests` (`UpdateTests.cs`) với máy chủ giả; **chưa thử với GitHub thật** |
| `ProxyService`, `CertificateService`, `ShieldRuntime`, CLI (Titanium 8.0.2 biên dịch từ mã nguồn, BouncyCastle từ mã nguồn) | `tests/e2e` + chạy tay trên Linux: HTTP/HTTPS giải mã, chứng chỉ upstream tự ký bị từ chối (502), chặn host, trang chặn, WebSocket, SSE, POST 5 MB, tải 300 MB (~500 MB/s, RAM không tăng), 200 yêu cầu song song, `selftest` |
| Chromium thật qua proxy: ẩn phần tử, `:has-text`, ẩn theo class/id qua `plutonshield.invalid`, CSP (header, thiếu connect-src, thẻ meta), nén, popup | `tests/e2e/check.js` — 45 kiểm tra đều đạt |
| `SystemProxy` (registry), `StartupRegistration`, DPAPI, cài chứng chỉ vào kho Windows, WinUI App (gồm các trang mới, chuông cập nhật và trang cập nhật, `L.cs`, đổi ngôn ngữ tại chỗ, nút kiểm tra, ô thử địa chỉ), khay, chạy bộ cài im lặng và tự mở lại (`/RELAUNCH=1`) | **Chưa biên dịch, chưa chạy** (cần Windows) |
| Firefox, Edge/Chrome trên Windows | **Chưa thử** (Chromium trên Linux đã thử) |
