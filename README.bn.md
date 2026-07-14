[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# iDRAC দিয়ে Dell PowerEdge R730xd ফ্যান নিয়ন্ত্রণ

iDRAC/IPMI দিয়ে Dell PowerEdge R730xd-এর ফ্যান নিয়ন্ত্রণ ও হার্ডওয়্যার পর্যবেক্ষণের জন্য Windows 10/11 WinUI 3 অ্যাপ। এতে ম্যানুয়াল গতি, Dell Auto, CPU তাপমাত্রা বা পাওয়ার ফ্যান কার্ভ, BMC SDR সেন্সর, Fan 1-6 RPM, লোকাল চার্ট, প্রিসেট ও ট্রে অ্যাকশন একসঙ্গে পাওয়া যায়।

## ডাউনলোড ও সমর্থনের সীমা

- [সর্বশেষ GitHub Release](https://github.com/mason369/dell-poweredge-r730xd-fan-control/releases/latest) থেকে `DellR730xdFanControlCenter-win-x64.zip` নামিয়ে সবকিছু একসঙ্গে এক্সট্র্যাক্ট করুন, তারপর `DellR730xdFanControlCenter.exe` চালান।
- বর্তমান সোর্স ভার্সন `1.1.2`। সর্বশেষ প্রকাশিত Release এখনও `v1.1.0`, এবং তার প্যাকেজের exe/dll ফাইল ভার্সন `1.1.0.0`; মিল থাকা tag তৈরি না হওয়া পর্যন্ত `1.1.2` আনুষ্ঠানিক Release নয়।
- লক্ষ্য হার্ডওয়্যার Dell PowerEdge R730xd। লোকালভাবে শুধু R730xd / iDRAC 2.82 দেখা হয়েছে; অন্য firmware তত্ত্বাবধানে যাচাই করতে হবে।

## বৈশিষ্ট্য

- সব ফ্যান `0-100%` নিয়ন্ত্রণ, ম্যানুয়াল প্রিসেট, অথবা Dell Auto-তে নিয়ন্ত্রণ ফেরত।
- CPU তাপমাত্রা অনুযায়ী স্বয়ংক্রিয় নিয়ন্ত্রণ এবং সম্পাদনাযোগ্য তাপমাত্রা-ফ্যান বা পাওয়ার-ফ্যান কার্ভ।
- তাপমাত্রা, RPM, পাওয়ার, ভোল্টেজ, কারেন্ট, রিডান্ড্যান্সি ও ডিসক্রিট স্টেটের জন্য বাস্তব `mc info` ও `sdr elist` কল।
- ECharts/WebView2 দিয়ে সাত দিনের লোকাল JSONL ইতিহাস, 22 UI ভাষা, ট্রে মেনু এবং DPAPI-সুরক্ষিত ঐচ্ছিক পাসওয়ার্ড সংরক্ষণ।

## দ্রুত শুরু

1. iDRAC-এ **IPMI over LAN** চালু করুন এবং ম্যানেজমেন্ট নেটওয়ার্ক যাচাই করুন।
2. এক্সট্র্যাক্ট করা Release বা সোর্স চালান: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`।
3. সেটিংস পেজে iDRAC/BMC ঠিকানা, ইউজার ও পাসওয়ার্ড দিন। পরে স্বয়ংক্রিয় সংযোগ দরকার হলেই কেবল DPAPI চালু করুন।
4. সংরক্ষণের সময় প্রথমে `mc info`, তারপর `sdr elist` চালে। বাস্তব কমান্ড ও লগ লেখা সফল হওয়ার পরেই polling ও সাফল্য স্ট্যাটাস শুরু হয়।
5. Dell Auto বা সতর্ক `20%`/`35%` দিয়ে শুরু করুন এবং তাপমাত্রা ও RPM নজরে রাখুন।

## ডিফল্ট মান ও লোকাল ফাইল

- polling `1 s`, কমান্ড timeout `35 s`, লক্ষ্য/উচ্চ/জরুরি তাপমাত্রা `68 °C` / `78 °C` / `84 °C`, স্বয়ংক্রিয় রেঞ্জ `10-42%`, ইতিহাস `7` দিন।
- সেটিংস: `%LocalAppData%\DellR730xdFanControlCenter\settings.json`।
- লগ: `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`।
- ইতিহাস/WebView2: `%LocalAppData%\DellR730xdFanControlCenter\chart-history`, `%LocalAppData%\DellR730xdFanControlCenter\WebView2`।
- পাসওয়ার্ড `IPMI_PASSWORD` দিয়ে `ipmitool -E`-তে যায় এবং কমান্ড-লাইন আর্গুমেন্টে থাকে না।

## ব্যর্থতা ও হার্ডওয়্যার সীমা

- প্রমাণীকরণ, নেটওয়ার্ক, SDR, WebView2, raw কমান্ড বা লগ লেখার ব্যর্থতা দেখানো ও রেকর্ড হয়; সাফল্য হিসেবে দেখানো হয় না।
- ম্যানুয়াল মোড সফল হলেও পরের শতাংশ কমান্ড ব্যর্থ হলে অ্যাপ মাত্র একবার Dell Auto পুনরুদ্ধার কমান্ড পাঠায়। মূল অনুরোধ ব্যর্থই থাকে এবং পুনরায় চালানো হয় না।
- `0x00-0x05` firmware target selector, শতাংশ নয়। একক ফ্যান নিয়ন্ত্রণ ডিফল্টভাবে বন্ধ; পরীক্ষিত সার্ভারে `0x00` সব ফ্যানকে দ্রুত ঘুরিয়েছে।
- ব্যবহারকারীর কমান্ড IPMI lock-এর জন্য অপেক্ষা করে। lock ব্যস্ত থাকলে background polling ও automatic tick স্পষ্টভাবে বাদ পড়ে; দ্বিতীয় `ipmitool` process শুরু হয় না।
- কম গতি বা ভুল কার্ভ CPU, ডিস্ক, PCIe কার্ড ও পাওয়ার সাপ্লাই অতিরিক্ত গরম করতে পারে। সন্দেহ হলে Dell Auto ব্যবহার করুন।

## যাচাই ও ডকুমেন্টেশন

```powershell
dotnet run --project .\Tests\PresetModelTests\PresetModelTests.csproj -c Release
dotnet build .\DellR730xdFanControlCenter.csproj -c Release -p:Platform=x64
```

এই যাচাইগুলি বাস্তব সার্ভার পরীক্ষার বিকল্প নয়। GUI শুরু, বাস্তব raw কমান্ড, অন্য iDRAC firmware ও সব DPI কম্বিনেশন সম্পূর্ণভাবে কভার হয় না। [ইংরেজি গাইড](README.en-US.md), [IPMI কমান্ড](docs/COMMANDS.en-US.md) ও [নিরাপত্তা](SECURITY.en-US.md) দেখুন।
