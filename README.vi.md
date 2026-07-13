[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# Trung tâm điều khiển quạt Dell PowerEdge R730xd qua iDRAC

Ứng dụng WinUI 3 cho Windows 10/11, dùng để điều khiển quạt và giám sát phần cứng Dell PowerEdge R730xd qua iDRAC/IPMI. Ứng dụng cung cấp tốc độ thủ công, Dell Auto, đường cong theo nhiệt độ CPU hoặc công suất, cảm biến BMC SDR, Fan 1-6 RPM, biểu đồ cục bộ, cấu hình sẵn và thao tác khay hệ thống.

## Tải xuống và phạm vi hỗ trợ

- Tải `DellR730xdFanControlCenter-win-x64.zip` từ [GitHub Release mới nhất](https://github.com/mason369/dell-poweredge-r730xd-fan-control/releases/latest), giải nén toàn bộ rồi chạy `DellR730xdFanControlCenter.exe`.
- Mã nguồn hiện tại là `1.1.0`. Release `v1.1.0` được xây dựng từ tag tương ứng và các tệp exe/dll trong gói có phiên bản `1.1.0.0`.
- Phần cứng mục tiêu là Dell PowerEdge R730xd. Chỉ R730xd với iDRAC 2.82 được quan sát cục bộ; firmware khác phải được kiểm tra khi có người giám sát.

## Tính năng

- Điều khiển tất cả quạt từ `0-100%`, lưu cấu hình thủ công hoặc trả quyền điều khiển cho Dell Auto.
- Tự động điều chỉnh theo nhiệt độ CPU và đường cong nhiệt độ-quạt hoặc công suất-quạt có thể chỉnh sửa.
- Thực thi `mc info` và `sdr elist` thật để hiển thị nhiệt độ, RPM, công suất, điện áp, dòng điện, dự phòng và trạng thái rời rạc.
- Lịch sử JSONL cục bộ 7 ngày với ECharts/WebView2, 22 ngôn ngữ UI, menu khay hệ thống và lưu mật khẩu tùy chọn được bảo vệ bởi DPAPI.

## Bắt đầu nhanh

1. Bật **IPMI over LAN** trong iDRAC và kiểm tra mạng quản lý.
2. Chạy Release đã giải nén hoặc chạy từ mã nguồn: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. Trên trang cài đặt, nhập địa chỉ iDRAC/BMC, người dùng và mật khẩu. Chỉ bật DPAPI khi cần tự động kết nối lần sau.
4. Khi lưu, ứng dụng chạy `mc info` trước, sau đó `sdr elist`. Việc thăm dò và trạng thái thành công chỉ bắt đầu sau khi lệnh và việc ghi log thực sự thành công.
5. Bắt đầu bằng Dell Auto hoặc giá trị thận trọng `20%`/`35%`, đồng thời theo dõi nhiệt độ và RPM.

## Giá trị mặc định và tệp cục bộ

- Thăm dò `1 s`, hết thời gian lệnh `35 s`, nhiệt độ mục tiêu/cao/khẩn cấp `68 °C` / `78 °C` / `84 °C`, phạm vi tự động `10-42%`, lịch sử `7` ngày.
- Cài đặt: `%LocalAppData%\DellR730xdFanControlCenter\settings.json`.
- Log: `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`.
- Lịch sử/WebView2: `%LocalAppData%\DellR730xdFanControlCenter\chart-history`, `%LocalAppData%\DellR730xdFanControlCenter\WebView2`.
- Mật khẩu được chuyển cho `ipmitool -E` qua `IPMI_PASSWORD` và không xuất hiện trong tham số dòng lệnh.

## Lỗi và giới hạn phần cứng

- Lỗi xác thực, mạng, SDR, WebView2, lệnh raw hoặc ghi log đều được hiển thị và ghi lại; chúng không được coi là thành công.
- Nếu chuyển sang chế độ thủ công thành công nhưng lệnh phần trăm tiếp theo thất bại, ứng dụng chỉ gửi một lệnh khôi phục Dell Auto. Yêu cầu gốc vẫn thất bại và không được tự động thử lại.
- `0x00-0x05` là bộ chọn mục tiêu firmware, không phải phần trăm. Điều khiển từng quạt bị tắt theo mặc định; `0x00` đã làm tất cả quạt quay nhanh trên máy chủ thử nghiệm.
- Lệnh người dùng chờ khóa IPMI. Khi khóa bận, việc thăm dò nền và tick tự động được bỏ qua có thông báo; không khởi chạy tiến trình `ipmitool` thứ hai.
- Tốc độ thấp hoặc đường cong sai có thể làm quá nhiệt CPU, ổ đĩa, card PCIe và nguồn. Hãy dùng Dell Auto khi không chắc chắn.

## Xác minh và tài liệu

```powershell
dotnet run --project .\Tests\PresetModelTests\PresetModelTests.csproj -c Release
dotnet build .\DellR730xdFanControlCenter.csproj -c Release -p:Platform=x64
```

Các kiểm tra này không thay thế thử nghiệm trên máy chủ thực. Khởi động GUI, lệnh raw thật, firmware iDRAC khác và mọi tổ hợp DPI chưa được bao phủ hoàn toàn. Xem [hướng dẫn tiếng Anh](README.en-US.md), [lệnh IPMI](docs/COMMANDS.en-US.md) và [bảo mật](SECURITY.en-US.md).
