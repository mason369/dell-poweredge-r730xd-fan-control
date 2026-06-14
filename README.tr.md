[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# R730XD Akıllı Fan Merkezi

Dell PowerEdge R730xd için WinUI 3 uygulaması: iDRAC/IPMI ile fan kontrolü, BMC sensör izleme ve geçmiş grafikleri. Tam uzun dokümantasyon [简体中文](README.md) ve [English](README.en-US.md) dosyalarındadır; bu sayfa Türkçe temel girişidir.

## Hızlı başlangıç

1. iDRAC içinde **IPMI over LAN** özelliğini açın ve Windows'un yönetim ağına erişebildiğini doğrulayın.
2. Kaynaktan çalıştırın: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. Settings bölümünde iDRAC/BMC host, kullanıcı ve parola bilgilerini girin. DPAPI kaydını yalnızca otomatik bağlantı gerekiyorsa açın.
4. Save settings gerçek `mc info` ve `sdr elist` komutlarını çalıştırır; polling yalnızca başarıdan sonra başlar.
5. Dell Auto veya 20%/35% gibi temkinli değerlerle başlayın. Düşük hızlar ve tek Fan hedefleri gözetim altında doğrulanmalıdır.

## Önemli davranış

- Hatalar stdout/stderr, UI durumu ve JSONL log ile görünür olur; başarı gibi gizlenmez.
- `RPM`, `W`, `V`, `A`, `°C`, `iDRAC`, `IPMI`, `BMC`, `SDR` ve `ipmitool` teknik terim veya birim olarak korunur.
- Loglar `%LocalAppData%\DellR730xdFanControlCenter\logs`, grafik geçmişi `%LocalAppData%\DellR730xdFanControlCenter\chart-history` altındadır.
- Fan kontrolü soğutma payını doğrudan etkiler. Yük, ortam sıcaklığı veya sensörler belirsizse Dell Auto kullanın.
