namespace Waha40kMcp.Models;

// ── Datasheets ────────────────────────────────────────────────────────────────

public class Datasheet
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string FactionId { get; set; } = "";
    public string FactionName { get; set; } = "";
    public string Link { get; set; } = "";
    public string Keywords { get; set; } = "";
    public string FactionKeywords { get; set; } = "";
    public string Datasheettype { get; set; } = "";
    public string LegendText { get; set; } = "";

    // Stats (filled from Datasheets_models.csv)
    public List<DatasheetModel> Models { get; set; } = [];
    public List<DatasheetWeapon> Weapons { get; set; } = [];
    public List<DatasheetAbility> Abilities { get; set; } = [];
    public List<string> Options { get; set; } = [];
    public List<PointsCostEntry> PointsCosts { get; set; } = [];

    // Convenience: letzter (höchster) Punktewert für Suchen/Listen
    public int? PointsCost => PointsCosts.Count > 0 ? PointsCosts.Last().Cost : null;
}

public class DatasheetModel
{
    public string DatasheetId { get; set; } = "";
    public string Name { get; set; } = "";
    public string M { get; set; } = "";   // Move
    public string T { get; set; } = "";   // Toughness
    public string Sv { get; set; } = "";  // Save
    public string W { get; set; } = "";   // Wounds
    public string Ld { get; set; } = "";  // Leadership
    public string Oc { get; set; } = "";  // Objective Control
    public string InvSv { get; set; } = ""; // Invulnerable Save
}

public class DatasheetWeapon
{
    public string DatasheetId { get; set; } = "";
    public string WeaponId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = ""; // ranged / melee
    public string Range { get; set; } = "";
    public string A { get; set; } = "";    // Attacks
    public string BsWs { get; set; } = ""; // Ballistic/Weapon Skill
    public string S { get; set; } = "";    // Strength
    public string Ap { get; set; } = "";   // AP
    public string D { get; set; } = "";    // Damage
    public string Keywords { get; set; } = "";
}

public class DatasheetAbility
{
    public string DatasheetId { get; set; } = "";
    public string AbilityId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = ""; // Datasheet, Wargear, Core, Faction
}

// ── Points Cost Entry ─────────────────────────────────────────────────────────

public class PointsCostEntry
{
    public string Description { get; set; } = ""; // z.B. "5 models" oder "1 model"
    public int Cost { get; set; }
    // Für MFM-Staffelung: 1 = erste(n) Kopien (günstiger), 2 = weitere Kopien (teurer). 0 = unbekannt/Wahapedia.
    public int CopyTier { get; set; } = 0;
}

public class WargearOptionCost
{
    public string Name { get; set; } = "";   // z.B. "Storm Shield"
    public int Cost { get; set; }            // Punkte PRO Modell das diese Option trägt
}

// ── Strategie-Wissensbasis ───────────────────────────────────────────────────

public class StrategyNote
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Eigene Fraktion, auf die sich der Tipp bezieht. Leer = allgemein.</summary>
    public string Faction { get; set; } = "";

    /// <summary>Gegnerische Fraktion im Matchup. Leer = fraktionsübergreifend.</summary>
    public string OpponentFaction { get; set; } = "";

    /// <summary>Missionstyp/Format, falls relevant, z.B. "Crucible of Battle", "Strike Force".</summary>
    public string Mission { get; set; } = "";

    /// <summary>Betroffene Einheit(en), falls der Tipp einheitenspezifisch ist.</summary>
    public string Unit { get; set; } = "";

    /// <summary>Kurzer Titel/Schlagwort für die Notiz.</summary>
    public string Title { get; set; } = "";

    /// <summary>Der eigentliche taktische Tipp, paraphrasiert (keine Wort-für-Wort-Kopie der Quelle).</summary>
    public string Tip { get; set; } = "";

    /// <summary>Frei wählbare Tags zum Filtern, z.B. "deployment", "objective-control", "alpha-strike".</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Veröffentlichungsdatum der Quelle, falls bekannt. Dient zur Aktualitäts-Filterung.</summary>
    public DateTime? PublishedDate { get; set; }

    /// <summary>Quelle (URL oder Bezeichnung), zur Nachvollziehbarkeit.</summary>
    public string Source { get; set; } = "";
}


// ── Stratagems ────────────────────────────────────────────────────────────────

public class Stratagem
{
    public string Id { get; set; } = "";
    public string FactionId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string CpCost { get; set; } = "";
    public string Legend { get; set; } = "";
    public string Turn { get; set; } = "";
    public string Phase { get; set; } = "";
    public string Description { get; set; } = "";
    public string Detachment { get; set; } = "";
}

// ── Factions ──────────────────────────────────────────────────────────────────

public class Faction
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Link { get; set; } = "";
}

// ── Detachments & Enhancements ──────────────────────────────────────────────────

public class DetachmentAbility
{
    public string Id { get; set; } = "";
    public string FactionId { get; set; } = "";
    public string DetachmentId { get; set; } = "";
    public string Detachment { get; set; } = "";
    public string Name { get; set; } = "";
    public string Legend { get; set; } = "";
    public string Description { get; set; } = "";
}

public class Enhancement
{
    public string Id { get; set; } = "";
    public string FactionId { get; set; } = "";
    public string DetachmentId { get; set; } = "";
    public string Detachment { get; set; } = "";
    public string Name { get; set; } = "";
    public int Cost { get; set; }
    public string Legend { get; set; } = "";
    public string Description { get; set; } = "";
}

// ── Army Builder ──────────────────────────────────────────────────────────────

public class ArmyUnit
{
    /// <summary>Stabile, im Army-Objekt eindeutige ID — unabhängig von der (sich durch
    /// add_unit/remove_unit verschiebenden) Listenposition. Wird für <see cref="AttachedToUnitId"/>
    /// referenziert, damit die Zuordnung Umsortierungen/Löschungen übersteht.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public string DatasheetId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Points { get; set; }
    public int ModelCount { get; set; } = 1;

    /// <summary>Name der angehängten Enhancement, falls vorhanden (leer = keine).</summary>
    public string EnhancementName { get; set; } = "";
    /// <summary>Punktekosten der Enhancement — bereits in <see cref="Points"/> enthalten.</summary>
    public int EnhancementCost { get; set; }

    /// <summary>
    /// Falls gesetzt: die <see cref="Id"/> der Einheit, die dieser (Leader-)Charakter anführt.
    /// Wird von ArmyPdfExporter genutzt, um Leader und geführte Einheit zu einem gemeinsamen
    /// Detasheet-Block zusammenzuführen (wie "Attach" in New Recruit).
    /// </summary>
    public string? AttachedToUnitId { get; set; }
}

public class ArmyList
{
    public string Name { get; set; } = "My Army";
    public string FactionId { get; set; } = "";
    public string FactionName { get; set; } = "";
    public string Detachment { get; set; } = "";
    public List<ArmyUnit> Units { get; set; } = [];
    public int TotalPoints => Units.Sum(u => u.Points);
    public int PointsLimit { get; set; } = 2000;
}