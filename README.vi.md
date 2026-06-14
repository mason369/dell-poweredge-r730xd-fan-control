[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# Trung tâm quạt thông minh R730XD

Ứng dụng WinUI 3 cho Dell PowerEdge R730xd: điều khiển quạt qua iDRAC/IPMI, giám sát cảm biến BMC và biểu đồ lịch sử. Tài liệu dài đầy đủ có ở [简体中文](README.md) và [English](README.en-US.md); trang này là hướng dẫn cốt lõi bằng tiếng Việt.

## Bắt đầu nhanh

1. Bật **IPMI over LAN** trong iDRAC và xác nhận Windows truy cập được mạng quản lý.
2. Chạy từ mã nguồn: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. Trong Settings, nhập host, người dùng và mật khẩu iDRAC/BMC. Chỉ bật lưu DPAPI nếu cần tự động kết nối lần sau.
4. Save settings chạy `mc info` và `sdr elist` thật; polling liên tục chỉ bắt đầu sau khi thành công.
5. Bắt đầu với Dell Auto hoặc mức thận trọng 20%/35%. Tốc độ thấp và mục tiêu Fan đơn lẻ phải được kiểm tra khi có người theo dõi máy.

## Hành vi quan trọng

- Lỗi hiển thị stdout/stderr, trạng thái UI và log JSONL; không bị che thành thành công.
- `RPM`, `W`, `V`, `A`, `°C`, `iDRAC`, `IPMI`, `BMC`, `SDR` và `ipmitool` được giữ nguyên như thuật ngữ kỹ thuật hoặc đơn vị.
- Log nằm ở `%LocalAppData%\DellR730xdFanControlCenter\logs`; lịch sử biểu đồ ở `%LocalAppData%\DellR730xdFanControlCenter\chart-history`.
- Điều khiển quạt ảnh hưởng trực tiếp đến khả năng tản nhiệt. Nếu tải, nhiệt độ môi trường hoặc cảm biến chưa rõ, hãy dùng Dell Auto.
