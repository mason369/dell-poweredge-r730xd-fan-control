[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# iDRAC ile Dell PowerEdge R730xd fan kontrolü

iDRAC/IPMI üzerinden Dell PowerEdge R730xd fanlarını kontrol eden ve donanımı izleyen Windows 10/11 WinUI 3 uygulamasıdır. Manuel hızlar, Dell Auto, CPU sıcaklığı veya güce bağlı fan eğrileri, BMC SDR sensörleri, Fan 1-6 RPM, yerel grafikler, profiller ve sistem tepsisi eylemlerini bir araya getirir.

## İndirme ve kapsam

- [En son GitHub Release](https://github.com/mason369/dell-poweredge-r730xd-fan-control/releases/latest) sayfasından `DellR730xdFanControlCenter-win-x64.zip` dosyasını indirin, tamamını çıkarın ve `DellR730xdFanControlCenter.exe` dosyasını çalıştırın.
- Geçerli kaynak ve en son yayımlanan Release `v1.1.3`dir; paketteki exe/dll dosya sürümü `1.1.3.0`dır, böylece kaynak, tag ve ikili dosyalar aynı sürümü gösterir.
- Hedef donanım Dell PowerEdge R730xd'dir. Yerel olarak yalnızca iDRAC 2.82 kullanan R730xd gözlemlenmiştir; diğer firmware sürümleri gözetim altında doğrulanmalıdır.

## Özellikler

- Tüm fanları `0-100%` arasında kontrol etme, manuel profiller ve kontrolü Dell Auto'ya geri verme.
- CPU sıcaklığına göre otomatik ayar ve düzenlenebilir sıcaklık-fan veya güç-fan eğrileri.
- Sıcaklık, RPM, güç, voltaj, akım, yedeklilik ve ayrık durumlar için gerçek `mc info` ve `sdr elist` çağrıları.
- ECharts/WebView2 ile yedi günlük yerel JSONL geçmişi, 22 UI dili, sistem tepsisi menüsü ve isteğe bağlı DPAPI korumalı parola saklama.

## Hızlı başlangıç

1. iDRAC içinde **IPMI over LAN** özelliğini açın ve yönetim ağını denetleyin.
2. Çıkarılmış Release'i veya kaynağı çalıştırın: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. Ayarlar sayfasında iDRAC/BMC adresi, kullanıcı ve parolayı girin. DPAPI kaydını yalnızca daha sonra otomatik bağlantı gerekiyorsa açın.
4. Kayıt sırasında önce `mc info`, ardından `sdr elist` çalıştırılır. Yoklama ve başarı durumu yalnızca gerçek komut ve log yazma başarısından sonra başlar.
5. Dell Auto veya temkinli `20%`/`35%` değerleriyle başlayın; sıcaklık ve RPM değerlerini izleyin.

## Varsayılanlar ve yerel dosyalar

- Yoklama `1 s`, komut zaman aşımı `35 s`, hedef/yüksek/acil sıcaklık `68 °C` / `78 °C` / `84 °C`, otomatik aralık `10-42%`, geçmiş `7` gün.
- Ayarlar: `%LocalAppData%\DellR730xdFanControlCenter\settings.json`.
- Loglar: `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`.
- Geçmiş ve WebView2: `%LocalAppData%\DellR730xdFanControlCenter\chart-history`, `%LocalAppData%\DellR730xdFanControlCenter\WebView2`.
- Parola `IPMI_PASSWORD` üzerinden `ipmitool -E` komutuna iletilir ve komut satırı argümanlarında yer almaz.

## Hatalar ve donanım sınırları

- Kimlik doğrulama, ağ, SDR, WebView2, raw komut ve log yazma hataları gösterilir ve kaydedilir; başarı olarak sunulmaz.
- Manuel mod etkinleşir ancak sonraki yüzde komutu başarısız olursa uygulama tam olarak bir Dell Auto kurtarma komutu gönderir. İlk istek başarısız kalır ve tekrar denenmez.
- `0x00-0x05` firmware hedef seçicileridir, yüzde değeri değildir. Tek fan kontrolü varsayılan olarak kapalıdır; `0x00` test edilen sunucuda tüm fanları yüksek hıza çıkarmıştır.
- Kullanıcı komutları IPMI kilidini bekler. Kilit meşgulse arka plan yoklaması ve otomatik tick açıkça atlanır; ikinci bir `ipmitool` işlemi başlatılmaz.
- Düşük hızlar veya yanlış eğriler CPU, diskler, PCIe kartları ve güç kaynaklarını aşırı ısıtabilir. Emin değilseniz Dell Auto kullanın.

## Doğrulama ve belgeler

```powershell
dotnet run --project .\Tests\PresetModelTests\PresetModelTests.csproj -c Release
dotnet build .\DellR730xdFanControlCenter.csproj -c Release -p:Platform=x64
```

Bu kontroller hedef sunucudaki testi ikame etmez. GUI başlatma, gerçek raw komutlar, diğer iDRAC firmware sürümleri ve tüm DPI kombinasyonları tam olarak kapsanmamıştır. [İngilizce kılavuz](README.en-US.md), [IPMI komutları](docs/COMMANDS.en-US.md) ve [güvenlik](SECURITY.en-US.md) belgelerine bakın.
