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
