using System.Collections.Generic;

public class Character
{
    public string Id { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();

    public void SetAttribute(string key, object value)
    {
        this.Properties[key] = value;
    }
}