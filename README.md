# Waha40k MCP Server

[![CI](https://github.com/MEC-Guard/Waha40kMcp/actions/workflows/ci.yml/badge.svg)](https://github.com/MEC-Guard/Waha40kMcp/actions/workflows/ci.yml)

*[English version](README.en.md)*

Ein [MCP](https://modelcontextprotocol.io)-Server (Model Context Protocol) für Warhammer 40.000 (10th Edition), der Claude (oder jeden anderen MCP-fähigen Client) mit Live-Daten von [Wahapedia](https://wahapedia.ru) und dem offiziellen [Munitorum Field Manual](https://mfm.warhammer-community.com) versorgt.

*Powered by Wahapedia — nicht mit Games Workshop oder Wahapedia affiliiert.*

Der Server stellt **25 Tools** in vier Bereichen bereit:

- 📖 **Datenbank-Abfragen** — Datasheets, Fraktionen, Stratagems, Punkte-Vergleich
- ⚔️ **MathHammer / Combat-Rechner** — Erwartungswert- und Monte-Carlo-Schadensberechnung inkl. automatischer Fähigkeiten-Erkennung
- ⚒️ **Army Builder** — Listen bauen mit automatisch aktualisierten MFM-Punktekosten (inkl. Copy-Tier-Staffelung und Wargear-Aufpreisen)
- 📚 **Strategie-Wissensbasis** — taktische Tipps aus Artikeln/Battle-Reports strukturiert ablegen und wiederfinden

---

## Inhaltsverzeichnis

- [Voraussetzungen](#voraussetzungen)
- [Installation](#installation)
- [Claude Desktop einrichten (stdio-Modus)](#claude-desktop-einrichten-stdio-modus)
- [HTTP-Modus (Remote-Zugriff)](#http-modus-remote-zugriff)
- [Alle Tools im Detail](#alle-tools-im-detail)
  - [Datenbank-Abfragen](#1-datenbank-abfragen-waha40ktools)
  - [MathHammer / Combat-Rechner](#2-mathhammer--combat-rechner-combatcalculator)
  - [Army Builder](#3-army-builder-armybuildertools)
  - [Strategie-Wissensbasis](#4-strategie-wissensbasis-strategytools)
- [Beispiel-Fragen an Claude](#beispiel-fragen-an-claude)
- [Caching & Datenverzeichnisse](#caching--datenverzeichnisse)
- [Tests](#tests)
- [Projektstruktur](#projektstruktur)
- [Datenquellen](#datenquellen)
- [Lizenz](#lizenz)

---

## Voraussetzungen

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9)
- Claude Desktop (oder ein anderer MCP-Client, der stdio- oder HTTP-Transport unterstützt)
- Internetverbindung beim ersten Start:
  - lädt die Wahapedia-CSV-Dateien (~10 MB, danach 24h Cache)
  - lädt einmalig einen Chromium-Browser für Playwright herunter (~300 MB, wird für das Scrapen der offiziellen MFM-Punktekosten benötigt)

## Installation

```bash
# 1. Repository klonen
git clone https://github.com/<dein-user>/Waha40kMcp.git
cd Waha40kMcp

# 2. Pakete wiederherstellen & bauen
dotnet build -c Release

# 3. Testlauf (optional, prüft dass Wahapedia erreichbar ist und Playwright installiert wird)
dotnet run
```

Beim ersten Start:
- werden alle CSV-Dateien von Wahapedia heruntergeladen und unter `%LOCALAPPDATA%\Waha40kMcp\cache\` (Windows) bzw. `~/.local/share/Waha40kMcp/cache/` (Linux/Mac) zwischengespeichert (24h TTL),
- installiert Playwright automatisch einen Chromium-Browser, falls noch nicht vorhanden.

> **Tipp für schnelleren Start:** Vorab `dotnet publish -c Release -o publish` ausführen und danach direkt die erzeugte `.exe`/das Binary starten statt `dotnet run` — das spart den Build-Schritt bei jedem Serverstart.

## Claude Desktop einrichten (stdio-Modus)

Der Standardmodus (ohne Flags) läuft als stdio-Prozess — das ist der richtige Modus für Claude Desktop auf demselben Rechner.

Öffne die Konfigurationsdatei:
- Windows: `%APPDATA%\Claude\claude_desktop_config.json`
- Mac: `~/Library/Application Support/Claude/claude_desktop_config.json`

und füge einen Eintrag hinzu:

```json
{
  "mcpServers": {
    "waha40k": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:\\Pfad\\zu\\Waha40kMcp\\Waha40kMcp.csproj",
        "--configuration",
        "Release"
      ]
    }
  }
}
```

Oder, mit dem oben erwähnten `dotnet publish`-Vorabbau, direkt auf die ausführbare Datei zeigen (startet schneller):

```json
{
  "mcpServers": {
    "waha40k": {
      "command": "C:\\Pfad\\zu\\Waha40kMcp\\publish\\Waha40kMcp.exe"
    }
  }
}
```

Claude Desktop neu starten — die 20 Tools stehen danach automatisch zur Verfügung.

## HTTP-Modus (Remote-Zugriff)

Mit dem Flag `--http` läuft der Server stattdessen als HTTP-Server — gedacht für Remote-Zugriff (z.B. vom Handy über einen Reverse-Proxy), nicht für den lokalen stdio-Betrieb.

```bash
# Pflicht: Auth-Token setzen, sonst verweigert der Server den Start im HTTP-Modus
export WAHA40K_TOKEN="ein-langes-zufälliges-geheimnis"   # PowerShell: $env:WAHA40K_TOKEN = "..."
export WAHA40K_PORT=5005                                  # optional, Standard: 5005

dotnet run -- --http
```

- Jede Anfrage muss den Header `Authorization: Bearer <WAHA40K_TOKEN>` mitschicken (Vergleich läuft in konstanter Zeit, timing-attack-resistent).
- Der Server bindet auf `0.0.0.0:<PORT>` — **stelle über einen Reverse-Proxy sicher, dass von außen nur HTTPS erreichbar ist**, bevor du den Port nach außen öffnest (z.B. NAS/Router-Portfreigabe).
- Ohne gesetztes `WAHA40K_TOKEN` startet der Server im HTTP-Modus gar nicht erst (Schutz gegen versehentlich offenen Zugriff).

## Alle Tools im Detail

### 1. Datenbank-Abfragen (`Waha40kTools`)

Direkte Abfragen der Wahapedia-Datenbank (Datasheets, Fraktionen, Stratagems). Legends-Einheiten werden automatisch herausgefiltert (nicht im Matched Play).

| Tool | Parameter | Beschreibung |
|------|-----------|--------------|
| `get_datasheet` | `unit_name`, `faction?` | Sucht eine Einheit nach Name (bis zu 3 Treffer), liefert Stats-Tabelle, Fernkampf-/Nahkampfwaffen, Fähigkeiten, Ausrüstungsoptionen und Punktekosten. |
| `list_faction_units` | `faction`, `keyword_filter?` | Listet alle Einheiten einer Fraktion, optional gefiltert nach Keyword (z.B. `Infantry`, `Vehicle`, `Character`). |
| `search_stratagems` | `faction`, `query?`, `phase?`, `detachment?` | Sucht Stratagems einer Fraktion, optional nach Text, Phase (`Shooting`, `Fight`, `Movement`, `Command`) oder Detachment gefiltert. |
| `list_factions` | – | Listet alle verfügbaren Fraktionen mit ihrer internen ID. |
| `list_detachments` | `faction` | Listet alle Detachments einer Fraktion mit ihrer Detachment-Fähigkeit (voller Regeltext). |
| `list_enhancements` | `faction`, `detachment?` | Listet Enhancements (Ausrüstungs-Boni für Charaktere) einer Fraktion mit Punktekosten und Effekt, optional nach Detachment gefiltert. |
| `calculate_army_points` | `unit_names`, `faction`, `points_limit=2000` | Berechnet die Gesamtpunkte einer kommagetrennten Liste von Einheitennamen und prüft das Punktelimit. |
| `compare_units` | `unit_a`, `unit_b` | Vergleicht zwei Einheiten nebeneinander (Stats + Punkte). |

### 2. MathHammer / Combat-Rechner (`CombatCalculator`)

Liest Waffen-Keywords **und** Datasheet-Fähigkeiten automatisch aus (Re-rolls, Feel No Pain, Lethal Hits, Sustained Hits, Devastating Wounds, Stealth/-1 to hit, zusätzliche Invulnerable Saves, Schadensreduzierung, Blast, abweichende Crit-Schwellen) und unterstützt gleichzeitig einen Leader **und** einen Support-Charakter (z.B. Apothecary/Painboy) pro Seite.

| Tool | Parameter | Beschreibung |
|------|-----------|--------------|
| `calculate_combat` | `attacker_name`, `defender_name`, `mode='ranged'\|'melee'`, `attacker_faction?`, `defender_faction?`, `attacker_models=5`, `weapons_filter?`, `attacker_leader?`, `defender_leader?`, `attacker_support?`, `defender_support?`, `defender_models=5`, `defender_cover=false` | Berechnet den **Durchschnittsschaden** (Erwartungswert) pro Waffe und in Summe: Treffer-, Wund-, Save- und Schadenswahrscheinlichkeiten, getötete Modelle. |
| `simulate_combat` | wie oben, zusätzlich `iterations=10000` | **Monte-Carlo-Simulation** mit echten Würfelwürfen statt reinem Erwartungswert: zeigt Median, 10./90. Perzentil, Min/Max, Wahrscheinlichkeit für 0 getötete Modelle und ein Text-Histogramm der Ergebnisverteilung. Realistischer als `calculate_combat` für Entscheidungen am Spieltisch, da Würfelglück-Streuung sichtbar wird. |

**Zusätzlich unterstützte Mechaniken:**
- **Blast** — `defender_models` gibt die Modellanzahl im Ziel-Trupp an; bei 6-10 Modellen gibt's automatisch +1 Attacke, bei 11+ Modellen +3 Attacken (10th-Edition-Kernregel).
- **Benefit of Cover** — `defender_cover: true` setzt Waffen mit AP -1 automatisch auf AP 0 (wirkt sich nicht auf AP -2 oder schlechter aus, wie in den Regeln vorgesehen).
- **Abweichende Crit-Schwellen** — Fähigkeitstexte wie „Critical Hits on a 5+" werden automatisch erkannt und gehen in die Sustained-Hits-/Lethal-Hits-/Devastating-Wounds-Rechnung ein.
- **Detachment-Fähigkeiten & Enhancements** — `attacker_detachment?`/`defender_detachment?` und `attacker_enhancement?`/`defender_enhancement?` lassen die Detachment-Regel bzw. eine getragene Enhancement (z.B. „4+ invulnerable save") in die Rechnung einfließen, genau wie Datasheet-Fähigkeiten. Siehe `list_detachments()`/`list_enhancements()` für gültige Namen.

Beispiel:
```
calculate_combat('Einhyr Hearthguard', 'Deathshroud Terminators', mode: 'ranged', attacker_models: 5)
```

### 3. Army Builder (`ArmyBuilderTools`)

Verwaltet In-Memory-Army-Listen und lädt Punktekosten automatisch vom offiziellen **Munitorum Field Manual** (via Playwright-Scraping, mit 24h-Cache und automatischem Fallback auf Wahapedia-Punkte, falls das MFM keine Daten liefert). Erkennt Copy-Tier-Staffelungen (z.B. 1.–2. Kopie günstiger als 3.+ Kopie) automatisch.

| Tool | Parameter | Beschreibung |
|------|-----------|--------------|
| `create_army` | `army_name`, `faction`, `points_limit=2000`, `detachment?` | Erstellt eine neue Army-Liste, optional direkt mit Detachment (siehe `list_detachments()`). |
| `set_detachment` | `army_name`, `detachment` | Legt das Detachment einer Army fest oder ändert es. Nötig für `add_enhancement`. |
| `add_unit` | `army_name`, `unit_name`, `model_count=0` | Fügt eine Einheit hinzu; lädt die passenden Punktekosten automatisch vom MFM (inkl. Copy-Tier-Staffelung nach Modellanzahl). |
| `remove_unit` | `army_name`, `unit_index` | Entfernt eine Einheit per Index (1-basiert, siehe `show_army`). |
| `add_enhancement` | `army_name`, `unit_index`, `enhancement_name` | Hängt eine Enhancement an eine Einheit (max. 1 pro Einheit); Punktekosten fließen automatisch in die Army-Gesamtsumme ein. |
| `remove_enhancement` | `army_name`, `unit_index` | Entfernt die Enhancement einer Einheit wieder. |
| `show_army` | `army_name` | Zeigt die aktuelle Liste mit allen Einheiten, Enhancements, Punkten und einem Fortschrittsbalken zum Punktelimit. |
| `list_armies` | – | Listet alle in dieser Server-Sitzung gespeicherten Armies. |
| `refresh_mfm_points` | `faction` | Löscht den Punkte-Cache einer Fraktion und lädt frisch vom MFM — nutzen, wenn Games Workshop neue Punkte veröffentlicht hat. |
| `get_wargear_options` | `unit_name`, `faction?` | Zeigt Wargear-Punktaufpreise einer Einheit (z.B. „per Storm Shield: +5 Punkte pro Modell"), zusätzlich zum Grundpreis. |

> **Hinweis:** Army-Listen leben nur für die Dauer der Server-Sitzung im Speicher (kein Neustart-Persistenz) — anders als die Strategie-Notizen unten, die dauerhaft auf Disk gespeichert werden.

### 4. Strategie-Wissensbasis (`StrategyTools`)

Eine lokale, dauerhaft gespeicherte Wissensbasis für taktische Tipps, die Claude aus Web-Artikeln oder Battle-Reports **paraphrasiert** (nie wörtlich zitiert) extrahiert und strukturiert ablegt.

| Tool | Parameter | Beschreibung |
|------|-----------|--------------|
| `save_strategy_note` | `title`, `tip`, `faction=''`, `opponent_faction=''`, `mission=''`, `unit=''`, `tags=''`, `source=''`, `published_date?` | Speichert einen taktischen Tipp in eigenen Worten. |
| `request_strategy_research` | `faction`, `opponent_faction=''`, `max_age_days=60`, `min_sources=3` | Liefert Claude eine strukturierte Checkliste für eine automatische Recherche (mehrere aktuelle Quellen suchen, paraphrasieren, speichern). Ist selbst **kein** Web-Suche-Tool — der Server hat keinen Internetzugriff für Recherche, Claude führt die Suche im Anschluss mit seinen eigenen Tools aus. |
| `search_strategy_notes` | `faction?`, `opponent_faction?`, `mission?`, `unit?`, `keyword?`, `tag?`, `max_age_days?` | Durchsucht die Wissensbasis; alle Filter sind optional und kombinierbar. |
| `delete_strategy_note` | `id` | Löscht eine Notiz anhand ihrer ID (siehe `search_strategy_notes`). |
| `list_strategy_overview` | – | Übersicht: Anzahl Notizen, Verteilung nach Fraktion, neueste Einträge. |

## Beispiel-Fragen an Claude

- *"Zeig mir das Datasheet für Intercessor Squad"*
- *"Welche Space Marines Stratagems gibt es in der Shooting Phase?"*
- *"Liste alle Necron Fahrzeuge auf"*
- *"Berechne die Punkte für: Intercessor Squad, Predator, Captain, Dreadnought"*
- *"Vergleiche Intercessor Squad mit Tactical Squad"*
- *"Wie viel Schaden macht ein Trupp von 5 Einhyr Hearthguard gegen Deathshroud Terminators im Fernkampf?"*
- *"Simuliere 10.000 Durchläufe: Wolf Guard Terminators im Nahkampf gegen Necron Warriors"*
- *"Erstelle eine 2000-Punkte-Liste für Leagues of Votann und füge 10 Hernkyn Yaegirs hinzu"*
- *"Welche Detachments gibt es für Adeptus Custodes und was machen sie?"*
- *"Zeig mir die Enhancements im Shield-Host-Detachment"*
- *"Recherchiere aktuelle Taktiken für Leagues of Votann gegen Space Marines"*
- *"Was habe ich mir schon zu Deployment-Tipps notiert?"*

## Caching & Datenverzeichnisse

Alle persistenten Daten liegen unter `%LOCALAPPDATA%\Waha40kMcp\` (Windows) bzw. `~/.local/share/Waha40kMcp/` (Linux/Mac):

| Verzeichnis | Inhalt | TTL |
|-------------|--------|-----|
| `cache\` | Wahapedia-CSV-Dateien (Datasheets, Stratagems, Fraktionen, …) | 24h |
| `mfm-cache\` | Gescrapte MFM-Punktekosten & Wargear-Aufpreise pro Fraktion | 24h |
| `strategy\notes.json` | Gespeicherte taktische Tipps (dauerhaft, kein Ablaufdatum) | – |

Cache-Verzeichnisse können jederzeit gelöscht werden — sie werden beim nächsten Bedarf automatisch neu befüllt.

## Tests

Das Projekt hat eine Testsuite (`Waha40kMcp.Tests`, xUnit) mit über 100 Tests für die Kernlogik (MathHammer-Wahrscheinlichkeitsrechnung, Ability-Text-Parsing, Punkte-Staffelung, Such-/Filterlogik, MFM-Text-Parsing) — komplett offline, ohne Netzwerk- oder Browserzugriff:

```bash
dotnet test
```

## Projektstruktur

```
Waha40kMcp/
├── Waha40kMcp.sln               # Solution (beide Projekte)
├── .github/workflows/ci.yml    # GitHub Actions: Build + Test bei jedem Push/PR
├── Program.cs                  # Einstiegspunkt: stdio- oder --http-Modus
├── Data/
│   ├── WahapediaRepository.cs  # Lädt & indiziert Wahapedia-CSVs
│   ├── MfmScraper.cs           # Scraped Punktekosten vom offiziellen MFM (Playwright)
│   ├── IMfmScraper.cs          # Interface für Testbarkeit
│   └── StrategyRepository.cs   # Persistiert Strategie-Notizen als JSON
├── Models/
│   └── Models.cs                # Datasheet, Stratagem, Faction, ArmyList, StrategyNote, …
├── Tools/
│   ├── Waha40kTools.cs          # Datenbank-Abfragen
│   ├── CombatCalculator.cs      # MathHammer / Monte-Carlo-Simulation
│   ├── ArmyBuilderTools.cs      # Army Builder
│   └── StrategyTools.cs         # Strategie-Wissensbasis
└── Waha40kMcp.Tests/            # xUnit-Testsuite
```

## Datenquellen

- Einheiten-, Fraktions- und Stratagem-Daten: öffentlicher CSV-Export von [Wahapedia](https://wahapedia.ru) (24h-Cache, automatische Aktualisierung). Bitte unterstütze Wahapedia: https://boosty.to/wahapedia
- Offizielle Punktekosten: [Munitorum Field Manual](https://mfm.warhammer-community.com) (Games Workshop), per Playwright-Scraping abgerufen.

## Lizenz

MIT — siehe [LICENSE](LICENSE). Dieses Projekt ist nicht mit Games Workshop oder Wahapedia affiliiert; alle Warhammer-40.000-Inhalte (Regeln, Einheitennamen, Punktekosten) sind Eigentum von Games Workshop.
