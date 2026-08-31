# Flash-Aufteilung

Der Schaltplan nennt einen W25Q16 mit 2 MiB. Der Linker darf ausschließlich die
untere Hälfte verwenden; die obere Hälfte gehört dem Storage-Dienst.

```text
Flash-Offset        Größe      Verwendung
0x000000-0x0FFFFF   1 MiB      Firmware, harte Linker-Grenze
0x100000-0x107FFF   32 KiB     CRC-geschütztes Speed-Journal
0x108000-0x145FFF   248 KiB    aktiver Textbereich, Bank A + Bank B
0x146000-0x183FFF   248 KiB    für spätere Erweiterung reserviert
0x184000-0x1C1FFF   248 KiB    für spätere Erweiterung reserviert
0x1C2000-0x1FFFFF   248 KiB    für spätere Erweiterung reserviert
```

Die vereinfachte Bedienung verwendet genau einen Text. Drei weitere gleich
große Bereiche bleiben reserviert, werden aber weder ausgewählt noch
beschrieben.

Der aktive Bereich besteht aus zwei löschblock-ausgerichteten Banken zu je
`0x1F000` Bytes (31 × 4096). Pro Bank:

```text
0x00000              256-Byte-Header
0x00100              UTF-8-Nutzdaten, maximal 0x1EE00 Bytes
0x1EF00              256-Byte-Commit-Seite
```

Beim Speichern bleibt die gültige Bank unverändert. Die Firmware löscht die
andere Bank, schreibt Header und Inhalt in 256-Byte-Seiten, liest den Inhalt
zur CRC32-Prüfung zurück und programmiert die Commit-Seite zuletzt. Beim Boot
gelten nur Banken mit passendem Header, Grenzen, Inhalt-CRC und Commit-Marker.
Von zwei gültigen Banken gewinnt die neuere Generation. Ein Stromausfall lässt
damit entweder den alten oder den vollständig neuen Text gültig.

Die Datei `firmware/linker/pico_flash_region.ld` begrenzt den Firmwarebereich
auf exakt `0x100000` Bytes. Zusätzliche statische Prüfungen gleichen physische
Flashgröße, Storage-Ende, Seitengrößen und Slotgrenzen ab.
