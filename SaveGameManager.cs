using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

public class GameState
{
    public DateTime GameTime { get; set; }
    public List<Character> Characters { get; set; }

    public string CurrentRoom { get; set; }

    public string CurrentAction { get; set; }

    public GameState()
    {
        GameTime = new DateTime();
        Characters = new List<Character>();
        CurrentAction = "start";
        CurrentRoom = "start";
    }
}

public class SaveGameManager
{
    public void SaveGame(GameState gameState, string filePath)
    {
        string json = JsonConvert.SerializeObject(gameState, new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto
        });
        File.WriteAllText(filePath, json);
    }

    public GameState LoadGame(string filePath)
    {
        if (File.Exists(filePath))
        {
            string json = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<GameState>(json, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto
            });
        }
        else
        {
            // Datei existiert nicht, gib einen Standardzustand zurück
            return new GameState();
        }
    }
}
