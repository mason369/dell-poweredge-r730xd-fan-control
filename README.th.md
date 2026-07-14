[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# ศูนย์ควบคุมพัดลม Dell PowerEdge R730xd ผ่าน iDRAC

แอป WinUI 3 สำหรับ Windows 10/11 ที่ควบคุมพัดลมและตรวจสอบฮาร์ดแวร์ Dell PowerEdge R730xd ผ่าน iDRAC/IPMI รวมการตั้งค่าความเร็วแบบกำหนดเอง, Dell Auto, เส้นโค้งตามอุณหภูมิ CPU หรือกำลังไฟ, เซนเซอร์ BMC SDR, Fan 1-6 RPM, กราฟในเครื่อง, โปรไฟล์ และเมนูถาดระบบ

## ดาวน์โหลดและขอบเขตการรองรับ

- ดาวน์โหลด `DellR730xdFanControlCenter-win-x64.zip` จาก [GitHub Release ล่าสุด](https://github.com/mason369/dell-poweredge-r730xd-fan-control/releases/latest) แตกไฟล์ทั้งหมด แล้วรัน `DellR730xdFanControlCenter.exe`
- ซอร์สปัจจุบันและ Release ที่เผยแพร่ล่าสุดคือ `v1.1.2` โดย exe/dll ในแพ็กเกจมีเวอร์ชันไฟล์ `1.1.2.0` ดังนั้นซอร์ส tag และไบนารีจึงระบุเวอร์ชันเดียวกัน
- ฮาร์ดแวร์เป้าหมายคือ Dell PowerEdge R730xd ทดสอบในเครื่องเฉพาะ R730xd กับ iDRAC 2.82 เท่านั้น firmware อื่นต้องทดสอบขณะมีผู้เฝ้าดู

## ความสามารถ

- ควบคุมพัดลมทั้งหมด `0-100%`, บันทึกโปรไฟล์กำหนดเอง หรือคืนการควบคุมให้ Dell Auto
- ปรับอัตโนมัติตามอุณหภูมิ CPU และเส้นโค้งอุณหภูมิ-พัดลมหรือกำลังไฟ-พัดลมที่แก้ไขได้
- รัน `mc info` และ `sdr elist` จริงเพื่อแสดงอุณหภูมิ, RPM, กำลังไฟ, แรงดัน, กระแส, ความซ้ำซ้อน และสถานะแบบไม่ต่อเนื่อง
- ประวัติ JSONL ในเครื่อง 7 วันด้วย ECharts/WebView2, UI 22 ภาษา, เมนูถาดระบบ และการเก็บรหัสผ่านแบบเลือกได้ที่ป้องกันด้วย DPAPI

## เริ่มใช้งานด่วน

1. เปิด **IPMI over LAN** ใน iDRAC และตรวจสอบเครือข่ายจัดการ
2. รัน Release ที่แตกไฟล์แล้ว หรือรันจากซอร์ส: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`
3. ในหน้าการตั้งค่า ใส่ที่อยู่ iDRAC/BMC, ผู้ใช้ และรหัสผ่าน เปิด DPAPI เมื่อต้องการเชื่อมต่ออัตโนมัติในครั้งถัดไปเท่านั้น
4. เมื่อบันทึก แอปจะรัน `mc info` แล้วจึงรัน `sdr elist` การ polling และสถานะสำเร็จจะเริ่มหลังจากคำสั่งและการเขียน log สำเร็จจริงเท่านั้น
5. เริ่มด้วย Dell Auto หรือค่าระวัง `20%`/`35%` และเฝ้าดูอุณหภูมิกับ RPM

## ค่าเริ่มต้นและไฟล์ในเครื่อง

- polling `1 s`, หมดเวลาคำสั่ง `35 s`, อุณหภูมิเป้าหมาย/สูง/ฉุกเฉิน `68 °C` / `78 °C` / `84 °C`, ช่วงอัตโนมัติ `10-42%`, ประวัติ `7` วัน
- การตั้งค่า: `%LocalAppData%\DellR730xdFanControlCenter\settings.json`
- log: `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`
- ประวัติ/WebView2: `%LocalAppData%\DellR730xdFanControlCenter\chart-history`, `%LocalAppData%\DellR730xdFanControlCenter\WebView2`
- รหัสผ่านถูกส่งให้ `ipmitool -E` ผ่าน `IPMI_PASSWORD` และไม่ปรากฏในอาร์กิวเมนต์ของบรรทัดคำสั่ง

## ความล้มเหลวและขอบเขตฮาร์ดแวร์

- ข้อผิดพลาดด้านการยืนยันตัวตน, เครือข่าย, SDR, WebView2, คำสั่ง raw หรือการเขียน log จะแสดงและบันทึก ไม่แสดงว่าสำเร็จ
- ถ้าเข้าโหมดกำหนดเองสำเร็จ แต่คำสั่งเปอร์เซ็นต์ถัดไปล้มเหลว แอปจะส่งคำสั่งกู้คืน Dell Auto เพียงหนึ่งครั้ง คำขอเดิมยังคงล้มเหลวและไม่ลองซ้ำ
- `0x00-0x05` คือตัวเลือกเป้าหมายของ firmware ไม่ใช่เปอร์เซ็นต์ การควบคุมพัดลมรายตัวถูกปิดโดยค่าเริ่มต้น; `0x00` ทำให้พัดลมทั้งหมดหมุนเร็วในเซิร์ฟเวอร์ทดสอบ
- คำสั่งของผู้ใช้จะรอ lock ของ IPMI เมื่อ lock ไม่ว่าง background polling และ automatic tick จะถูกข้ามอย่างชัดเจน โดยไม่เริ่มโปรเซส `ipmitool` ตัวที่สอง
- ความเร็วต่ำหรือเส้นโค้งที่ผิดอาจทำให้ CPU, ดิสก์, การ์ด PCIe และแหล่งจ่ายไฟร้อนเกินไป หากไม่แน่ใจให้ใช้ Dell Auto

## การตรวจสอบและเอกสาร

```powershell
dotnet run --project .\Tests\PresetModelTests\PresetModelTests.csproj -c Release
dotnet build .\DellR730xdFanControlCenter.csproj -c Release -p:Platform=x64
```

การตรวจเหล่านี้ไม่แทนการทดสอบบนเซิร์ฟเวอร์จริง ยังไม่ครอบคลุมการเริ่ม GUI, คำสั่ง raw จริง, iDRAC firmware อื่น และชุด DPI ทั้งหมด ดู [คู่มือภาษาอังกฤษ](README.en-US.md), [คำสั่ง IPMI](docs/COMMANDS.en-US.md) และ [ความปลอดภัย](SECURITY.en-US.md)
