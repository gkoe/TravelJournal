# TravelJournal

**Aus einem Urlaubsordner wird eine vollständige Reisedokumentation.**

TravelJournal ist eine Windows-Desktop-App, die GPS-Fotos kuratiert, automatisch Streckenkarten erzeugt und daraus eine Vollbild-Diashow oder eine teilbare Web-Präsentation baut — alles lokal, ohne Cloud, ohne Abo.

---

## Features

| | |
|---|---|
| 📂 **Ordner öffnen & scannen** | JPEG und HEIC-Fotos einlesen, neu hinzugefügte Fotos per Rescan erkennen |
| 📱 **HEIC importieren** | iPhone-Fotos nach JPEG konvertieren, Originale automatisch löschen |
| ✏️ **Fotos umbenennen** | `IMG_4711.jpg` → `2025_07_14_09_23_Salzburg.jpg` |
| ✅ **Kuratieren** | Fotos per Tastatur auswählen / abwählen / als Start- oder Schlussfolie markieren |
| 🔄 **Drehen & Zuschneiden** | Nicht-destruktiv drehen, mit `S` dauerhaft speichern; Ausschnitt per Maus aufziehen |
| 📍 **Ortserkennung** | GPS-Koordinaten → Ortsnamen via OpenStreetMap / Nominatim |
| 🗺️ **Streckenkarten** | Automatische Karte pro Reise-Stopp via MapTiler (PNG, direkt neben den Fotos) |
| 🎞️ **Vollbild-Präsentation** | Diashow mit Ort, Datum und Beschreibung als Einblendung (`Strg+P`) |
| 🌐 **Web-Export** | Selbstständig lauffähiger HTML/CSS/JS-Ordner, keine Serverinstallation nötig |

---

## Screenshots

> *Folgen demnächst.*

---

## Schnellstart

### 1 — Herunterladen

**[→ Releases](https://github.com/gkoe/TravelJournal/releases)** · neuestes ZIP für Windows 64-Bit

### 2 — Entpacken & konfigurieren

ZIP in einen beliebigen Ordner entpacken und `appsettings.json` öffnen:

```json
{
  "MapTiler": {
    "ApiKey": "DEIN_KEY_HIER"
  },
  "Nominatim": {
    "ContactEmail": "deine@email.at"
  }
}
```

- **MapTiler API Key** — kostenlosen Account auf [cloud.maptiler.com](https://cloud.maptiler.com/auth/widget) anlegen (Free-Plan: 100 000 Tiles/Monat)
- **Nominatim E-Mail** — deine Adresse als Kontaktinfo für den OpenStreetMap-Dienst (kein Account nötig)

> Ohne MapTiler-Key kann keine Karte generiert werden — alle anderen Funktionen laufen ohne API-Key.

### 3 — Starten

```
TravelJournal.exe
```

Keine Installation, keine Admin-Rechte. Beim ersten Start zeigt Windows Defender eine SmartScreen-Warnung (fehlende Code-Signierung) — **Weitere Informationen → Trotzdem ausführen**.

---

## Typischer Ablauf

```
1.  Ordner öffnen          →  Foto-Ordner vom Urlaub wählen
2.  HEIC importieren        →  (nur bei iPhone-Fotos nötig)
3.  Fotos umbenennen        →  einmalig, für sortierbare Dateinamen
4.  Fotos kuratieren        →  1 = auswählen, 2 = abwählen, Leertaste = zyklisch
5.  Orte ermitteln          →  GPS-Koordinaten → Ortsnamen (braucht Internet)
6.  Karten generieren       →  Strg+M  (optional)
7.  Titel & Beschreibungen  →  im Detailbereich rechts
8.  Präsentation starten    →  Strg+P
9.  Web-Export              →  Ordner mit Freunden teilen
```

---

## Tastenkürzel

| Taste | Aktion |
|-------|--------|
| `1` / `2` / `0` | Ausgewählt / Abgewählt / Offen |
| `3` / `4` | Startfolie / Schlussfolie |
| `Leertaste` | Status zyklisch weiterschalten |
| `L` / `R` | Links / rechts drehen |
| `S` | Drehung dauerhaft speichern |
| `Entf` | Foto entfernen |
| `F5` | Ordner neu scannen |
| `Strg+M` | Karten generieren |
| `Strg+P` | Präsentation starten |
| `Esc` | Zuschnitt-Auswahl verwerfen |

---

## Systemvoraussetzungen

- Windows 10 / 11 (64-Bit)
- Keine zusätzliche Software — .NET ist enthalten

---

## Technologie

| | |
|---|---|
| Plattform | .NET 10, C# 13, WPF |
| UI-Bibliothek | [WPF-UI](https://github.com/lepoco/wpfui) (Fluent Design) |
| Bildverarbeitung | [SixLabors ImageSharp](https://github.com/SixLabors/ImageSharp) |
| HEIC-Decoding | [Magick.NET](https://github.com/dlemstra/Magick.NET) |
| Karten-Tiles | [MapTiler Cloud](https://maptiler.com) |
| Geocoding | [Nominatim](https://nominatim.org) / OpenStreetMap |
| EXIF-Metadaten | [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet) |

---

## Handbuch

Eine ausführliche Kombination aus Werbebroschüre und Benutzerhandbuch liegt als
[TravelJournal-Handbuch.md](TravelJournal-Handbuch.md) bzw. als
[TravelJournal-Handbuch.pdf](TravelJournal-Handbuch.pdf) im Repository.

---

## Lizenz

MIT License — siehe [LICENSE](LICENSE).
