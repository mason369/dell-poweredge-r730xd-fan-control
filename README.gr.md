[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# R730XD Smart Fan Center

Εφαρμογή WinUI 3 για Dell PowerEdge R730xd: έλεγχος ανεμιστήρων μέσω iDRAC/IPMI, παρακολούθηση αισθητήρων BMC και ιστορικά γραφήματα. Η πλήρης εκτενής τεκμηρίωση υπάρχει στα [简体中文](README.md) και [English](README.en-US.md); αυτή η σελίδα είναι ο βασικός ελληνικός οδηγός.

## Γρήγορη εκκίνηση

1. Ενεργοποιήστε **IPMI over LAN** στο iDRAC και βεβαιωθείτε ότι τα Windows φτάνουν στο δίκτυο διαχείρισης.
2. Εκτελέστε από τον πηγαίο κώδικα: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. Στο Settings εισαγάγετε host, χρήστη και κωδικό iDRAC/BMC. Ενεργοποιήστε DPAPI μόνο αν χρειάζεστε αυτόματη σύνδεση.
4. Το Save settings εκτελεί πραγματικά `mc info` και `sdr elist`; το polling αρχίζει μόνο μετά από επιτυχία.
5. Ξεκινήστε με Dell Auto ή συντηρητικά 20%/35%. Χαμηλές ταχύτητες και μεμονωμένοι στόχοι Fan πρέπει να ελέγχονται με επίβλεψη.

## Σημαντική συμπεριφορά

- Οι αποτυχίες εμφανίζουν stdout/stderr, κατάσταση UI και JSONL log· δεν κρύβονται ως επιτυχία.
- `RPM`, `W`, `V`, `A`, `°C`, `iDRAC`, `IPMI`, `BMC`, `SDR` και `ipmitool` παραμένουν τεχνικοί όροι ή μονάδες.
- Τα logs βρίσκονται στο `%LocalAppData%\DellR730xdFanControlCenter\logs`, το ιστορικό γραφημάτων στο `%LocalAppData%\DellR730xdFanControlCenter\chart-history`.
- Ο έλεγχος ανεμιστήρων επηρεάζει άμεσα την ψύξη. Αν το φορτίο, το περιβάλλον ή οι αισθητήρες είναι αβέβαια, χρησιμοποιήστε Dell Auto.
