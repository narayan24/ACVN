using System.Collections.Generic;

public class Character
{
    public string Id { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();

    /// <summary>
    /// Set to true via "is_main_character": true in chars.json.
    /// Main characters appear in the NPC character-setup screen at game start
    /// so the player can customise their names, ages and relationships.
    /// The player character (relation == "mc") is never shown here.
    /// </summary>
    public bool IsMainCharacter { get; set; }

    public void SetAttribute(string key, object value)
    {
        this.Properties[key] = value;
    }
}