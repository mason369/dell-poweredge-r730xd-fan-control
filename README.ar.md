[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# مركز التحكم في مراوح Dell PowerEdge R730xd عبر iDRAC

تطبيق WinUI 3 لنظامي Windows 10/11 للتحكم في مراوح Dell PowerEdge R730xd ومراقبة العتاد عبر iDRAC/IPMI. يجمع السرعات اليدوية، وDell Auto، ومنحنيات حرارة CPU أو الطاقة، ومستشعرات BMC SDR، وFan 1-6 RPM، والرسوم المحلية، والإعدادات المسبقة، وأوامر علبة النظام.

## التنزيل ونطاق الدعم

- نزّل `DellR730xdFanControlCenter-win-x64.zip` من [أحدث إصدار GitHub](https://github.com/mason369/dell-poweredge-r730xd-fan-control/releases/latest)، وفك الحزمة كاملة، ثم شغّل `DellR730xdFanControlCenter.exe`.
- إصدار المصدر الحالي وأحدث Release منشور هما `v1.1.3`، وإصدار ملفات exe/dll في الحزمة هو `1.1.3.0`؛ يشير المصدر والوسم والملفات إلى الإصدار نفسه.
- العتاد المستهدف هو Dell PowerEdge R730xd. تمت الملاحظة محليًا على R730xd مع iDRAC 2.82 فقط؛ أي firmware آخر يحتاج إلى تحقق بوجود مراقب.

## الميزات

- التحكم في جميع المراوح من `0-100%`، وحفظ إعدادات يدوية، أو إعادة التحكم إلى Dell Auto.
- ضبط تلقائي حسب حرارة CPU، مع منحنيات قابلة للتحرير للحرارة-المروحة والطاقة-المروحة.
- تنفيذ فعلي لـ `mc info` و`sdr elist` لعرض الحرارة، وRPM، والطاقة، والجهد، والتيار، والتكرار، والحالات المنفصلة.
- سجل JSONL محلي لسبعة أيام مع ECharts/WebView2، و22 لغة UI، وقائمة علبة النظام، وحفظ اختياري لكلمة المرور بحماية DPAPI.

## بدء سريع

1. فعّل **IPMI over LAN** في iDRAC وتحقق من شبكة الإدارة.
2. شغّل Release بعد فكه، أو شغّل المصدر: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. في صفحة الإعدادات، أدخل عنوان iDRAC/BMC والمستخدم وكلمة المرور. فعّل DPAPI فقط عند الحاجة إلى اتصال تلقائي لاحقًا.
4. عند الحفظ، يُنفّذ `mc info` ثم `sdr elist`. لا يبدأ polling ولا تظهر حالة النجاح إلا بعد نجاح الأوامر وكتابة السجل فعليًا.
5. ابدأ بـ Dell Auto أو بقيم حذرة `20%`/`35%`، وراقب الحرارة وRPM.

## القيم الافتراضية والملفات المحلية

- polling كل `1 s`، ومهلة الأمر `35 s`، وحرارة الهدف/العالية/الطوارئ `68 °C` / `78 °C` / `84 °C`، والمدى التلقائي `10-42%`، والسجل `7` أيام.
- الإعدادات: `%LocalAppData%\DellR730xdFanControlCenter\settings.json`.
- السجلات: `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`.
- المحفوظات وWebView2: `%LocalAppData%\DellR730xdFanControlCenter\chart-history`، `%LocalAppData%\DellR730xdFanControlCenter\WebView2`.
- تُمرّر كلمة المرور إلى `ipmitool -E` عبر `IPMI_PASSWORD`، ولا تظهر في وسائط سطر الأوامر.

## الأخطاء وحدود العتاد

- تُعرض أخطاء المصادقة، والشبكة، وSDR، وWebView2، وأوامر raw، وكتابة السجل وتُسجل؛ ولا تُعرض كنجاح.
- إذا نجح الدخول إلى الوضع اليدوي وفشل أمر النسبة التالي، يرسل التطبيق أمر استعادة Dell Auto مرة واحدة فقط. يبقى الطلب الأصلي فاشلًا ولا يُعاد تنفيذه.
- `0x00-0x05` محددات هدف في firmware، وليست نسبًا مئوية. التحكم المنفرد معطل افتراضيًا؛ وأدى `0x00` إلى رفع سرعة جميع المراوح في الخادم المختبر.
- تنتظر أوامر المستخدم قفل IPMI. عند انشغاله، يُتجاوز polling وtick التلقائي بصورة ظاهرة، ولا تبدأ عملية `ipmitool` ثانية.
- السرعات المنخفضة أو المنحنيات الخاطئة قد ترفع حرارة CPU، والأقراص، وبطاقات PCIe، ومزودات الطاقة. استخدم Dell Auto عند الشك.

## التحقق والوثائق

```powershell
dotnet run --project .\Tests\PresetModelTests\PresetModelTests.csproj -c Release
dotnet build .\DellR730xdFanControlCenter.csproj -c Release -p:Platform=x64
```

لا تستبدل هذه الفحوص التجربة على الخادم الفعلي. لا تغطي بالكامل بدء GUI، وأوامر raw الحقيقية، ونسخ iDRAC firmware الأخرى، وجميع تركيبات DPI. راجع [الدليل الإنجليزي](README.en-US.md)، و[أوامر IPMI](docs/COMMANDS.en-US.md)، و[الأمان](SECURITY.en-US.md).
