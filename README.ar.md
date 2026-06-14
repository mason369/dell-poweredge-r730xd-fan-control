[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# مركز مراوح R730XD الذكي

تطبيق WinUI 3 مخصص لـ Dell PowerEdge R730xd للتحكم في المراوح عبر iDRAC/IPMI، ومراقبة حساسات BMC، وعرض الرسوم التاريخية. الوثائق الطويلة الكاملة متوفرة في [简体中文](README.md) و [English](README.en-US.md)، وهذه الصفحة مدخل عربي مختصر.

## بدء سريع

1. فعّل **IPMI over LAN** في iDRAC وتأكد أن Windows يستطيع الوصول إلى شبكة الإدارة.
2. شغّل من المصدر: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. في Settings أدخل عنوان iDRAC/BMC واسم المستخدم وكلمة المرور. فعّل حفظ DPAPI فقط إذا كنت تحتاج اتصالا تلقائيا لاحقا.
4. عند Save settings ينفذ التطبيق أوامر حقيقية `mc info` و `sdr elist`؛ ولا يبدأ polling المستمر إلا بعد النجاح.
5. ابدأ بـ Dell Auto أو قيم محافظة مثل 20%/35%. السرعات المنخفضة وأهداف Fan الفردية يجب اختبارها أثناء مراقبة الجهاز.

## سلوك مهم

- تظهر الأخطاء عبر stdout/stderr وحالة الواجهة وسجل JSONL؛ ولا يتم إخفاؤها كنجاح.
- `RPM` و `W` و `V` و `A` و `°C` و `iDRAC` و `IPMI` و `BMC` و `SDR` و `ipmitool` تبقى مصطلحات تقنية أو وحدات.
- السجلات في `%LocalAppData%\DellR730xdFanControlCenter\logs`، وتاريخ الرسوم في `%LocalAppData%\DellR730xdFanControlCenter\chart-history`.
- التحكم بالمراوح يؤثر مباشرة في هامش التبريد. عند عدم وضوح الحمل أو الحرارة أو الحساسات، استخدم Dell Auto.
