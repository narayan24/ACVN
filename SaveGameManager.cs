using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

public class GameState
{
    public DateTime GameTime { get; set; }
    public List<Character> Characters { get; set; }
    public string CurrentRoom { get; set; }
    public string CurrentAction { get; set; }
    public Dictionary<string, object> GameVariables { get; set; }
    public Dictionary<string, int> Inventory { get; set; }
    public Dictionary<string, int> QuestProgress { get; set; }
    public Dictionary<string, string> WornClothing { get; set; }

    public GameState()
    {
        GameTime = new DateTime();
        Characters = new List<Character>();
        CurrentAction = "start";
        CurrentRoom = "start";
        GameVariables = new Dictionary<string, object>();
        Inventory = new Dictionary<string, int>();
        QuestProgress = new Dictionary<string, int>();
        WornClothing = new Dictionary<string, string>();
    }
}

public class SaveGameManager
{
    // 16-byte AES-128 key — saves are encrypted so they can't be trivially edited
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("ACVNSaveKey2024!");

    public void SaveGame(GameState gameState, string filePath)
    {
        string json = JsonConvert.SerializeObject(gameState, new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto
        });
        File.WriteAllBytes(filePath, Encrypt(json));
    }

    public GameState LoadGame(string filePath)
    {
        if (!File.Exists(filePath))
            return new GameState();

        string json = Decrypt(File.ReadAllBytes(filePath));
        return JsonConvert.DeserializeObject<GameState>(json, new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto
        });
    }

    private byte[] Encrypt(string plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = Key;
        aes.GenerateIV();
        using var enc = aes.CreateEncryptor();
        byte[] cipher = enc.TransformFinalBlock(Encoding.UTF8.GetBytes(plaintext), 0, Encoding.UTF8.GetByteCount(plaintext));
        // Layout: [16 bytes IV][cipher]
        byte[] result = new byte[16 + cipher.Length];
        Array.Copy(aes.IV, 0, result, 0, 16);
        Array.Copy(cipher, 0, result, 16, cipher.Length);
        return result;
    }

    private string Decrypt(byte[] data)
    {
        byte[] iv     = new byte[16];
        byte[] cipher = new byte[data.Length - 16];
        Array.Copy(data, 0,  iv,     0, 16);
        Array.Copy(data, 16, cipher, 0, cipher.Length);

        using var aes = Aes.Create();
        aes.Key = Key;
        aes.IV  = iv;
        using var dec = aes.CreateDecryptor();
        byte[] plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
        return Encoding.UTF8.GetString(plain);
    }
}
