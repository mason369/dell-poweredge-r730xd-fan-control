[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# R730XD স্মার্ট ফ্যান সেন্টার

Dell PowerEdge R730xd-এর জন্য WinUI 3 অ্যাপ: iDRAC/IPMI দিয়ে ফ্যান নিয়ন্ত্রণ, BMC সেন্সর পর্যবেক্ষণ, এবং ইতিহাসভিত্তিক চার্ট। সম্পূর্ণ দীর্ঘ ডকুমেন্টেশন [简体中文](README.md) এবং [English](README.en-US.md)-এ আছে; এই পৃষ্ঠা বাংলা মূল নির্দেশিকা।

## দ্রুত শুরু

1. iDRAC-এ **IPMI over LAN** চালু করুন এবং Windows থেকে ম্যানেজমেন্ট নেটওয়ার্কে পৌঁছানো যায় কি না যাচাই করুন।
2. সোর্স থেকে চালান: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`।
3. Settings-এ iDRAC/BMC host, user এবং password দিন। ভবিষ্যৎ স্বয়ংক্রিয় সংযোগ দরকার হলে তবেই DPAPI সংরক্ষণ চালু করুন।
4. Save settings বাস্তব `mc info` এবং `sdr elist` চালায়; সফল হলে তবেই polling শুরু হয়।
5. আগে Dell Auto বা সতর্ক 20%/35% ব্যবহার করুন। কম গতি ও একক Fan target মানুষের নজরদারিতে পরীক্ষা করুন।

## গুরুত্বপূর্ণ আচরণ

- ব্যর্থতা stdout/stderr, UI status এবং JSONL log-এ দেখা যায়; এগুলো সফলতা হিসেবে লুকানো হয় না।
- `RPM`, `W`, `V`, `A`, `°C`, `iDRAC`, `IPMI`, `BMC`, `SDR` এবং `ipmitool` প্রযুক্তিগত শব্দ বা unit হিসেবে অপরিবর্তিত থাকে।
- log থাকে `%LocalAppData%\DellR730xdFanControlCenter\logs`; chart history থাকে `%LocalAppData%\DellR730xdFanControlCenter\chart-history`।
- ফ্যান নিয়ন্ত্রণ সরাসরি cooling margin বদলায়। load, ambient temperature বা sensor অস্পষ্ট হলে Dell Auto ব্যবহার করুন।
