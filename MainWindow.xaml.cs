using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using Scriban;
using System.Windows.Controls;
using Scriban.Runtime;

namespace ACVN
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private string storyPath;
        private string roomsPath;
        private string imagesPath;
        private string saveGamePath;

        private string currentRoom;
        private string currentAction;

        private ScriptObject scriptObject = new ScriptObject();

        private List<Character> characters;

        private GameTime gameTime = new GameTime();

        public MainWindow()
        {
            InitializeComponent();

            string rootPath = AppDomain.CurrentDomain.BaseDirectory;
            Directory.SetCurrentDirectory(rootPath);

            storyPath = Path.Combine(rootPath, "../../../story/");
            if (!Directory.Exists(storyPath))
            {
                storyPath = Path.Combine(rootPath, "story/");
                if (!Directory.Exists(storyPath))
                {
                    MessageBox.Show("The folder 'story' wasn't found. Not able to start the game!");
                }
            }
            roomsPath = Path.Combine(storyPath, "rooms");
            imagesPath = Path.Combine(storyPath, "images");
            saveGamePath = Path.Combine(rootPath, "savegames");
            if (!Directory.Exists(saveGamePath))
            {
                Directory.CreateDirectory(saveGamePath);
            }
            CheckFolder(storyPath);
            CheckFolder(roomsPath);
            CheckFolder(imagesPath);

            currentRoom = "start";
            currentAction = "start";

            GetChars();
            InitContent();
        }

        private void CheckFolder(string folder)
        {
            if (!Directory.Exists(folder))
            {
                MessageBox.Show("The folder '" + folder + "' wasn't found");
            }
        }

        private string clearPath(string path)
        {
            return Regex.Replace(path, @"_", "/");
        }

        /* CONTENT HANDLING */
        private void InitContent()
        {
            string filePath = Path.Combine(roomsPath, clearPath(currentRoom) + ".acvn");
            if (File.Exists(filePath) && Path.GetExtension(filePath).Equals(".acvn", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string fileContent = File.ReadAllText(filePath);

                    var context = new TemplateContext();
                    scriptObject["mc"] = GetCharacter("mc");
                    scriptObject["datetime"] = gameTime;
                    context.PushGlobal(scriptObject);

                    string pattern = @"#begin\s+(.*?)\s+(.*?)#end";
                    MatchCollection matches = Regex.Matches(fileContent, pattern, RegexOptions.Singleline);

                    if (matches.Count > 0)
                    {
                        foreach (Match match in matches)
                        {
                            if (match.Success)
                            {
                                string blockName = match.Groups[1].Value;
                                string blockContent = match.Groups[2].Value;

                                if (blockName == currentAction)
                                {
                                    var template = Scriban.Template.Parse(blockContent);
                                    blockContent = template.Render(context);
                                    ShowRandomMedia(clearPath(currentRoom) + "/" + (currentAction == "start" ? "" : clearPath(currentAction)));
                                    ParseRooms(blockContent);
                                    ParseContent(blockContent);
                                    UpdateStatusBar();
                                }

                                fileContent = fileContent.Replace(match.Value, string.Empty);
                            }
                        }
                    }
                    else
                    {
                        mainContent.NavigateToString(fileContent);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error while reading the file: {ex.Message}");
                }
            }
            else
            {
                MessageBox.Show($"Error loading file '{filePath}'");
            }
        }


        private void ParseRooms(string content)
        {
            string[] commands = content.Split(new string[] { "[[", "]]" }, StringSplitOptions.RemoveEmptyEntries);
            if (commands.Length > 0)
            {
                for (int i = 1; i < commands.Length; i++)
                {
                    commands[i - 1] = commands[i];
                }
                Array.Resize(ref commands, commands.Length - 1);
                roomStack.Children.Clear();
                actionStack.Children.Clear();
                foreach (string command in commands)
                {
                    string[] commandParts = command.Split(',');

                    if (commandParts.Length < 2)
                        continue;

                    string text = commandParts[0].Trim();
                    string roomName = commandParts[1].Trim();

                    if (commandParts.Length == 2)
                    {
                        // Rooms
                        Button button = new Button
                        {
                            Content = text,
                            Margin = new Thickness(5),
                            Padding = new Thickness(5),
                            Height = 30,
                            Background = System.Windows.Media.Brushes.LightGray
                        };

                        button.Click += (sender, e) =>
                        {
                            ExecuteCommand(commandParts);
                        };

                        roomStack.Children.Add(button);
                    } else {
                        // Actions
                        string actionName = commandParts[2].Trim();

                        Button button = new Button
                        {
                            Content = text,
                            Margin = new Thickness(5),
                            Padding = new Thickness(5),
                            Background = System.Windows.Media.Brushes.LightGray
                        };

                        button.Click += (sender, e) =>
                        {
                            ExecuteCommand(commandParts);
                        };

                        actionStack.Children.Add(button);
                    }
                }
            }
        }

        private void ParseContent(string content)
        {
            string cssContent = string.Empty;
            if (File.Exists(Path.Combine(storyPath, "style.css")))
            {
                cssContent = File.ReadAllText(Path.Combine(storyPath, "style.css"));
            }
            string contentClean = "<style>" + cssContent + "</style>";
            contentClean+= Regex.Replace(content, @"#begin.*\n|#end\n*", string.Empty);
            contentClean = Regex.Replace(contentClean, @"\[\[.*?\]\]", string.Empty);
            mainContent.NavigateToString(contentClean);
        }

        private void UpdateStatusBar()
        {
            Character mc = GetCharacter("mc");

            if (mc != null)
            {
                statusStack.Children.Clear();
                Grid grid = new Grid();

                if (mc.Properties.TryGetValue("attributes", out var attributes) && attributes is JArray attributeArray)
                {
                    foreach (var attribute in attributeArray)
                    {
                        if (attribute is JObject attributeObject)
                        {
                            string attributeName = attributeObject.Value<string>("name");
                            int min = attributeObject.Value<int>("min");
                            int max = attributeObject.Value<int>("max");
                            int value = attributeObject.Value<int>("value");

                            // Erstelle und füge die ProgressBar in deinem WPF-Layout hinzu
                            ProgressBar progressBar = new ProgressBar
                            {
                                Minimum = min,
                                Maximum = max,
                                Value = value,
                                Width = 100,
                                Height = 20,
                                Margin = new Thickness(5)
                            };

                            TextBlock textBlock = new TextBlock
                            {
                                Text = attributeName + ":",
                                VerticalAlignment = VerticalAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Left,
                                Margin = new Thickness(10, 0, 0, 0),
                            };

                            grid.Children.Add(textBlock);
                            grid.Children.Add(progressBar);
                        }
                    }
                }

                statusStack.Children.Add(grid);
            }

            playerNameText.Text = mc.Properties["firstname"].ToString()
                + (mc.Properties.ContainsKey("nickname") ? " (" + mc.Properties["nickname"].ToString() + ") " : "")
                + (mc.Properties.ContainsKey("lastname") ? mc.Properties["lastname"].ToString() : "");
            gameTimeText.Text = gameTime.CurrentTime.ToString("dddd, dd. MMMM yyyy HH:mm tt");
        }

        private void ExecuteCommand(string[] command)
        {
            this.currentRoom = command[1].Trim();
            if (command.Length > 2)
            {
                this.currentAction = command[2].Trim();
            }
            else
            {
                currentAction = "start";
            }
            InitContent();
        }

        /* MEDIA HANDLING */
        private void ShowRandomMedia(string pathToSearch)
        {
            string path = Path.Combine(imagesPath, pathToSearch);
            // Debug.WriteLine("Looking for media in:\n" + path);
            if (Directory.Exists(path))
            {
                var files = Directory.GetFiles(path, "*.*");

                Random random = new Random();
                int randomImage = random.Next(0, files.Length);
                DisplayMedia(files[randomImage]);
            }
            else
            {
                mainMedia.Visibility = Visibility.Collapsed;
            }
        }

        private void DisplayMedia(string file)
        {
            // Debug.WriteLine("Showing image:\n" + file);
            mainMedia.Visibility = Visibility.Visible;
            mainMedia.Source = new Uri(file);
        }

        private void GetChars()
        {
            characters = new List<Character>();

            string path = Path.Combine(storyPath, "chars.json");
            string json = File.ReadAllText(path);
            dynamic data = JsonConvert.DeserializeObject<dynamic>(json);

            foreach (var charData in data.chars)
            {
                Character character = new Character
                {
                    Id = charData.id
                };

                foreach (var property in charData)
                {
                    if (property.Name != "id")
                    {
                        character.Properties[property.Name] = property.Value;
                    }
                }

                characters.Add(character);
            }

            // Jetzt hast du eine Liste von Character-Objekten
            foreach (var character in characters)
            {
                // Debug.WriteLine($"Id: {character.Id}");
                foreach (var property in character.Properties)
                {
                    // Debug.WriteLine($"{property.Key}: {property.Value}");
                }
            }
        }

        private Character GetCharacter(string id)
        {
            foreach (var character in characters)
            {
                if (character.Id == id)
                {
                    return character;
                }
            }

            return null;
        }

        public void UpdateGameTime(TimeSpan elapsedGameTime)
        {
            // Aktualisiere die Spielzeit
            gameTime.Update(elapsedGameTime);

            // Aktualisiere das XAML-Element mit der aktuellen Uhrzeit
            gameTimeText.Text = gameTime.CurrentTime.ToString("h:mm tt"); // Beispiel: 12:00 PM
        }

        public void saveButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Game saved");
        }

        public void loadButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Game loaded");
        }

        public void quickSaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveGameManager saveGameManager = new SaveGameManager();
            GameState gameState = new GameState
            {
                GameTime = gameTime,
                Characters = characters,
                CurrentRoom = currentRoom,
                CurrentAction = currentAction
            };
            saveGameManager.SaveGame(gameState, Path.Combine(saveGamePath, "quicksave.json"));
        }

        public void quickLoadButton_Click(object sender, RoutedEventArgs e)
        {
            SaveGameManager saveGameManager = new SaveGameManager();
            GameState gameState = saveGameManager.LoadGame(Path.Combine(saveGamePath, "quicksave.json"));
            gameTime = gameState.GameTime;
            characters = gameState.Characters;
            currentRoom = gameState.CurrentRoom;
            currentAction = gameState.CurrentAction;
            InitContent();
        }
    }
}
