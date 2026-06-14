[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# ศูนย์พัดลมอัจฉริยะ R730XD

แอป WinUI 3 สำหรับ Dell PowerEdge R730xd ใช้ควบคุมพัดลมผ่าน iDRAC/IPMI ตรวจสอบเซ็นเซอร์ BMC และดูกราฟย้อนหลัง เอกสารฉบับเต็มอยู่ที่ [简体中文](README.md) และ [English](README.en-US.md); หน้านี้เป็นคู่มือหลักภาษาไทย

## เริ่มใช้งานเร็ว

1. เปิด **IPMI over LAN** ใน iDRAC และตรวจสอบว่า Windows เข้าถึงเครือข่ายจัดการได้
2. รันจากซอร์ส: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`
3. ใน Settings ใส่ host, user และ password ของ iDRAC/BMC เปิด DPAPI เฉพาะเมื่อต้องการเชื่อมต่ออัตโนมัติภายหลัง
4. Save settings จะรัน `mc info` และ `sdr elist` จริง และเริ่ม polling ต่อเนื่องเฉพาะเมื่อสำเร็จ
5. เริ่มด้วย Dell Auto หรือค่าระวังเช่น 20%/35% ความเร็วต่ำและเป้าหมาย Fan เดี่ยวควรทดสอบขณะมีคนเฝ้าดูเครื่อง

## พฤติกรรมสำคัญ

- ความล้มเหลวจะแสดง stdout/stderr สถานะ UI และ log JSONL ไม่ถูกซ่อนเป็นความสำเร็จ
- `RPM`, `W`, `V`, `A`, `°C`, `iDRAC`, `IPMI`, `BMC`, `SDR` และ `ipmitool` เป็นคำเทคนิคหรือหน่วย จึงคงรูปเดิม
- log อยู่ที่ `%LocalAppData%\DellR730xdFanControlCenter\logs`; ประวัติกราฟอยู่ที่ `%LocalAppData%\DellR730xdFanControlCenter\chart-history`
- การควบคุมพัดลมมีผลต่อการระบายความร้อนโดยตรง หากโหลด อุณหภูมิแวดล้อม หรือเซ็นเซอร์ไม่ชัดเจน ให้ใช้ Dell Auto
