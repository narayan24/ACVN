using System.Collections.Generic;

public class ScheduleEntry
{
    public string Days { get; set; }          // "weekdays" | "weekends" | "all" | "1,2,3"
    public string From { get; set; }          // "HH:MM"
    public string To { get; set; }            // "HH:MM"
    public List<string> Locations { get; set; } = new List<string>();
    public string Activity { get; set; }
}

public class CharacterSchedule
{
    public string CharId { get; set; }
    public List<ScheduleEntry> Entries { get; set; } = new List<ScheduleEntry>();
}

public class ItemDefinition
{
    public string Id { get; set; }
    public string Type { get; set; }
    public string Subtype { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public int StartingQuantity { get; set; }

    /// <summary>
    /// Attribute changes applied to "mc" when the item is consumed via use_item.
    /// Keys match attribute ids (e.g. "hunger", "energy", "mood", "health").
    /// Only meaningful for type: "food" or type: "consumable".
    /// Example: { "hunger": 30, "energy": 10 }
    /// </summary>
    public Dictionary<string, int> Effects { get; set; } = new Dictionary<string, int>();
}

public class ClothingDefinition
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Subtype { get; set; }          // bra | panties | clothes | shoes
    public int Durability { get; set; } = 100;
    public int Daring { get; set; }
    public int Inhibition { get; set; }           // player inhibition must be <= this to wear
    public int StartingQuantity { get; set; } = 1; // 0 = must be purchased/unlocked first
    public int Price { get; set; }                 // shop price in Kron (0 = not for sale)
    public List<string> Tags { get; set; } = new List<string>();
    public string Image { get; set; }
}

public class QuestStepDef
{
    public string Id { get; set; }
    public string Description { get; set; }
}

public class QuestDefinition
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Hint { get; set; }
    public List<QuestStepDef> Steps { get; set; } = new List<QuestStepDef>();
}

public class ModDefinition
{
    public string Path        { get; set; }
    public string Id          { get; set; }
    public string Name        { get; set; }
    public string Version     { get; set; } = "";
    public string Author      { get; set; } = "";
    public string Description { get; set; } = "";
    public int    Priority    { get; set; } = 50;
    public bool   Enabled     { get; set; } = true;
}
