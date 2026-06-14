[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# R730XD 스마트 팬 센터

Dell PowerEdge R730xd용 WinUI 3 iDRAC/IPMI 팬 제어, BMC 센서 모니터링, 기록 차트 도구입니다. 전체 장문 문서는 [简体中文](README.md) 및 [English](README.en-US.md)에 있으며, 이 파일은 한국어 핵심 안내입니다.

## 빠른 시작

1. iDRAC에서 **IPMI over LAN**을 켜고 Windows PC가 관리망에 접근할 수 있는지 확인합니다.
2. 소스에서 실행합니다: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. Settings에서 iDRAC/BMC 주소, 사용자 이름, 비밀번호를 입력합니다. 자동 연결이 필요할 때만 DPAPI 비밀번호 저장을 켭니다.
4. Save settings를 누르면 실제 `mc info`와 `sdr elist`가 실행되며, 성공한 뒤에만 폴링이 시작됩니다.
5. 먼저 Dell Auto 또는 보수적인 20%/35% 팬 속도를 사용하세요. 낮은 팬 속도와 단일 팬 대상 제어는 사람이 지켜보는 상태에서만 확인해야 합니다.

## 중요한 동작

- 명령 실패는 stdout/stderr, UI 상태, JSONL 로그에 표시되며 성공처럼 숨기지 않습니다.
- `RPM`, `W`, `V`, `A`, `°C`, `iDRAC`, `IPMI`, `BMC`, `SDR`, `ipmitool`은 전문 용어 또는 단위라서 번역하지 않습니다.
- 런타임 로그는 `%LocalAppData%\DellR730xdFanControlCenter\logs`에, 차트 기록은 `%LocalAppData%\DellR730xdFanControlCenter\chart-history`에 저장됩니다.
- 팬 제어는 서버 냉각 여유에 직접 영향을 줍니다. 부하, 주변 온도, 센서 상태가 불확실하면 Dell Auto를 우선 사용하세요.
