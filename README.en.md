# Waha40k MCP Server

[![CI](https://github.com/MEC-Guard/Waha40kMcp/actions/workflows/ci.yml/badge.svg)](https://github.com/MEC-Guard/Waha40kMcp/actions/workflows/ci.yml)

*[Deutsche Version / German version](README.md)*

An [MCP](https://modelcontextprotocol.io) (Model Context Protocol) server for Warhammer 40,000 (10th Edition) that supplies Claude (or any other MCP-capable client) with live data from [Wahapedia](https://wahapedia.ru) and the official [Munitorum Field Manual](https://mfm.warhammer-community.com).

*Powered by Wahapedia — not affiliated with Games Workshop or Wahapedia.*

The server exposes **26 tools** across four areas:

- 📖 **Database lookups** — datasheets, factions, stratagems, unit comparison
- ⚔️ **MathHammer / combat calculator** — expected-value and Monte Carlo damage calculation with automatic ability detection
- ⚒️ **Army builder** — build lists with automatically refreshed MFM points (including copy-tier pricing, wargear surcharges, detachments & enhancements), stored permanently, exportable as PDF
- 📚 **Strategy knowledge base** — capture and retrieve tactical tips extracted from articles/battle reports

---

## Table of contents

- [Requirements](#requirements)
- [Installation](#installation)
- [Setting up Claude Desktop (stdio mode)](#setting-up-claude-desktop-stdio-mode)
- [HTTP mode (remote access)](#http-mode-remote-access)
- [All tools in detail](#all-tools-in-detail)
  - [Database lookups](#1-database-lookups-waha40ktools)
  - [MathHammer / combat calculator](#2-mathhammer--combat-calculator-combatcalculator)
  - [Army builder](#3-army-builder-armybuildertools)
  - [Strategy knowledge base](#4-strategy-knowledge-base-strategytools)
- [Example questions for Claude](#example-questions-for-claude)
- [Caching & data directories](#caching--data-directories)
- [Tests](#tests)
- [Project structure](#project-structure)
- [Data sources](#data-sources)
- [License](#license)

---

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9)
- Claude Desktop (or any other MCP client that supports stdio or HTTP transport)
- Internet connection on first run:
  - downloads the Wahapedia CSV files (~10 MB, then cached for 24h)
  - downloads a Chromium browser for Playwright once (~300 MB, needed to scrape the official MFM points)

## Installation

```bash
# 1. Clone the repository
git clone https://github.com/<your-user>/Waha40kMcp.git
cd Waha40kMcp

# 2. Restore packages & build
dotnet build -c Release

# 3. Test run (optional — verifies Wahapedia is reachable and installs Playwright)
dotnet run
```

On first run:
- all CSV files from Wahapedia are downloaded and cached under `%LOCALAPPDATA%\Waha40kMcp\cache\` (Windows) or `~/.local/share/Waha40kMcp/cache/` (Linux/Mac), with a 24h TTL,
- Playwright automatically installs a Chromium browser if not already present.

> **Tip for faster startup:** run `dotnet publish -c Release -o publish` ahead of time and point directly at the resulting executable instead of `dotnet run` — this skips the build step on every server start.

## Setting up Claude Desktop (stdio mode)

The default mode (no flags) runs as an stdio process — this is the right mode for Claude Desktop on the same machine.

Open the configuration file:
- Windows: `%APPDATA%\Claude\claude_desktop_config.json`
- Mac: `~/Library/Application Support/Claude/claude_desktop_config.json`

and add an entry:

```json
{
  "mcpServers": {
    "waha40k": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:\\path\\to\\Waha40kMcp\\Waha40kMcp.csproj",
        "--configuration",
        "Release"
      ]
    }
  }
}
```

Or, using the `dotnet publish` build mentioned above, point directly at the executable (starts faster):

```json
{
  "mcpServers": {
    "waha40k": {
      "command": "C:\\path\\to\\Waha40kMcp\\publish\\Waha40kMcp.exe"
    }
  }
}
```

Restart Claude Desktop — the 20 tools will then be available automatically.

## HTTP mode (remote access)

With the `--http` flag the server instead runs as an HTTP server — intended for remote access (e.g. from your phone via a reverse proxy), not for local stdio operation.

```bash
# Required: set an auth token, otherwise the server refuses to start in HTTP mode
export WAHA40K_TOKEN="a-long-random-secret"   # PowerShell: $env:WAHA40K_TOKEN = "..."
export WAHA40K_PORT=5005                       # optional, default: 5005

dotnet run -- --http
```

- Every request must include the header `Authorization: Bearer <WAHA40K_TOKEN>` (compared in constant time, resistant to timing attacks).
- The server binds to `0.0.0.0:<PORT>` — **make sure only HTTPS is reachable from the outside via a reverse proxy** before opening the port externally (e.g. NAS/router port forwarding).
- Without a `WAHA40K_TOKEN` set, the server won't even start in HTTP mode (protection against accidentally open access).

## All tools in detail

### 1. Database lookups (`Waha40kTools`)

Direct queries against the Wahapedia database (datasheets, factions, stratagems). Legends units are filtered out automatically (not used in Matched Play).

| Tool | Parameters | Description |
|------|-----------|--------------|
| `get_datasheet` | `unit_name`, `faction?` | Looks up a unit by name (up to 3 matches), returns a stats table, ranged/melee weapons, abilities, wargear options, and points cost. |
| `list_faction_units` | `faction`, `keyword_filter?` | Lists all units of a faction, optionally filtered by keyword (e.g. `Infantry`, `Vehicle`, `Character`). |
| `search_stratagems` | `faction`, `query?`, `phase?`, `detachment?` | Searches a faction's stratagems, optionally filtered by text, phase (`Shooting`, `Fight`, `Movement`, `Command`) or detachment. |
| `list_factions` | – | Lists all available factions with their internal ID. |
| `list_detachments` | `faction` | Lists all detachments of a faction with their detachment ability (full rules text). |
| `list_enhancements` | `faction`, `detachment?` | Lists enhancements (equipment bonuses for characters) of a faction with points cost and effect, optionally filtered by detachment. |
| `calculate_army_points` | `unit_names`, `faction`, `points_limit=2000` | Calculates the total points of a comma-separated list of unit names and checks the points limit. |
| `compare_units` | `unit_a`, `unit_b` | Compares two units side by side (stats + points). |

### 2. MathHammer / combat calculator (`CombatCalculator`)

Automatically reads weapon keywords **and** datasheet abilities (re-rolls, Feel No Pain, Lethal Hits, Sustained Hits, Devastating Wounds, Stealth/-1 to hit, additional invulnerable saves, damage reduction, Blast, non-standard critical thresholds) and supports a Leader **and** a support character (e.g. Apothecary/Painboy) on each side simultaneously.

| Tool | Parameters | Description |
|------|-----------|--------------|
| `calculate_combat` | `attacker_name`, `defender_name`, `mode='ranged'\|'melee'`, `attacker_faction?`, `defender_faction?`, `attacker_models=5`, `weapons_filter?`, `attacker_leader?`, `defender_leader?`, `attacker_support?`, `defender_support?`, `defender_models=5`, `defender_cover=false` | Calculates the **average damage** (expected value) per weapon and in total: hit, wound, save, and damage probabilities, models killed. |
| `simulate_combat` | same as above, plus `iterations=10000` | **Monte Carlo simulation** with actual dice rolls instead of a pure expected value: shows median, 10th/90th percentile, min/max, probability of killing 0 models, and a text histogram of the result distribution. More realistic than `calculate_combat` for tabletop decisions, since it reveals dice-luck variance. |

**Also supports:**
- **Blast** — `defender_models` sets the target unit's model count; 6-10 models automatically adds +1 Attack, 11+ models adds +3 Attacks (10th edition core rule).
- **Benefit of Cover** — `defender_cover: true` treats AP -1 weapons as AP 0 (does not affect AP -2 or worse, per the rules).
- **Non-standard critical thresholds** — ability text such as "Critical Hits on a 5+" is detected automatically and factored into the Sustained Hits / Lethal Hits / Devastating Wounds math.
- **Detachment abilities & enhancements** — `attacker_detachment?`/`defender_detachment?` and `attacker_enhancement?`/`defender_enhancement?` fold a detachment's ability or a carried enhancement (e.g. "4+ invulnerable save") into the calculation, just like datasheet abilities. See `list_detachments()`/`list_enhancements()` for valid names.

Example:
```
calculate_combat('Einhyr Hearthguard', 'Deathshroud Terminators', mode: 'ranged', attacker_models: 5)
```

### 3. Army builder (`ArmyBuilderTools`)

Manages army lists — stored permanently on disk, so they survive a server restart — and automatically loads points costs from the official **Munitorum Field Manual** (via Playwright scraping, with a 24h cache and automatic fallback to Wahapedia points if the MFM has no data). Detects copy-tier pricing (e.g. 1st–2nd copy cheaper than 3rd+ copy) automatically.

| Tool | Parameters | Description |
|------|-----------|--------------|
| `create_army` | `army_name`, `faction`, `points_limit=2000`, `detachment?` | Creates a new army list, optionally with a detachment right away (see `list_detachments()`). |
| `set_detachment` | `army_name`, `detachment` | Sets or changes an army's detachment. Required for `add_enhancement`. |
| `add_unit` | `army_name`, `unit_name`, `model_count=0` | Adds a unit; automatically loads the matching points cost from the MFM (including copy-tier pricing by model count). |
| `remove_unit` | `army_name`, `unit_index` | Removes a unit by index (1-based, see `show_army`). |
| `add_enhancement` | `army_name`, `unit_index`, `enhancement_name` | Attaches an enhancement to a unit (max. 1 per unit); its points cost is automatically added to the army total. |
| `remove_enhancement` | `army_name`, `unit_index` | Removes a unit's enhancement again. |
| `show_army` | `army_name` | Shows the current list with all units, enhancements, points, and a progress bar toward the points limit. |
| `list_armies` | – | Lists all saved armies (permanent, across restarts). |
| `refresh_mfm_points` | `faction` | Clears a faction's points cache and reloads fresh from the MFM — use when Games Workshop has published new points. |
| `get_wargear_options` | `unit_name`, `faction?` | Shows wargear points surcharges for a unit (e.g. "per Storm Shield: +5 points per model"), on top of the base cost. |
| `export_army_pdf` | `army_name`, `output_path?` | Exports the list as a PDF in the style of [New Recruit](https://www.newrecruit.eu): a roster overview page plus a datasheet detail page (stats, abilities, weapons) per unit type. Rendered via Playwright/Chromium — no extra tooling needed. |

> **Note:** Army lists are saved to JSON immediately after every change (see [Caching & data directories](#caching--data-directories)) — unlike the MFM points cache, which expires after 24h.
>
> **PDF export limitation:** the army builder doesn't track individual per-model wargear selections or which character leads which unit. Detail pages therefore show the full reference datasheet rather than a specific loadout; unlike New Recruit, attached leaders aren't merged into a combined block, and multiple copies of the same unit share one detail page (with a note on the count) instead of an "x2" badge.

### 4. Strategy knowledge base (`StrategyTools`)

A local, permanently stored knowledge base for tactical tips that Claude **paraphrases** (never quotes verbatim) from web articles or battle reports and files away in a structured form.

| Tool | Parameters | Description |
|------|-----------|--------------|
| `save_strategy_note` | `title`, `tip`, `faction=''`, `opponent_faction=''`, `mission=''`, `unit=''`, `tags=''`, `source=''`, `published_date?` | Saves a tactical tip in your own words. |
| `request_strategy_research` | `faction`, `opponent_faction=''`, `max_age_days=60`, `min_sources=3` | Gives Claude a structured checklist for automated research (find several current sources, paraphrase, save). This is **not** a web-search tool itself — the server has no internet access for research; Claude performs the search afterwards with its own tools. |
| `search_strategy_notes` | `faction?`, `opponent_faction?`, `mission?`, `unit?`, `keyword?`, `tag?`, `max_age_days?` | Searches the knowledge base; all filters are optional and combinable. |
| `delete_strategy_note` | `id` | Deletes a note by its ID (see `search_strategy_notes`). |
| `list_strategy_overview` | – | Overview: number of notes, breakdown by faction, most recent entries. |

## Example questions for Claude

- *"Show me the datasheet for Intercessor Squad"*
- *"What Space Marines stratagems are there in the Shooting phase?"*
- *"List all Necron vehicles"*
- *"Calculate the points for: Intercessor Squad, Predator, Captain, Dreadnought"*
- *"Compare Intercessor Squad with Tactical Squad"*
- *"How much damage does a squad of 5 Einhyr Hearthguard do to Deathshroud Terminators at range?"*
- *"Simulate 10,000 runs: Wolf Guard Terminators in melee against Necron Warriors"*
- *"Create a 2000-point list for Leagues of Votann and add 10 Hernkyn Yaegirs"*
- *"Export my Votann list as a PDF"*
- *"What detachments does Adeptus Custodes have and what do they do?"*
- *"Show me the enhancements in the Shield Host detachment"*
- *"Research current tactics for Leagues of Votann against Space Marines"*
- *"What have I already noted about deployment tips?"*

## Caching & data directories

All persistent data lives under `%LOCALAPPDATA%\Waha40kMcp\` (Windows) or `~/.local/share/Waha40kMcp/` (Linux/Mac):

| Directory | Contents | TTL |
|-------------|--------|-----|
| `cache\` | Wahapedia CSV files (datasheets, stratagems, factions, …) | 24h |
| `mfm-cache\` | Scraped MFM points costs & wargear surcharges per faction | 24h |
| `strategy\notes.json` | Saved tactical tips (permanent, no expiry) | – |
| `armies\armies.json` | Saved army lists including units, detachment, and enhancements (permanent) | – |
| `armies\exports\` | PDF exports from `export_army_pdf` (default location unless `output_path` is given) | – |

Cache directories (`cache\`, `mfm-cache\`) can be deleted at any time — they are refilled automatically the next time they're needed. `strategy\notes.json` and `armies\armies.json`, on the other hand, are your saved data — don't delete them if you want to keep it.

## Tests

The project has a test suite (`Waha40kMcp.Tests`, xUnit) with 100+ tests covering the core logic (MathHammer probability math, ability-text parsing, points-tier pricing, search/filter logic, MFM text parsing) — fully offline, no network or browser access required:

```bash
dotnet test
```

## Project structure

```
Waha40kMcp/
├── Waha40kMcp.sln               # Solution (both projects)
├── .github/workflows/ci.yml    # GitHub Actions: build + test on every push/PR
├── Program.cs                  # Entry point: stdio or --http mode
├── Data/
│   ├── WahapediaRepository.cs  # Loads & indexes Wahapedia CSVs
│   ├── MfmScraper.cs           # Scrapes points costs from the official MFM (Playwright)
│   ├── IMfmScraper.cs          # Interface for testability
│   └── StrategyRepository.cs   # Persists strategy notes as JSON
├── Models/
│   └── Models.cs                # Datasheet, Stratagem, Faction, ArmyList, StrategyNote, …
├── Tools/
│   ├── Waha40kTools.cs          # Database lookups
│   ├── CombatCalculator.cs      # MathHammer / Monte Carlo simulation
│   ├── ArmyBuilderTools.cs      # Army builder
│   └── StrategyTools.cs         # Strategy knowledge base
└── Waha40kMcp.Tests/            # xUnit test suite
```

## Data sources

- Unit, faction, and stratagem data: public CSV export from [Wahapedia](https://wahapedia.ru) (24h cache, refreshed automatically). Please support Wahapedia: https://boosty.to/wahapedia
- Official points costs: [Munitorum Field Manual](https://mfm.warhammer-community.com) (Games Workshop), retrieved via Playwright scraping.

## License

MIT — see [LICENSE](LICENSE). This project is not affiliated with Games Workshop or Wahapedia; all Warhammer 40,000 content (rules, unit names, points costs) is the property of Games Workshop.
