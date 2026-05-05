using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Scriban;
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

        private ScriptObject templateVariables = new ScriptObject();
        private ScriptObject gameVars = new ScriptObject();

        // Static back-reference so GameFunctions' static methods can reach instance state
        private static MainWindow _instance;

        private List<Character> characters;
        private List<ItemDefinition> itemDefinitions = new List<ItemDefinition>();
        private List<QuestDefinition> questDefinitions = new List<QuestDefinition>();
        private List<CharacterSchedule> characterSchedules = new List<CharacterSchedule>();
        private Dictionary<string, int> inventory = new Dictionary<string, int>();
        private Dictionary<string, int> questProgress = new Dictionary<string, int>();

        private DateTime gameTime = DateTime.Now;
        private Uri currentMediaSource;
        private bool debugEnabled = false;

        private Stack<(string room, string action)> navigationHistory = new Stack<(string, string)>();
        private string logPath;
        private bool videoAutoplay = true;
        private List<string> savedPhotos = new List<string>();
        private System.Windows.Threading.DispatcherTimer phoneCallTimer;
        private string phoneCurrentRoom;
        private string phoneCurrentAction;
        private Dictionary<string, string> wornClothing = new Dictionary<string, string>();
        private MediaPlayer _cameraPlayer;
        private bool _settingsReady = false;

        private static string SettingsFilePath => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        private class AppSettings
        {
            public double Volume { get; set; } = 80;
            public bool VideoAutoplay { get; set; } = true;
            public string Language { get; set; } = "de";
        }

        private Dictionary<string, string> _config = new Dictionary<string, string>();
        private Dictionary<string, bool> _journalExpanded = new Dictionary<string, bool>();
        private Dictionary<string, object> _dailyDefaults = new Dictionary<string, object>();

        private List<ClothingDefinition> clothingDefinitions = new List<ClothingDefinition>();
        private HashSet<string> ownedClothing = new HashSet<string>();
        private string _wardrobeState = "main";
        private string _wardrobeCategorySelected;
        private string _wardrobeItemSelected;

        private static readonly string[] ClothingSubtypes = { "bra", "panties", "clothes", "shoes" };

        private Dictionary<string, TextBox> _setupFields = new Dictionary<string, TextBox>();

        public MainWindow()
        {
            _instance = this;
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
            logPath = Path.Combine(rootPath, "game.log");
            CheckFolder(storyPath);
            CheckFolder(roomsPath);
            CheckFolder(imagesPath);

            currentRoom = "intro";
            currentAction = "start";

            LoadConfig();
            GetCharacters();
            LoadItems();
            LoadClothes();
            LoadQuests();
            LoadSchedules();
            InitInventoryFromDefaults();
            InitClothingFromDefaults();
            AutoEquipOnStart();

            UpdateTemplateVariables();

            InitTabs();
            LoadAppSettings();
            _settingsReady = true;
            PopulateLangDropdown();
            Loc.LanguageChanged += ApplyLocalization;
            ApplyLocalization();
            ShowIntroScreen();
        }

        private void CheckFolder(string folder)
        {
            if (!Directory.Exists(folder))
                MessageBox.Show("The folder '" + folder + "' wasn't found");
        }

        private void LoadConfig()
        {
            string path = Path.Combine(storyPath, "config.json");
            if (!File.Exists(path)) return;
            try
            {
                dynamic data = JsonConvert.DeserializeObject<dynamic>(File.ReadAllText(path, System.Text.Encoding.UTF8));
                foreach (var prop in (Newtonsoft.Json.Linq.JObject)data)
                    _config[prop.Key.ToLower()] = prop.Value.ToString().ToLower();
            }
            catch { }
        }

        private void SetIntroLayout(bool introMode)
        {
            if (introMode)
            {
                mainGrid.ColumnDefinitions[1].MinWidth = 0;
                mainGrid.ColumnDefinitions[1].Width    = new GridLength(0);
                mainGrid.ColumnDefinitions[2].MinWidth = 0;
                mainGrid.ColumnDefinitions[2].Width    = new GridLength(0);
                rightPanel.Visibility  = Visibility.Collapsed;
                colSplitter.Visibility = Visibility.Collapsed;
            }
            else
            {
                mainGrid.ColumnDefinitions[1].MinWidth = 0;
                mainGrid.ColumnDefinitions[1].Width    = new GridLength(5);
                mainGrid.ColumnDefinitions[2].MinWidth = 160;
                mainGrid.ColumnDefinitions[2].Width    = new GridLength(300, GridUnitType.Star);
                rightPanel.Visibility  = Visibility.Visible;
                colSplitter.Visibility = Visibility.Visible;
            }
        }

        private void ShowIntroScreen()
        {
            currentRoom   = "intro";
            currentAction = "start";
            navigationHistory.Clear();
            UpdateBackButton();
            mainContent.Visibility = Visibility.Visible;
            SetIntroLayout(true);
            InitContent();
        }

        private void HandleStartGame()
        {
            string adultGame = _config.TryGetValue("adultgame", out var v) ? v : "no";
            if (adultGame == "yes")
            {
                navigationHistory.Push((currentRoom, currentAction));
                currentRoom   = "intro";
                currentAction = "agecheck";
                UpdateBackButton();
                InitContent();
            }
            else
            {
                ShowSetupScreen();
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
                    string fileContent = File.ReadAllText(filePath, System.Text.Encoding.UTF8);



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
                                    blockContent = RenderTemplate(blockContent);
                                    ShowRandomMedia(clearPath(currentRoom) + "/" + (currentAction == "start" ? "" : clearPath(currentAction)));
                                    ParseRooms(blockContent);
                                    ParseContent(blockContent);
                                    UpdateStatusBar();
                                    UpdateInventoryPanel();
                                    UpdateJournalPanel();
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
                    LogError("Exception in InitContent", ex.ToString());
                    ShowDebugInfo("Fehler: " + ex.Message, isError: true);
                    MessageBox.Show($"Error while reading the file: {ex.Message}");
                }
            }
            else
            {
                string msg = $"File not found: '{filePath}'";
                LogError(msg);
                ShowDebugInfo(msg, isError: true);
                MessageBox.Show(msg);
            }
        }

        private string RenderTemplate(string content)
        {
            UpdateTemplateVariables();

            // Scriban parses `func "arg" -5` as `("arg" - 5)` (binary minus on preceding string).
            // Wrap negative number literals preceded by whitespace/comma in parens to fix this.
            content = Regex.Replace(content, @"\{\{(.*?)\}\}",
                m => "{{" + Regex.Replace(m.Groups[1].Value,
                    @"(?<=[,\s])-(\d+(?:\.\d+)?)", "(-$1)") + "}}",
                RegexOptions.Singleline);

            var template = Scriban.Template.Parse(content);
            if (template.HasErrors)
            {
                string errors = string.Join(Environment.NewLine, template.Messages);
                ShowDebugInfo("Template error:\n" + errors, isError: true);
                LogError("Template parse error", errors + "\n\n--- Template content ---\n" + content);
                return $"<p style='color:red'>Template error:<br>{System.Net.WebUtility.HtmlEncode(errors)}</p>";
            }

            var context = new TemplateContext();
            context.PushGlobal(new GameFunctions());
            context.PushGlobal(templateVariables);

            try
            {
                return template.Render(context);
            }
            catch (Exception ex)
            {
                string msg = ex.Message;
                ShowDebugInfo("Template render error:\n" + msg, isError: true);
                LogError("Template render error", msg + "\n\n--- Template content ---\n" + content);
                return $"<p style='color:red'>Render error:<br>{System.Net.WebUtility.HtmlEncode(msg)}</p>";
            }
        }

        private void UpdateTemplateVariables()
        {
            // Inject every character by their ID so templates can use {{ brother.properties.firstname }}
            // without needing {{ brother = get_character "brother" }} first.
            foreach (var ch in characters ?? Enumerable.Empty<Character>())
                templateVariables[ch.Id] = ch;

            templateVariables["characters"] = characters;
            templateVariables["vars"] = gameVars;

            var invObj = new ScriptObject();
            foreach (var kv in inventory)
                invObj[kv.Key] = kv.Value;
            templateVariables["inventory"] = invObj;
        }


        private void ParseRooms(string content)
        {
            string[] commands = content.Split(new string[] { "[[", "]]" }, StringSplitOptions.RemoveEmptyEntries);
            if (commands.Length > 0)
            {
                for (int i = 1; i < commands.Length; i++)
                    commands[i - 1] = commands[i];
                Array.Resize(ref commands, commands.Length - 1);
                roomStack.Children.Clear();
                actionStack.Children.Clear();
                foreach (string command in commands)
                {
                    string[] allParts = command.Split(',');
                    if (allParts.Length < 2) continue;

                    // Optional last param: hex color starting with '#'
                    string colorHex = null;
                    string[] parts = allParts;
                    if (allParts.Last().Trim().StartsWith("#"))
                    {
                        colorHex = allParts.Last().Trim();
                        parts = allParts.Take(allParts.Length - 1).ToArray();
                    }
                    if (parts.Length < 2) continue;

                    string text     = parts[0].Trim();
                    string roomName = parts[1].Trim();

                    SolidColorBrush colorBrush = null;
                    if (colorHex != null)
                    {
                        try { colorBrush = new SolidColorBrush(
                            (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                            .ConvertFromString(colorHex)); } catch { }
                    }

                    string[] captured = parts;
                    if (parts.Length == 2)
                    {
                        var button = new Button
                        {
                            Content = MakeLabelContent(text),
                            Margin  = new Thickness(4, 0, 0, 0),
                            Height  = 36
                        };
                        if (colorBrush != null) button.Background = colorBrush;
                        button.Click += (_, __) => ExecuteCommand(captured);
                        roomStack.Children.Add(button);
                    }
                    else
                    {
                        var button = new Button
                        {
                            Content = MakeLabelContent(text),
                            Margin  = new Thickness(0, 0, 6, 0),
                            Height  = 32
                        };
                        if (colorBrush != null) button.Background = colorBrush;
                        button.Click += (_, __) => ExecuteCommand(captured);
                        actionStack.Children.Add(button);
                    }
                }
            }
        }

        // Converts a label string with simple inline HTML into a WPF TextBlock.
        // Supported tags: <small>, <b>/<strong>, <i>/<em> with optional style="color:".
        private static object MakeLabelContent(string html)
        {
            if (!html.Contains('<'))
                return html;

            var tb = new System.Windows.Controls.TextBlock { TextWrapping = System.Windows.TextWrapping.Wrap };
            var parts = Regex.Split(html, @"(<[^>]+>)");

            var stack = new Stack<(double size, System.Windows.Media.Brush color, System.Windows.FontWeight weight, System.Windows.FontStyle style)>();
            double curSize   = double.NaN;
            System.Windows.Media.Brush curColor = null;
            var curWeight    = System.Windows.FontWeights.Normal;
            var curStyle     = System.Windows.FontStyles.Normal;

            foreach (var part in parts)
            {
                if (part.StartsWith("<") && part.EndsWith(">"))
                {
                    string inner = part[1..^1].Trim();
                    if (inner.StartsWith('/'))
                    {
                        if (stack.Count > 0)
                        {
                            var prev = stack.Pop();
                            curSize = prev.size; curColor = prev.color;
                            curWeight = prev.weight; curStyle = prev.style;
                        }
                    }
                    else
                    {
                        stack.Push((curSize, curColor, curWeight, curStyle));
                        string tagName = inner.Split(' ')[0].ToLower();
                        if (tagName == "small")
                        {
                            curSize = 10;
                            var cm = Regex.Match(inner, @"color:\s*([#\w]+)");
                            if (cm.Success)
                                try { curColor = new System.Windows.Media.SolidColorBrush(
                                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                                    .ConvertFromString(cm.Groups[1].Value)); } catch { }
                        }
                        else if (tagName is "b" or "strong") curWeight = System.Windows.FontWeights.Bold;
                        else if (tagName is "i" or "em")     curStyle  = System.Windows.FontStyles.Italic;
                    }
                }
                else if (!string.IsNullOrEmpty(part))
                {
                    var run = new System.Windows.Documents.Run(part);
                    if (!double.IsNaN(curSize)) run.FontSize   = curSize;
                    if (curColor != null)        run.Foreground = curColor;
                    run.FontWeight = curWeight;
                    run.FontStyle  = curStyle;
                    tb.Inlines.Add(run);
                }
            }

            return tb;
        }

        private void ParseContent(string content)
        {
            string cssContent = string.Empty;
            if (File.Exists(Path.Combine(storyPath, "style.css")))
            {
                cssContent = File.ReadAllText(Path.Combine(storyPath, "style.css"), System.Text.Encoding.UTF8);
            }
            string contentClean = "<meta charset=\"utf-8\"><style>" + cssContent + "</style>";
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

                if (mc.Properties.TryGetValue("attributes", out var attributes) && attributes is JObject attributesObject)
                {
                    foreach (var attribute in attributesObject)
                    {
                        var attributeValues = attribute.Value as JObject;

                        if (attributeValues?.Value<bool?>("hidden") == true)
                            continue;

                        int min = attributeValues.Value<int>("min");
                        int max = attributeValues.Value<int>("max");
                        int value = attributeValues.Value<int>("value");
                        string name = attributeValues.Value<string>("name");

                        var wrapper = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

                        // Label row: attribute name left, value/max right
                        var labelRow = new Grid();
                        labelRow.Children.Add(new TextBlock
                        {
                            Text = name,
                            HorizontalAlignment = HorizontalAlignment.Left,
                            Foreground = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromRgb(0xAB, 0xAB, 0xAB)),
                            FontSize = 11
                        });
                        labelRow.Children.Add(new TextBlock
                        {
                            Text = $"{value} / {max}",
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Foreground = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66)),
                            FontSize = 11
                        });

                        wrapper.Children.Add(labelRow);
                        wrapper.Children.Add(new ProgressBar
                        {
                            Minimum = min,
                            Maximum = max,
                            Value = value,
                            Margin = new Thickness(0, 4, 0, 0)
                        });

                        statusStack.Children.Add(wrapper);
                    }
                }
            }

            playerNameText.Text = mc.Properties["firstname"].ToString()
                + (mc.Properties.ContainsKey("nickname") ? " (" + mc.Properties["nickname"].ToString() + ") " : "")
                + (mc.Properties.ContainsKey("lastname") ? mc.Properties["lastname"].ToString() : "");
            var culture = new System.Globalization.CultureInfo(Loc.CurrentLanguage == "en" ? "en-US" : "de-DE");
            gameTimeText.Text = gameTime.ToString("ddd, dd.MM.yyyy  HH:mm", culture);
        }



        private void ExecuteCommand(string[] command)
        {
            string dest   = command[1].Trim();
            string action = command.Length > 2 ? command[2].Trim() : "start";

            // ── Special destinations ──────────────────────────────────────────
            switch (dest)
            {
                case "__quit__":
                    Application.Current.Shutdown();
                    return;

                case "__load__":
                    var dlg = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = Loc.T("btn.load"),
                        Filter = "ACVN Spielstand (*.acvnsave)|*.acvnsave",
                        InitialDirectory = saveGamePath
                    };
                    if (dlg.ShowDialog() == true)
                    {
                        RestoreGameState(new SaveGameManager().LoadGame(dlg.FileName));
                        mainContent.Visibility = Visibility.Visible;
                        InitContent();
                    }
                    return;

                case "__quickload__":
                    string qpath = Path.Combine(saveGamePath, "quicksave.acvnsave");
                    if (!File.Exists(qpath)) { MessageBox.Show(Loc.T("confirm.noQuicksave")); return; }
                    RestoreGameState(new SaveGameManager().LoadGame(qpath));
                    mainContent.Visibility = Visibility.Visible;
                    InitContent();
                    return;

                case "__startgame__":
                    HandleStartGame();
                    return;

                case "__ageyes__":
                    ShowSetupScreen();
                    return;

                case "__ageno__":
                    MessageBox.Show(
                        Loc.T("age.denied_msg"),
                        Loc.T("age.denied_title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    Application.Current.Shutdown();
                    return;
            }
            // ─────────────────────────────────────────────────────────────────

            if (action == "wardrobe")
            {
                ShowWardrobe();
                return;
            }
            navigationHistory.Push((currentRoom, currentAction));
            currentRoom   = dest;
            currentAction = action;
            UpdateBackButton();
            InitContent();
        }

        public void backButton_Click(object sender, RoutedEventArgs e)
        {
            if (navigationHistory.Count == 0) return;
            settingsPanel.Visibility = Visibility.Collapsed;
            (currentRoom, currentAction) = navigationHistory.Pop();
            UpdateBackButton();
            InitContent();
        }

        private void UpdateBackButton()
        {
            bool can = navigationHistory.Count > 0;
            backButton.IsEnabled = can;
            settingsBackButton.IsEnabled = can;
        }

        /* MEDIA HANDLING */
        private void ShowRandomMedia(string pathToSearch)
        {
            // Walk up the hierarchy until a folder with media files is found.
            // e.g. "home/room/bed_naked" → "home/room" → "home"
            string search = pathToSearch.TrimEnd('/');
            while (true)
            {
                if (!string.IsNullOrEmpty(search))
                {
                    string path = Path.GetFullPath(Path.Combine(imagesPath, search));
                    if (Directory.Exists(path))
                    {
                        var files = Directory.GetFiles(path, "*.*");
                        if (files.Length > 0)
                        {
                            int idx = new Random().Next(0, files.Length);
                            DisplayMedia(files[idx]);
                            ShowDebugInfo($"Media [{search}]: {Path.GetFileName(files[idx])}", isError: false);
                            return;
                        }
                    }
                }

                int lastSlash = search.LastIndexOf('/');
                if (lastSlash < 0)
                {
                    // No media found at any level
                    mainMedia.Visibility = Visibility.Collapsed;
                    mainImage.Visibility = Visibility.Collapsed;
                    mediaFullscreenHint.Visibility = Visibility.Collapsed;
                    ShowDebugInfo($"No media found for: {pathToSearch}", isError: true);
                    return;
                }
                search = search[..lastSlash];
            }
        }

        private void DisplayMedia(string file)
        {
            currentMediaSource = new Uri(file);
            bool isVideo = VideoExtensions.Contains(Path.GetExtension(file));

            if (isVideo)
            {
                mainImage.Visibility = Visibility.Collapsed;
                mainImage.Source = null;
                mainMedia.Volume = volumeSlider?.Value / 100.0 ?? 0.8;
                mainMedia.Source = currentMediaSource;
                mainMedia.Visibility = Visibility.Visible;
            }
            else
            {
                mainMedia.Stop();
                mainMedia.Source = null;
                mainMedia.Visibility = Visibility.Collapsed;
                mainImage.Source = new System.Windows.Media.Imaging.BitmapImage(currentMediaSource);
                mainImage.Visibility = Visibility.Visible;
            }
            mediaFullscreenHint.Visibility = Visibility.Visible;
        }

        private void mainMedia_MediaOpened(object sender, RoutedEventArgs e)
        {
            bool isVideo = mainMedia.NaturalDuration.HasTimeSpan
                           && mainMedia.NaturalDuration.TimeSpan.TotalSeconds > 0;
            mainMedia.Play();
            if (isVideo && !videoAutoplay)
            {
                mainMedia.Pause();
                mainMedia.Position = TimeSpan.Zero;
            }
        }

        private void mainMedia_MediaEnded(object sender, RoutedEventArgs e)
        {
            mainMedia.Position = TimeSpan.Zero;
            mainMedia.Play();
        }

        private void mediaModalVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            mediaModalVideo.Position = TimeSpan.Zero;
            mediaModalVideo.Play();
        }

        public void mediaArea_FullscreenBtn_Click(object sender, RoutedEventArgs e)
        {
            if (currentMediaSource == null) return;
            ShowMediaModal(currentMediaSource);
        }

        public void volumeSlider_Changed(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            int pct = (int)volumeSlider.Value;
            volumeLabel.Text = pct + "%";
            mainMedia.Volume = pct / 100.0;
            SaveAppSettings();
        }

        public void videoAutoplay_Changed(object sender, RoutedEventArgs e)
        {
            videoAutoplay = videoAutoplayToggle.IsChecked == true;
            SaveAppSettings();
        }

        private void LoadAppSettings()
        {
            try
            {
                if (!File.Exists(SettingsFilePath)) return;
                var s = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(SettingsFilePath));
                if (s == null) return;
                volumeSlider.Value = s.Volume;
                videoAutoplayToggle.IsChecked = s.VideoAutoplay;
                videoAutoplay = s.VideoAutoplay;
                if (!string.IsNullOrEmpty(s.Language))
                    Loc.SetLanguage(s.Language);
            }
            catch { /* ignore corrupt settings */ }
        }

        private void SaveAppSettings()
        {
            if (!_settingsReady) return;
            try
            {
                var s = new AppSettings { Volume = volumeSlider.Value, VideoAutoplay = videoAutoplay, Language = Loc.CurrentLanguage };
                File.WriteAllText(SettingsFilePath, JsonConvert.SerializeObject(s, Formatting.Indented));
            }
            catch { }
        }

        private void LogError(string message, string detail = null)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]");
                sb.Append($" [{currentRoom}/{currentAction}]");
                sb.AppendLine($" {message}");
                if (detail != null)
                    foreach (var line in detail.Split('\n'))
                        sb.AppendLine($"    {line.TrimEnd()}");
                File.AppendAllText(logPath, sb.ToString(), System.Text.Encoding.UTF8);
            }
            catch { /* log must never crash the app */ }
        }

        private void ShowDebugInfo(string message, bool isError)
        {
            Debug.WriteLine($"[ACVN] {message}");
            if (!debugEnabled) return;
            debugText.Text = message;
            debugText.Foreground = isError
                ? System.Windows.Media.Brushes.OrangeRed
                : System.Windows.Media.Brushes.DarkGray;
            debugPanel.Visibility = Visibility.Visible;
        }

        public void restartButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                Loc.T("confirm.restart"),
                Loc.T("confirm.restart.title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            settingsPanel.Visibility = Visibility.Collapsed;
            gameTime = DateTime.Now;
            gameVars.Clear();
            navigationHistory.Clear();
            InitInventoryFromDefaults();
            questProgress.Clear();
            wornClothing.Clear();
            InitClothingFromDefaults();
            AutoEquipOnStart();
            GetCharacters();
            UpdateTemplateVariables();
            ShowIntroScreen();
        }

        public void settingsButton_Click(object sender, RoutedEventArgs e)
        {
            settingsPanel.Visibility = settingsPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        public void debugToggle_Changed(object sender, RoutedEventArgs e)
        {
            debugEnabled = debugToggle.IsChecked == true;
            if (!debugEnabled)
                debugPanel.Visibility = Visibility.Collapsed;
        }

        public void mediaArea_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (currentMediaSource == null || mainMedia.Visibility != Visibility.Visible)
                return;
            ShowMediaModal(currentMediaSource);
        }

        private static readonly System.Collections.Generic.HashSet<string> VideoExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".avi", ".mkv", ".wmv", ".mov", ".webm", ".flv" };

        private void ShowMediaModal(Uri source)
        {
            bool isVideo = VideoExtensions.Contains(Path.GetExtension(source.LocalPath));
            if (isVideo)
            {
                mediaModalImage.Visibility = Visibility.Collapsed;
                mediaModalVideo.Volume = volumeSlider?.Value / 100.0 ?? 0.8;
                mediaModalVideo.Source = source;
                mediaModalVideo.Visibility = Visibility.Visible;
            }
            else
            {
                mediaModalVideo.Stop();
                mediaModalVideo.Source = null;
                mediaModalVideo.Visibility = Visibility.Collapsed;
                mediaModalImage.Source = new System.Windows.Media.Imaging.BitmapImage(source);
                mediaModalImage.Visibility = Visibility.Visible;
            }
            // Hide HWND-based controls so they don't bleed through the overlay
            mainContent.Visibility = Visibility.Hidden;
            mainMedia.Visibility   = Visibility.Hidden;
            mediaModalOverlay.Visibility = Visibility.Visible;
        }

        private void mediaModalVideo_MediaOpened(object sender, RoutedEventArgs e)
            => mediaModalVideo.Play();

        public void mediaModalOverlay_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            mediaModalVideo.Stop();
            mediaModalVideo.Source = null;
            mediaModalImage.Source = null;
            mediaModalOverlay.Visibility = Visibility.Collapsed;
            // Only restore HWND controls if no other overlay is keeping them hidden
            if (phoneAppOverlay.Visibility != Visibility.Visible
                && wardrobeOverlay.Visibility != Visibility.Visible)
            {
                mainContent.Visibility = Visibility.Visible;
                if (currentMediaSource != null)
                    mainMedia.Visibility = VideoExtensions.Contains(
                        Path.GetExtension(currentMediaSource.LocalPath))
                        ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /* ITEM INFO OVERLAY */
        private void ShowItemInfo(string itemId)
        {
            var def = itemDefinitions.FirstOrDefault(i => i.Id == itemId);
            itemInfoName.Text = def?.Name ?? itemId;
            itemInfoDesc.Text = def?.Description ?? string.Empty;
            itemInfoDesc.Visibility = string.IsNullOrEmpty(itemInfoDesc.Text)
                ? Visibility.Collapsed : Visibility.Visible;

            // Try to find an image in story/images/items/<id>.*
            string imgFolder = Path.Combine(imagesPath, "items");
            string imgFile = null;
            if (Directory.Exists(imgFolder))
            {
                string[] exts = { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" };
                imgFile = exts.Select(ext => Path.Combine(imgFolder, itemId + ext))
                              .FirstOrDefault(File.Exists);
            }
            if (imgFile != null)
            {
                itemInfoImage.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(imgFile));
                itemInfoImage.Visibility = Visibility.Visible;
            }
            else
            {
                itemInfoImage.Visibility = Visibility.Collapsed;
            }

            // Hide HWND-based controls so they don't bleed through the overlay
            mainContent.Visibility = Visibility.Hidden;
            mainMedia.Visibility   = Visibility.Hidden;
            mainImage.Visibility   = Visibility.Hidden;
            itemInfoOverlay.Visibility = Visibility.Visible;
        }

        private void CloseItemInfo()
        {
            itemInfoOverlay.Visibility = Visibility.Collapsed;
            mainContent.Visibility = Visibility.Visible;
            if (currentMediaSource != null)
            {
                bool isVid = VideoExtensions.Contains(Path.GetExtension(currentMediaSource.LocalPath));
                mainMedia.Visibility = isVid ? Visibility.Visible : Visibility.Collapsed;
                mainImage.Visibility = isVid ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        public void itemInfoClose_Click(object sender, RoutedEventArgs e)
            => CloseItemInfo();

        public void itemInfoOverlay_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => CloseItemInfo();

        public void itemInfoCard_StopPropagation(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => e.Handled = true;

        private void LoadItems()
        {
            string path = Path.Combine(storyPath, "items.json");
            if (!File.Exists(path)) return;
            dynamic data = JsonConvert.DeserializeObject<dynamic>(
                File.ReadAllText(path, System.Text.Encoding.UTF8));
            itemDefinitions.Clear();
            foreach (var item in data.items)
            {
                string rawId = item.id?.ToString();
                string fallbackId = item.name?.ToString()?.ToLower()?.Replace(" ", "_");
                itemDefinitions.Add(new ItemDefinition
                {
                    Id = rawId ?? fallbackId,
                    Type = item.type?.ToString(),
                    Subtype = item.subtype?.ToString(),
                    Name = item.name?.ToString(),
                    Description = item.description?.ToString(),
                    StartingQuantity = item.starting_quantity != null ? (int)item.starting_quantity : 0
                });
            }
        }

        private void LoadClothes()
        {
            string path = Path.Combine(storyPath, "clothes.json");
            if (!File.Exists(path)) return;
            dynamic data = JsonConvert.DeserializeObject<dynamic>(
                File.ReadAllText(path, System.Text.Encoding.UTF8));
            clothingDefinitions.Clear();
            foreach (var item in data.clothes)
            {
                var def = new ClothingDefinition
                {
                    Id          = item.id?.ToString(),
                    Name        = item.name?.ToString(),
                    Description = item.description?.ToString(),
                    Subtype     = item.subtype?.ToString(),
                    Durability  = item.durability != null ? (int)item.durability : 100,
                    Daring      = item.daring    != null ? (int)item.daring     : 0,
                    Inhibition  = item.inhibition != null ? (int)item.inhibition : 0,
                    Image       = item.image?.ToString()
                };
                if (item.tags != null)
                    foreach (var tag in item.tags)
                        def.Tags.Add(tag.ToString());
                clothingDefinitions.Add(def);
            }
        }

        private void InitClothingFromDefaults()
        {
            ownedClothing = new HashSet<string>(clothingDefinitions.Select(c => c.Id));
        }

        private void AutoEquipOnStart()
        {
            var rng = new Random();
            foreach (string subtype in ClothingSubtypes)
            {
                if (wornClothing.ContainsKey(subtype)) continue;
                var options = clothingDefinitions
                    .Where(c => c.Subtype == subtype && ownedClothing.Contains(c.Id)
                             && c.Tags != null && c.Tags.Contains("basic"))
                    .ToList();
                if (options.Count > 0)
                    wornClothing[subtype] = options[rng.Next(options.Count)].Id;
            }
        }

        private string FindClothingImage(ClothingDefinition def)
        {
            if (!string.IsNullOrEmpty(def.Image))
            {
                string abs = Path.GetFullPath(Path.Combine(imagesPath, def.Image.TrimStart('/')));
                if (File.Exists(abs)) return abs;
            }
            string folder = Path.Combine(imagesPath, "clothes");
            if (Directory.Exists(folder))
            {
                string[] exts = { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
                string found = exts.Select(e => Path.Combine(folder, def.Id + e)).FirstOrDefault(File.Exists);
                if (found != null) return found;
            }
            return null;
        }

        private int GetMcInhibition()
        {
            var mc = GetCharacter("mc");
            if (mc?.Properties?.TryGetValue("attributes", out var attrs) == true
                && attrs is JObject attrsObj
                && attrsObj.TryGetValue("inhibition", out var tok)
                && tok is JObject attrObj)
                return attrObj.Value<int>("value");
            return 100;
        }

        private void LoadQuests()
        {
            string path = Path.Combine(storyPath, "quests.json");
            if (!File.Exists(path)) return;
            dynamic data = JsonConvert.DeserializeObject<dynamic>(
                File.ReadAllText(path, System.Text.Encoding.UTF8));
            questDefinitions.Clear();
            foreach (var quest in data.quests)
            {
                var def = new QuestDefinition
                {
                    Id   = quest.id.ToString(),
                    Name = quest.name.ToString(),
                    Hint = quest.hint?.ToString()
                };
                foreach (var step in quest.steps)
                    def.Steps.Add(new QuestStepDef
                    {
                        Id = step.id.ToString(),
                        Description = step.description.ToString()
                    });
                questDefinitions.Add(def);
            }
        }

        private void LoadSchedules()
        {
            string path = Path.Combine(storyPath, "schedules.json");
            if (!File.Exists(path)) return;
            dynamic data = JsonConvert.DeserializeObject<dynamic>(
                File.ReadAllText(path, System.Text.Encoding.UTF8));
            characterSchedules.Clear();
            foreach (var sched in data.schedules)
            {
                var cs = new CharacterSchedule { CharId = sched.char_id.ToString() };
                foreach (var entry in sched.entries)
                {
                    var e = new ScheduleEntry
                    {
                        Days     = entry.days?.ToString(),
                        From     = entry.from?.ToString(),
                        To       = entry.to?.ToString(),
                        Activity = entry.activity?.ToString()
                    };
                    var loc = entry.location;
                    if (loc is Newtonsoft.Json.Linq.JArray arr)
                        e.Locations = arr.Select(t => t.ToString()).ToList();
                    else if (loc != null)
                        e.Locations = new List<string> { loc.ToString() };
                    cs.Entries.Add(e);
                }
                characterSchedules.Add(cs);
            }
        }

        private static int ParseTimeToMinutes(string time)
        {
            if (string.IsNullOrEmpty(time)) return 0;
            var p = time.Split(':');
            return int.Parse(p[0]) * 60 + (p.Length > 1 ? int.Parse(p[1]) : 0);
        }

        private (string location, string activity) GetCharScheduleEntry(string charId)
        {
            var sched = characterSchedules.FirstOrDefault(s => s.CharId == charId);
            if (sched == null) return (null, null);

            var now = gameTime;
            int dow    = (int)now.DayOfWeek;
            bool wd    = dow >= 1 && dow <= 5;
            bool we    = !wd;
            int nowMin = now.Hour * 60 + now.Minute;

            foreach (var entry in sched.Entries)
            {
                bool dayMatch = (entry.Days?.ToLower()) switch
                {
                    "weekdays" => wd,
                    "weekends" => we,
                    "all"      => true,
                    null       => true,
                    _          => entry.Days.Split(',')
                                       .Select(s => int.TryParse(s.Trim(), out int d) ? d : -1)
                                       .Contains(dow)
                };
                if (!dayMatch) continue;

                int fromMin = ParseTimeToMinutes(entry.From);
                int toMin   = ParseTimeToMinutes(entry.To);

                bool inRange = fromMin <= toMin
                    ? nowMin >= fromMin && nowMin < toMin
                    : nowMin >= fromMin || nowMin < toMin;   // spans midnight

                if (!inRange) continue;

                string loc = entry.Locations.Count switch
                {
                    0 => null,
                    1 => entry.Locations[0],
                    _ => entry.Locations[Math.Abs(now.Year * 10000 + now.Month * 100 + now.Day) % entry.Locations.Count]
                };
                return (loc, entry.Activity);
            }
            return (null, null);
        }

        private string GetCharCurrentLocation(string charId) => GetCharScheduleEntry(charId).location;
        private string GetCharCurrentActivity(string charId) => GetCharScheduleEntry(charId).activity;

        private void InitInventoryFromDefaults()
        {
            inventory.Clear();
            foreach (var item in itemDefinitions)
                if (item.StartingQuantity > 0)
                    inventory[item.Id] = item.StartingQuantity;
        }

        private void UpdateInventoryPanel()
        {
            inventoryStack.Children.Clear();

            // — Kleidung —
            AddInventorySectionHeader(Loc.T("inv.clothing"));
            var clothingItems = ownedClothing
                .Select(id => clothingDefinitions.FirstOrDefault(c => c.Id == id))
                .Where(d => d != null).ToList();

            if (clothingItems.Count == 0)
                AddInventoryEmptyNote();
            else
                foreach (var def in clothingItems)
                {
                    bool isWorn = wornClothing.TryGetValue(def.Subtype, out var wId) && wId == def.Id;
                    string capturedId = def.Id;

                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var namePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    namePanel.Children.Add(new TextBlock
                    {
                        Text              = def.Name,
                        FontSize          = 13,
                        Foreground        = Brushes.White,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    if (isWorn)
                        namePanel.Children.Add(new Border
                        {
                            Background      = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)),
                            CornerRadius    = new CornerRadius(3),
                            Margin          = new Thickness(6, 0, 0, 0),
                            Padding         = new Thickness(4, 1, 4, 1),
                            Child           = new TextBlock { Text = Loc.T("wardrobe.worn"), FontSize = 8, Foreground = Brushes.White }
                        });
                    Grid.SetColumn(namePanel, 0);
                    row.Children.Add(namePanel);

                    var subtypeLabel = new TextBlock
                    {
                        Text              = Loc.T("wardrobe." + def.Subtype),
                        FontSize          = 10,
                        Foreground        = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin            = new Thickness(8, 0, 0, 0)
                    };
                    Grid.SetColumn(subtypeLabel, 1);
                    row.Children.Add(subtypeLabel);

                    var discardBtn = new Button
                    {
                        Content     = "✕",
                        Height = 20, Width = 20,
                        Padding     = new Thickness(0),
                        Margin      = new Thickness(6, 0, 0, 0),
                        Foreground  = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)),
                        Background  = Brushes.Transparent,
                        BorderBrush = Brushes.Transparent,
                        FontSize    = 9
                    };
                    discardBtn.Click += (_, re) =>
                    {
                        re.Handled = true;
                        var d = clothingDefinitions.FirstOrDefault(c => c.Id == capturedId);
                        if (d == null) return;
                        var result = MessageBox.Show(Loc.T("confirm.discard", d.Name), Loc.T("confirm.discard.title"), MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (result != MessageBoxResult.Yes) return;
                        if (wornClothing.TryGetValue(d.Subtype, out var w) && w == capturedId)
                            wornClothing.Remove(d.Subtype);
                        ownedClothing.Remove(capturedId);
                        UpdateInventoryPanel();
                    };
                    Grid.SetColumn(discardBtn, 2);
                    row.Children.Add(discardBtn);

                    var btn = new Button
                    {
                        Content                    = row,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        Background                 = Brushes.Transparent,
                        BorderBrush                = Brushes.Transparent,
                        Padding                    = new Thickness(6, 5, 6, 5),
                        Margin                     = new Thickness(0, 2, 0, 0),
                        Cursor                     = System.Windows.Input.Cursors.Hand
                    };
                    string capturedDefId = capturedId;
                    btn.Click += (_, __) =>
                    {
                        _wardrobeState        = "item";
                        _wardrobeItemSelected = capturedDefId;
                        ShowWardrobe(resetState: false);
                    };
                    inventoryStack.Children.Add(btn);
                }

            inventoryStack.Children.Add(new System.Windows.Shapes.Rectangle { Height = 10 });

            // — Items —
            AddInventorySectionHeader(Loc.T("inv.items"));
            var visibleItems = inventory.Where(kv => kv.Value > 0).ToList();
            if (visibleItems.Count == 0)
                AddInventoryEmptyNote();
            else
                foreach (var kv in visibleItems)
                {
                    var    iDef        = itemDefinitions.FirstOrDefault(i => i.Id == kv.Key);
                    string displayName = iDef?.Name ?? kv.Key;
                    string itemId      = kv.Key;

                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var nameBlock = new TextBlock
                    {
                        Text              = displayName,
                        Foreground        = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
                        FontSize          = 13,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(nameBlock, 0);
                    row.Children.Add(nameBlock);

                    var qtyBlock = new TextBlock
                    {
                        Text              = $"×{kv.Value}",
                        Foreground        = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
                        FontSize          = 12,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin            = new Thickness(8, 0, 0, 0)
                    };
                    Grid.SetColumn(qtyBlock, 1);
                    row.Children.Add(qtyBlock);

                    var discardBtn = new Button
                    {
                        Content     = "✕",
                        Height = 20, Width = 20,
                        Padding     = new Thickness(0),
                        Margin      = new Thickness(6, 0, 0, 0),
                        Foreground  = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)),
                        Background  = Brushes.Transparent,
                        BorderBrush = Brushes.Transparent,
                        FontSize    = 9
                    };
                    string capturedItemId = itemId;
                    discardBtn.Click += (_, re) =>
                    {
                        re.Handled = true;
                        var d      = itemDefinitions.FirstOrDefault(i => i.Id == capturedItemId);
                        string nm  = d?.Name ?? capturedItemId;
                        var result = MessageBox.Show(Loc.T("confirm.discard", nm), Loc.T("confirm.discard.title"), MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (result != MessageBoxResult.Yes) return;
                        inventory.Remove(capturedItemId);
                        UpdateInventoryPanel();
                    };
                    Grid.SetColumn(discardBtn, 2);
                    row.Children.Add(discardBtn);

                    var btn = new Button
                    {
                        Content                    = row,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        Background                 = Brushes.Transparent,
                        BorderBrush                = Brushes.Transparent,
                        Padding                    = new Thickness(6, 5, 6, 5),
                        Margin                     = new Thickness(0, 2, 0, 0),
                        Cursor                     = System.Windows.Input.Cursors.Hand
                    };
                    btn.Click += (_, __) => ShowItemInfo(itemId);
                    inventoryStack.Children.Add(btn);
                }
        }

        private void AddInventorySectionHeader(string text)
        {
            inventoryStack.Children.Add(new TextBlock
            {
                Text       = text.ToUpper(),
                FontSize   = 9, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                Margin     = new Thickness(0, 4, 0, 2)
            });
        }

        private void AddInventoryEmptyNote()
        {
            inventoryStack.Children.Add(new TextBlock
            {
                Text       = Loc.T("inv.empty"),
                FontSize   = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                Margin     = new Thickness(8, 2, 0, 0)
            });
        }

        private void UpdateJournalPanel()
        {
            journalStack.Children.Clear();

            var active    = questDefinitions.Where(d => questProgress.TryGetValue(d.Id, out int s) && s < d.Steps.Count).ToList();
            var open      = questDefinitions.Where(d => !questProgress.ContainsKey(d.Id)).ToList();
            var completed = questDefinitions.Where(d => questProgress.TryGetValue(d.Id, out int s) && s >= d.Steps.Count).ToList();

            AddJournalSection(Loc.T("journal.active"), active,    "aktiv", _journalExpanded.TryGetValue("aktiv", out bool ea) ? ea : true);
            AddJournalSection(Loc.T("journal.open"),   open,      "offen", _journalExpanded.TryGetValue("offen", out bool eo) ? eo : false);
            AddJournalSection(Loc.T("journal.done"),   completed, "done",  _journalExpanded.TryGetValue("done",  out bool ed) ? ed : false);
        }

        private void AddJournalSection(string title, List<QuestDefinition> quests, string kind, bool expanded)
        {
            // Header
            var chevron = new TextBlock
            {
                Text = expanded ? "▲" : "▼",
                FontSize = 9,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Center
            };
            var headerGrid = new Grid();
            headerGrid.Children.Add(new TextBlock
            {
                Text       = title,
                FontSize   = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88))
            });
            headerGrid.Children.Add(chevron);

            var header = new Border
            {
                Background    = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x22, 0x22, 0x22)),
                CornerRadius  = new CornerRadius(4),
                Padding       = new Thickness(8, 6, 8, 6),
                Margin        = new Thickness(0, 8, 0, 0),
                Cursor        = System.Windows.Input.Cursors.Hand,
                Child         = headerGrid
            };

            // Content
            var content = new StackPanel
            {
                Margin     = new Thickness(0, 2, 0, 0),
                Visibility = expanded ? Visibility.Visible : Visibility.Collapsed
            };

            string capturedKind = kind;
            header.MouseLeftButtonUp += (_, __) =>
            {
                bool vis = content.Visibility == Visibility.Visible;
                content.Visibility           = vis ? Visibility.Collapsed : Visibility.Visible;
                chevron.Text                 = vis ? "▼" : "▲";
                _journalExpanded[capturedKind] = !vis;
            };

            journalStack.Children.Add(header);
            journalStack.Children.Add(content);

            if (quests.Count == 0)
            {
                content.Children.Add(new TextBlock
                {
                    Text       = Loc.T("journal.empty"),
                    FontSize   = 11,
                    Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44)),
                    Margin     = new Thickness(8, 4, 0, 0)
                });
                return;
            }

            foreach (var def in quests)
            {
                questProgress.TryGetValue(def.Id, out int step);

                string icon  = kind == "done" ? "✓ " : kind == "aktiv" ? "◉ " : "○ ";
                var nameColor = kind == "done"
                    ? System.Windows.Media.Color.FromRgb(0x55, 0xAA, 0x55)
                    : kind == "aktiv"
                        ? System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0)
                        : System.Windows.Media.Color.FromRgb(0x99, 0x99, 0x99);

                content.Children.Add(new TextBlock
                {
                    Text       = icon + def.Name,
                    FontSize   = 13,
                    FontWeight = kind == "aktiv" ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = new System.Windows.Media.SolidColorBrush(nameColor),
                    Margin     = new Thickness(8, 6, 0, 2)
                });

                if (kind == "aktiv" && step < def.Steps.Count)
                    content.Children.Add(new TextBlock
                    {
                        Text        = "→ " + def.Steps[step].Description,
                        FontSize    = 11,
                        Foreground  = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0xF5, 0xA6, 0x23)),
                        Margin      = new Thickness(18, 0, 0, 0),
                        TextWrapping = System.Windows.TextWrapping.Wrap
                    });

                if (kind == "offen" && !string.IsNullOrEmpty(def.Hint))
                    content.Children.Add(new TextBlock
                    {
                        Text        = "💡 " + def.Hint,
                        FontSize    = 11,
                        FontStyle   = FontStyles.Italic,
                        Foreground  = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66)),
                        Margin      = new Thickness(18, 0, 0, 0),
                        TextWrapping = System.Windows.TextWrapping.Wrap
                    });
            }
        }

        private void GetCharacters()
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
                Debug.WriteLine($"Id: {character.Id}");
                foreach (var property in character.Properties)
                {
                    Debug.WriteLine($"{property.Key}: {property.Value}");
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

        public void saveButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = Loc.T("btn.save"),
                Filter = "ACVN Spielstand (*.acvnsave)|*.acvnsave",
                DefaultExt = "acvnsave",
                InitialDirectory = saveGamePath
            };
            if (dialog.ShowDialog() == true)
            {
                new SaveGameManager().SaveGame(BuildGameState(), dialog.FileName);
                MessageBox.Show(Loc.T("confirm.save.done"));
            }
        }

        public void loadButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Spielstand laden",
                Filter = "ACVN Spielstand (*.acvnsave)|*.acvnsave",
                InitialDirectory = saveGamePath
            };
            if (dialog.ShowDialog() == true)
            {
                RestoreGameState(new SaveGameManager().LoadGame(dialog.FileName));
                InitContent();
            }
        }

        public void quickSaveButton_Click(object sender, RoutedEventArgs e)
        {
            new SaveGameManager().SaveGame(BuildGameState(), Path.Combine(saveGamePath, "quicksave.acvnsave"));
            MessageBox.Show(Loc.T("confirm.quicksave.done"));
        }

        public void quickLoadButton_Click(object sender, RoutedEventArgs e)
        {
            string path = Path.Combine(saveGamePath, "quicksave.acvnsave");
            if (!File.Exists(path))
            {
                MessageBox.Show(Loc.T("confirm.noQuicksave"));
                return;
            }
            RestoreGameState(new SaveGameManager().LoadGame(path));
            InitContent();
        }

        private GameState BuildGameState() => new GameState
        {
            GameTime = gameTime,
            Characters = characters,
            CurrentRoom = currentRoom,
            CurrentAction = currentAction,
            GameVariables = new Dictionary<string, object>(
                gameVars.Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value))),
            Inventory = new Dictionary<string, int>(inventory),
            QuestProgress = new Dictionary<string, int>(questProgress),
            WornClothing = new Dictionary<string, string>(wornClothing),
            OwnedClothing = ownedClothing.ToList()
        };

        private void RestoreGameState(GameState state)
        {
            gameTime = state.GameTime;
            characters = state.Characters;
            currentRoom = state.CurrentRoom;
            currentAction = state.CurrentAction;
            gameVars.Clear();
            foreach (var kv in state.GameVariables)
                gameVars[kv.Key] = kv.Value;
            inventory = state.Inventory ?? new Dictionary<string, int>();
            questProgress = state.QuestProgress ?? new Dictionary<string, int>();
            wornClothing = state.WornClothing ?? new Dictionary<string, string>();
            ownedClothing = state.OwnedClothing?.Count > 0
                ? new HashSet<string>(state.OwnedClothing)
                : new HashSet<string>(clothingDefinitions.Select(c => c.Id));
            navigationHistory.Clear();
            UpdateBackButton();
        }

        /* TAB NAVIGATION */
        private Button[] tabButtons;
        private System.Windows.FrameworkElement[] tabPanels;

        private void InitTabs()
        {
            tabButtons = new[] { tabBtnStatus, tabBtnPhone, tabBtnInventory, tabBtnJournal, tabBtnRelationships, tabBtnGameSettings };
            tabPanels  = new System.Windows.FrameworkElement[] { panelStatus, panelPhone, panelInventory, panelJournal, panelRelationships, panelGameSettings };
            SwitchTab(0);
        }

        private void SwitchTab(int index)
        {
            var accent   = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4));
            var inactive = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6A, 0x6A, 0x6A));
            for (int i = 0; i < tabPanels.Length; i++)
            {
                tabPanels[i].Visibility   = i == index ? Visibility.Visible : Visibility.Collapsed;
                tabButtons[i].BorderBrush = i == index ? accent : System.Windows.Media.Brushes.Transparent;
                tabButtons[i].Foreground  = i == index ? System.Windows.Media.Brushes.White : inactive;
            }
        }

        public void tabBtnStatus_Click(object sender, RoutedEventArgs e)          => SwitchTab(0);
        public void tabBtnPhone_Click(object sender, RoutedEventArgs e)           => SwitchTab(1);
        public void tabBtnInventory_Click(object sender, RoutedEventArgs e)       => SwitchTab(2);
        public void tabBtnJournal_Click(object sender, RoutedEventArgs e)         => SwitchTab(3);
        public void tabBtnRelationships_Click(object sender, RoutedEventArgs e)   { SwitchTab(4); UpdateRelationshipsPanel(); }
        public void tabBtnGameSettings_Click(object sender, RoutedEventArgs e)    => SwitchTab(5);

        /* RELATIONSHIPS PANEL */
        private void UpdateRelationshipsPanel()
        {
            relationshipsStack.Children.Clear();
            foreach (var ch in characters)
            {
                if (ch.Id == "mc") continue;

                string firstName = ch.Properties.TryGetValue("firstname", out var fn) ? fn.ToString() : ch.Id;
                string relation  = ch.Properties.TryGetValue("relation",  out var rel) ? rel.ToString() : string.Empty;
                string age       = ch.Properties.TryGetValue("age",       out var ag)  ? ag.ToString()  : string.Empty;

                // Card container
                var card = new Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x22, 0x22, 0x22)),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(0, 0, 0, 10),
                    Padding = new Thickness(10)
                };

                var inner = new StackPanel();

                // Portrait image
                string imgFolder = Path.Combine(imagesPath, "chars");
                string[] exts = { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
                string imgFile = exts.Select(e => Path.Combine(imgFolder, ch.Id + e)).FirstOrDefault(File.Exists);
                if (imgFile != null)
                {
                    inner.Children.Add(new Image
                    {
                        Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(imgFile)),
                        Height = 120,
                        Stretch = System.Windows.Media.Stretch.UniformToFill,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 8)
                    });
                }

                // Name + info
                inner.Children.Add(new TextBlock
                {
                    Text = firstName + (string.IsNullOrEmpty(relation) ? "" : $"  ·  {relation}"),
                    FontSize = 13, FontWeight = FontWeights.SemiBold,
                    Foreground = System.Windows.Media.Brushes.White
                });
                if (!string.IsNullOrEmpty(age))
                    inner.Children.Add(new TextBlock
                    {
                        Text = $"{age} {Loc.T("rel.years")}",
                        FontSize = 11,
                        Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88)),
                        Margin = new Thickness(0, 2, 0, 0)
                    });

                // Attributes
                if (ch.Properties.TryGetValue("attributes", out var attrs) && attrs is Newtonsoft.Json.Linq.JObject attrsObj)
                {
                    foreach (var kv in attrsObj)
                    {
                        if (kv.Value is not Newtonsoft.Json.Linq.JObject ao) continue;
                        if (ao.Value<bool?>("hidden") == true) continue;
                        string attrName = ao.Value<string>("name") ?? kv.Key;
                        int val = ao.Value<int>("value"), min = ao.Value<int>("min"), max = ao.Value<int>("max");

                        var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
                        row.Children.Add(new TextBlock
                        {
                            Text = attrName,
                            FontSize = 10,
                            Foreground = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromRgb(0xAA, 0xAA, 0xAA)),
                            HorizontalAlignment = HorizontalAlignment.Left
                        });
                        row.Children.Add(new TextBlock
                        {
                            Text = $"{val}/{max}",
                            FontSize = 10,
                            Foreground = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66)),
                            HorizontalAlignment = HorizontalAlignment.Right
                        });
                        inner.Children.Add(row);
                        inner.Children.Add(new ProgressBar
                        {
                            Minimum = min, Maximum = max, Value = val,
                            Margin = new Thickness(0, 2, 0, 0)
                        });
                    }
                }

                card.Child = inner;
                relationshipsStack.Children.Add(card);
            }

            if (relationshipsStack.Children.Count == 0)
                relationshipsStack.Children.Add(new TextBlock
                {
                    Text = Loc.T("rel.no_contacts"),
                    FontSize = 12,
                    Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x6A, 0x6A, 0x6A)),
                    Margin = new Thickness(0, 8, 0, 0)
                });
        }

        /* WARDROBE */
        private string GetClothingState()
        {
            if (wornClothing.ContainsKey("clothes")) return "dressed";
            if (wornClothing.ContainsKey("bra") || wornClothing.ContainsKey("panties")) return "underwear";
            return "naked";
        }

        private void ShowWardrobe(bool resetState = true)
        {
            if (resetState) _wardrobeState = "main";
            mainContent.Visibility = Visibility.Hidden;
            mainMedia.Visibility   = Visibility.Hidden;
            mainImage.Visibility   = Visibility.Hidden;
            RebuildWardrobePanel();
            wardrobeOverlay.Visibility = Visibility.Visible;
        }

        private void CloseWardrobe()
        {
            wardrobeOverlay.Visibility = Visibility.Collapsed;
            mainContent.Visibility = Visibility.Visible;
            if (currentMediaSource != null)
            {
                bool isVid = VideoExtensions.Contains(Path.GetExtension(currentMediaSource.LocalPath));
                mainMedia.Visibility = isVid ? Visibility.Visible : Visibility.Collapsed;
                mainImage.Visibility = isVid ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void RebuildWardrobePanel()
        {
            wardrobeStack.Children.Clear();
            switch (_wardrobeState)
            {
                case "category": RebuildWardrobeCategory(); break;
                case "item":     RebuildWardrobeItem();     break;
                default:         RebuildWardrobeMain();     break;
            }
            UpdateWardrobeStateLabel();
        }

        private void RebuildWardrobeMain()
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Column 0 spanning all 3 rows: "clothes" (large display)
            AddWardrobeTile(grid, "clothes", row: 0, col: 0, rowSpan: 3, large: true);

            // Column 1: bra / panties / shoes stacked
            AddWardrobeTile(grid, "bra",     row: 0, col: 1);
            AddWardrobeTile(grid, "panties", row: 1, col: 1);
            AddWardrobeTile(grid, "shoes",   row: 2, col: 1);

            wardrobeStack.Children.Add(grid);
        }

        private void AddWardrobeTile(Grid grid, string subtype, int row, int col, int rowSpan = 1, bool large = false)
        {
            string label  = Loc.T("wardrobe." + subtype);
            string wornId = wornClothing.TryGetValue(subtype, out var wid) ? wid : null;
            var    def    = wornId != null ? clothingDefinitions.FirstOrDefault(c => c.Id == wornId) : null;

            var tile = new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                CornerRadius    = new CornerRadius(6),
                Margin          = new Thickness(4),
                MinHeight       = large ? 160 : 70,
                Cursor          = System.Windows.Input.Cursors.Hand,
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
                BorderThickness = new Thickness(1)
            };

            var inner = new StackPanel
            {
                Margin              = new Thickness(8),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
            inner.Children.Add(new TextBlock
            {
                Text                = label.ToUpper(),
                FontSize            = 9,
                Foreground          = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin              = new Thickness(0, 0, 0, 4)
            });

            if (def != null)
            {
                string imgPath = FindClothingImage(def);
                if (imgPath != null)
                    inner.Children.Add(new Image
                    {
                        Source  = new System.Windows.Media.Imaging.BitmapImage(new Uri(imgPath)),
                        Height  = large ? 100 : 44,
                        Stretch = System.Windows.Media.Stretch.Uniform
                    });
                else
                    inner.Children.Add(new TextBlock
                    {
                        Text                = def.Name,
                        FontSize            = large ? 12 : 10,
                        Foreground          = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextWrapping        = System.Windows.TextWrapping.Wrap,
                        TextAlignment       = System.Windows.TextAlignment.Center
                    });
            }
            else
            {
                inner.Children.Add(new TextBlock
                {
                    Text                = "—",
                    FontSize            = large ? 24 : 16,
                    Foreground          = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }

            tile.Child = inner;
            string capturedSubtype = subtype;
            tile.MouseLeftButtonUp += (_, __) =>
            {
                _wardrobeState            = "category";
                _wardrobeCategorySelected = capturedSubtype;
                RebuildWardrobePanel();
            };

            Grid.SetRow(tile, row);
            Grid.SetColumn(tile, col);
            if (rowSpan > 1) Grid.SetRowSpan(tile, rowSpan);
            grid.Children.Add(tile);
        }

        private void RebuildWardrobeCategory()
        {
            string subtype = _wardrobeCategorySelected;
            string label   = Loc.T("wardrobe." + subtype);
            string wornId  = wornClothing.TryGetValue(subtype, out var id) ? id : null;

            AddWardrobeBackButton("main");
            wardrobeStack.Children.Add(new TextBlock
            {
                Text        = label,
                FontSize    = 14, FontWeight = FontWeights.SemiBold,
                Foreground  = Brushes.White,
                Margin      = new Thickness(0, 4, 0, 12)
            });

            var items = clothingDefinitions
                .Where(c => c.Subtype == subtype && ownedClothing.Contains(c.Id))
                .ToList();

            if (items.Count == 0)
            {
                wardrobeStack.Children.Add(new TextBlock
                {
                    Text       = Loc.T("wardrobe.no_items"),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                    FontSize   = 12
                });
                return;
            }

            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var def in items)
            {
                bool   isWorn     = def.Id == wornId;
                string capturedId = def.Id;

                var tile = new Border
                {
                    Background      = new SolidColorBrush(isWorn
                        ? Color.FromRgb(0x0A, 0x30, 0x50)
                        : Color.FromRgb(0x1E, 0x1E, 0x1E)),
                    CornerRadius    = new CornerRadius(6),
                    Margin          = new Thickness(0, 0, 8, 8),
                    Width           = 100, MinHeight = 110,
                    Cursor          = System.Windows.Input.Cursors.Hand,
                    BorderBrush     = isWorn
                        ? new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4))
                        : new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
                    BorderThickness = new Thickness(2)
                };

                var inner = new StackPanel { Margin = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Center };
                string imgPath = FindClothingImage(def);
                if (imgPath != null)
                    inner.Children.Add(new Image
                    {
                        Source  = new System.Windows.Media.Imaging.BitmapImage(new Uri(imgPath)),
                        Width   = 80, Height = 80,
                        Stretch = System.Windows.Media.Stretch.Uniform
                    });
                else
                    inner.Children.Add(new Border
                    {
                        Width = 80, Height = 80,
                        Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                        CornerRadius = new CornerRadius(4),
                        Child = new TextBlock
                        {
                            Text                = def.Name,
                            FontSize            = 9,
                            Foreground          = Brushes.White,
                            TextWrapping        = System.Windows.TextWrapping.Wrap,
                            TextAlignment       = System.Windows.TextAlignment.Center,
                            VerticalAlignment   = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Margin              = new Thickness(4)
                        }
                    });

                if (isWorn)
                    inner.Children.Add(new TextBlock
                    {
                        Text                = Loc.T("wardrobe.worn"),
                        FontSize            = 8,
                        Foreground          = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin              = new Thickness(0, 3, 0, 0)
                    });

                tile.Child = inner;
                tile.MouseLeftButtonUp += (_, __) =>
                {
                    _wardrobeState       = "item";
                    _wardrobeItemSelected = capturedId;
                    RebuildWardrobePanel();
                };
                wrap.Children.Add(tile);
            }
            wardrobeStack.Children.Add(wrap);
        }

        private void RebuildWardrobeItem()
        {
            var def = clothingDefinitions.FirstOrDefault(c => c.Id == _wardrobeItemSelected);
            if (def == null) { _wardrobeState = "main"; RebuildWardrobePanel(); return; }

            bool isWorn = wornClothing.TryGetValue(def.Subtype, out var wornId) && wornId == def.Id;
            int  mcInh  = GetMcInhibition();
            bool canWear = def.Inhibition == 0 || mcInh <= def.Inhibition;

            AddWardrobeBackButton("category");

            string imgPath = FindClothingImage(def);
            if (imgPath != null)
                wardrobeStack.Children.Add(new Image
                {
                    Source              = new System.Windows.Media.Imaging.BitmapImage(new Uri(imgPath)),
                    MaxHeight           = 160,
                    Margin              = new Thickness(0, 8, 0, 8),
                    Stretch             = System.Windows.Media.Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center
                });

            wardrobeStack.Children.Add(new TextBlock
            {
                Text       = def.Name,
                FontSize   = 14, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin     = new Thickness(0, 4, 0, 2)
            });

            if (!string.IsNullOrEmpty(def.Description))
                wardrobeStack.Children.Add(new TextBlock
                {
                    Text         = def.Description,
                    FontSize     = 11,
                    Foreground   = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    TextWrapping = System.Windows.TextWrapping.Wrap,
                    Margin       = new Thickness(0, 0, 0, 6)
                });

            if (def.Tags.Count > 0)
                wardrobeStack.Children.Add(new TextBlock
                {
                    Text       = Loc.T("wardrobe.tags") + ": " + string.Join(", ", def.Tags),
                    FontSize   = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    Margin     = new Thickness(0, 0, 0, 8)
                });

            if (!canWear && !isWorn)
                wardrobeStack.Children.Add(new TextBlock
                {
                    Text       = Loc.T("wardrobe.inhib_hint", def.Inhibition),
                    FontSize   = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)),
                    Margin     = new Thickness(0, 0, 0, 4)
                });

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };

            if (isWorn)
            {
                var undressBtn = new Button
                {
                    Content = Loc.T("wardrobe.unwear"),
                    Height  = 32, Padding = new Thickness(12, 0, 12, 0),
                    Margin  = new Thickness(0, 0, 8, 0)
                };
                string capturedSubtype = def.Subtype;
                undressBtn.Click += (_, __) =>
                {
                    wornClothing.Remove(capturedSubtype);
                    _wardrobeState = "category";
                    RebuildWardrobePanel();
                };
                btnRow.Children.Add(undressBtn);
            }
            else
            {
                var dressBtn = new Button
                {
                    Content   = Loc.T("wardrobe.wear"),
                    Height    = 32, Padding = new Thickness(12, 0, 12, 0),
                    Margin    = new Thickness(0, 0, 8, 0),
                    IsEnabled = canWear
                };
                string capturedId = def.Id, capturedSubtype = def.Subtype;
                dressBtn.Click += (_, __) =>
                {
                    wornClothing[capturedSubtype] = capturedId;
                    _wardrobeState = "category";
                    RebuildWardrobePanel();
                };
                btnRow.Children.Add(dressBtn);
            }

            var discardBtn = new Button
            {
                Content    = Loc.T("wardrobe.discard"),
                Height     = 32, Padding = new Thickness(12, 0, 12, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B))
            };
            string capturedDefId = def.Id, capturedDefSubtype = def.Subtype, capturedDefName = def.Name;
            discardBtn.Click += (_, __) =>
            {
                var result = MessageBox.Show(
                    Loc.T("confirm.discard", capturedDefName),
                    Loc.T("confirm.discard.title"), MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;
                if (wornClothing.TryGetValue(capturedDefSubtype, out var w) && w == capturedDefId)
                    wornClothing.Remove(capturedDefSubtype);
                ownedClothing.Remove(capturedDefId);
                _wardrobeState = "category";
                RebuildWardrobePanel();
                UpdateInventoryPanel();
            };
            btnRow.Children.Add(discardBtn);
            wardrobeStack.Children.Add(btnRow);
        }

        private void AddWardrobeBackButton(string targetState)
        {
            var btn = new Button
            {
                Content             = Loc.T("wardrobe.back"),
                Height              = 28, Padding = new Thickness(10, 0, 10, 0),
                Margin              = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background          = Brushes.Transparent,
                BorderBrush         = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44))
            };
            string captured = targetState;
            btn.Click += (_, __) => { _wardrobeState = captured; RebuildWardrobePanel(); };
            wardrobeStack.Children.Add(btn);
        }

        private void UpdateWardrobeStateLabel()
        {
            string state = GetClothingState();
            string stateText = state switch
            {
                "naked"     => Loc.T("wardrobe.naked"),
                "underwear" => Loc.T("wardrobe.underwear"),
                _           => Loc.T("wardrobe.dressed")
            };
            wardrobeStateLabel.Text = Loc.T("wardrobe.status") + ": " + stateText;
        }

        public void wardrobeOverlay_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => CloseWardrobe();

        public void wardrobeCard_StopPropagation(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => e.Handled = true;

        public void wardrobeClose_Click(object sender, RoutedEventArgs e)
            => CloseWardrobe();

        /* PHONE APPS */
        public void phoneApp_Click(object sender, RoutedEventArgs e)
        {
            string app = (string)((Button)sender).Tag;
            OpenPhoneApp(app);
        }

        private void OpenPhoneApp(string app)
        {
            // Reset all phone sub-panels
            phoneContentBrowser.Visibility     = Visibility.Collapsed;
            phoneStatusPanel.Visibility        = Visibility.Collapsed;
            phoneCameraPanel.Visibility        = Visibility.Collapsed;
            phoneMediaGalleryScroll.Visibility = Visibility.Collapsed;
            phoneInstagramPanel.Visibility     = Visibility.Collapsed;
            phoneActionStack.Children.Clear();
            // Hide HWND-based controls before showing overlay
            mainContent.Visibility = Visibility.Hidden;
            mainMedia.Visibility   = Visibility.Hidden;
            mainImage.Visibility   = Visibility.Hidden;
            phoneAppOverlay.Visibility = Visibility.Visible;

            switch (app)
            {
                case "telefon":
                    phoneAppTitle.Text = Loc.T("phone.telefon");
                    RenderPhoneAcvn("phone_call", "start");
                    break;
                case "nachrichten":
                    phoneAppTitle.Text = Loc.T("phone.nachrichten");
                    RenderPhoneAcvn("phone_sms", "start");
                    break;
                case "kamera":
                    phoneAppTitle.Text = Loc.T("phone.kamera");
                    OpenPhoneCamera();
                    break;
                case "medien":
                    phoneAppTitle.Text = Loc.T("phone.medien");
                    OpenPhoneMedia();
                    break;
                case "instagram":
                    phoneAppTitle.Text = Loc.T("phone.instagram");
                    phoneInstagramPanel.Visibility = Visibility.Visible;
                    AddPhoneCloseButton();
                    break;
            }
        }

        private void RenderPhoneAcvn(string room, string action)
        {
            phoneCurrentRoom   = room;
            phoneCurrentAction = action;

            string filePath = Path.Combine(roomsPath, clearPath(room) + ".acvn");
            if (!File.Exists(filePath)) { phoneContentBrowser.NavigateToString("<p style='color:#888'>Datei nicht gefunden.</p>"); phoneContentBrowser.Visibility = Visibility.Visible; return; }

            string fileContent = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            string pattern = @"#begin\s+(.*?)\s+(.*?)#end";
            var matches = System.Text.RegularExpressions.Regex.Matches(fileContent, pattern, System.Text.RegularExpressions.RegexOptions.Singleline);

            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                if (m.Groups[1].Value != action) continue;
                string rendered = RenderTemplate(m.Groups[2].Value);
                ParsePhoneRooms(rendered);
                AddPhoneCloseButton();
                ParsePhoneContent(rendered);
                break;
            }
        }

        private void ParsePhoneContent(string content)
        {
            string cssPath = Path.Combine(storyPath, "style.css");
            string css = File.Exists(cssPath) ? File.ReadAllText(cssPath, System.Text.Encoding.UTF8) : "";
            string html = $"<meta charset=\"utf-8\"><style>body{{background:#111;color:#ddd;font-family:'Segoe UI';}}{css}</style>";
            html += System.Text.RegularExpressions.Regex.Replace(content, @"#begin.*\n|#end\n*", string.Empty);
            html  = System.Text.RegularExpressions.Regex.Replace(html, @"\[\[.*?\]\]", string.Empty);
            phoneContentBrowser.NavigateToString(html);
            phoneContentBrowser.Visibility = Visibility.Visible;
        }

        private void ParsePhoneRooms(string content)
        {
            phoneActionStack.Children.Clear();
            var matches = System.Text.RegularExpressions.Regex.Matches(content, @"\[\[(.*?)\]\]");
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                var cmd = m.Groups[1].Value.Split(',');
                if (cmd.Length < 2) continue;
                string label  = cmd[0].Trim();
                string room   = cmd[1].Trim();
                string action = cmd.Length > 2 ? cmd[2].Trim() : "start";
                string capturedRoom = room, capturedAction = action;

                var btn = new Button
                {
                    Content = label,
                    Margin = new Thickness(0, 0, 6, 0),
                    Height = 32
                };
                btn.Click += (_, __) => ExecutePhoneCommand(capturedRoom, capturedAction);
                phoneActionStack.Children.Add(btn);
            }
        }

        private void ExecutePhoneCommand(string room, string action)
        {
            // Special: calling a character
            if (room == "phone_call" && action != "start")
            {
                StartPhoneCall(action);
                return;
            }
            // Special: SMS to a character
            if (room == "phone_sms" && action != "start")
            {
                ShowPhoneSms(action);
                return;
            }
            RenderPhoneAcvn(room, action);
        }

        private void StartPhoneCall(string charId)
        {
            var ch = GetCharacter(charId);
            string name = ch?.Properties.TryGetValue("firstname", out var fn) == true ? fn.ToString() : charId;

            phoneContentBrowser.Visibility = Visibility.Collapsed;
            phoneActionStack.Children.Clear();
            phoneStatusName.Text  = name;
            phoneStatusText.Text  = Loc.T("phone.connecting");
            phoneStatusPanel.Visibility = Visibility.Visible;

            // Character portrait
            string imgFolder = Path.Combine(imagesPath, "chars");
            string[] exts = { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
            string imgFile = exts.Select(ex => Path.Combine(imgFolder, charId + ex)).FirstOrDefault(File.Exists);
            phoneCallerImage.Source = imgFile != null
                ? new System.Windows.Media.Imaging.BitmapImage(new Uri(imgFile))
                : null;
            phoneCallerImage.Visibility = imgFile != null ? Visibility.Visible : Visibility.Collapsed;

            phoneCallTimer?.Stop();
            phoneCallTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            phoneCallTimer.Tick += (_, __) =>
            {
                phoneCallTimer.Stop();
                phoneStatusText.Text = $"{name} {Loc.T("phone.unavailable")}";
                var okBtn = new Button { Content = "OK", Margin = new Thickness(0, 0, 6, 0), Height = 32 };
                okBtn.Click += (__, ___) => { RenderPhoneAcvn("phone_call", "start"); };
                phoneActionStack.Children.Clear();
                phoneActionStack.Children.Add(okBtn);
                AddPhoneCloseButton();
            };
            phoneCallTimer.Start();
        }

        private void ShowPhoneSms(string charId)
        {
            var ch = GetCharacter(charId);
            string name = ch?.Properties.TryGetValue("firstname", out var fn) == true ? fn.ToString() : charId;

            phoneContentBrowser.Visibility = Visibility.Collapsed;
            phoneStatusName.Text  = name;
            phoneStatusText.Text  = Loc.T("phone.no_sms");
            phoneStatusPanel.Visibility = Visibility.Visible;

            var okBtn = new Button { Content = "OK", Margin = new Thickness(0, 0, 6, 0), Height = 32 };
            okBtn.Click += (__, ___) => RenderPhoneAcvn("phone_sms", "start");
            phoneActionStack.Children.Clear();
            phoneActionStack.Children.Add(okBtn);
            AddPhoneCloseButton();
        }

        private void OpenPhoneCamera()
        {
            phoneCameraPreview.Source     = null;
            phoneCameraPreview.Visibility = Visibility.Collapsed;
            phoneCameraHint.Visibility    = Visibility.Collapsed;

            if (currentMediaSource == null)
            {
                phoneCameraHintIcon.Text = "";
                phoneCameraHintText.Text = Loc.T("phone.no_image");
                phoneCameraHint.Visibility = Visibility.Visible;
            }
            else if (VideoExtensions.Contains(Path.GetExtension(currentMediaSource.LocalPath)))
            {
                // Render video through MediaPlayer + VideoDrawing (no HWND — works inside WPF overlays)
                _cameraPlayer = new MediaPlayer();
                var drawing = new System.Windows.Media.VideoDrawing
                {
                    Player = _cameraPlayer,
                    Rect   = new System.Windows.Rect(0, 0, 1920, 1080)
                };
                _cameraPlayer.MediaOpened += (s, _) =>
                {
                    var p = (MediaPlayer)s;
                    if (p.NaturalVideoWidth > 0)
                        drawing.Rect = new System.Windows.Rect(0, 0, p.NaturalVideoWidth, p.NaturalVideoHeight);
                    p.Play();
                };
                _cameraPlayer.Open(currentMediaSource);
                phoneCameraPreview.Source     = new System.Windows.Media.DrawingImage(drawing);
                phoneCameraPreview.Visibility = Visibility.Visible;
            }
            else
            {
                phoneCameraPreview.Source     = new System.Windows.Media.Imaging.BitmapImage(currentMediaSource);
                phoneCameraPreview.Visibility = Visibility.Visible;
            }
            phoneCameraPanel.Visibility = Visibility.Visible;
            AddPhoneCloseButton();
        }

        private void StopCameraPlayer()
        {
            if (_cameraPlayer == null) return;
            _cameraPlayer.Stop();
            _cameraPlayer.Close();
            _cameraPlayer = null;
        }

        public void phoneCaptureBtn_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (currentMediaSource == null) return;
            string path = currentMediaSource.LocalPath;
            if (!savedPhotos.Contains(path))
                savedPhotos.Add(path);
            ClosePhoneApp();
        }

        private System.Windows.Media.Imaging.BitmapSource GetVideoThumbnail(string path)
        {
            try
            {
                using var shellFile = Microsoft.WindowsAPICodePack.Shell.ShellFile.FromFilePath(path);
                shellFile.Thumbnail.CurrentSize = new System.Windows.Size(220, 220);
                return shellFile.Thumbnail.BitmapSource;
            }
            catch { return null; }
        }

        private void OpenPhoneMedia()
        {
            RebuildMediaGallery();
            phoneMediaGalleryScroll.Visibility = Visibility.Visible;
            AddPhoneCloseButton();
        }

        private void RebuildMediaGallery()
        {
            phoneMediaGallery.Children.Clear();
            if (savedPhotos.Count == 0)
            {
                phoneMediaGallery.Children.Add(new TextBlock
                {
                    Text = Loc.T("phone.no_media"),
                    Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55)),
                    FontSize = 13,
                    Margin = new Thickness(8)
                });
                return;
            }

            foreach (string mediaPath in savedPhotos.ToList())
            {
                string capturedPath = mediaPath;
                bool isVideo = VideoExtensions.Contains(Path.GetExtension(capturedPath));

                var container = new Grid
                {
                    Width = 110, Height = 110,
                    Margin = new Thickness(4),
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                if (isVideo)
                {
                    // Try to get a system-generated thumbnail via Shell
                    var thumb = GetVideoThumbnail(capturedPath);
                    if (thumb != null)
                    {
                        // Thumbnail image + small play-icon overlay
                        container.Children.Add(new Image
                        {
                            Source  = thumb,
                            Stretch = System.Windows.Media.Stretch.UniformToFill,
                            Width = 110, Height = 110
                        });
                        container.Children.Add(new TextBlock
                        {
                            Text = "", // Play icon (Segoe MDL2)
                            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                            FontSize = 22,
                            Foreground = System.Windows.Media.Brushes.White,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment   = VerticalAlignment.Center,
                            Effect = new System.Windows.Media.Effects.DropShadowEffect
                            {
                                Color = System.Windows.Media.Colors.Black,
                                BlurRadius = 6, ShadowDepth = 0, Opacity = 0.8
                            }
                        });
                    }
                    else
                    {
                        // Fallback: dark tile with icon
                        container.Children.Add(new Border
                        {
                            Background = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromRgb(0x18, 0x18, 0x18))
                        });
                        container.Children.Add(new TextBlock
                        {
                            Text = "",
                            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                            FontSize = 30, Foreground = System.Windows.Media.Brushes.White,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment   = VerticalAlignment.Center
                        });
                    }
                }
                else
                {
                    container.Children.Add(new Image
                    {
                        Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(capturedPath)),
                        Stretch = System.Windows.Media.Stretch.UniformToFill,
                        Width = 110, Height = 110
                    });
                }

                // Click anywhere on tile (except delete button) → open modal
                container.MouseLeftButtonUp += (_, me) =>
                {
                    if (me.Handled) return;
                    ShowMediaModal(new Uri(capturedPath));
                };

                // Delete button
                var delBtn = new Button
                {
                    Content = "✕",
                    Width = 22, Height = 22,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment   = VerticalAlignment.Top,
                    Margin      = new Thickness(0, 2, 2, 0),
                    Background  = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(0xCC, 0x20, 0x20, 0x20)),
                    Foreground  = System.Windows.Media.Brushes.White,
                    BorderBrush = System.Windows.Media.Brushes.Transparent,
                    FontSize = 10, Padding = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                delBtn.Click += (_, __) => { savedPhotos.Remove(capturedPath); RebuildMediaGallery(); };

                container.Children.Add(delBtn);
                phoneMediaGallery.Children.Add(container);
            }
        }

        private void AddPhoneCloseButton()
        {
            if (phoneActionStack.Children.Count > 0)
                phoneActionStack.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Width = 1, Height = 20,
                    Fill = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x3A, 0x3A, 0x3A)),
                    Margin = new Thickness(4, 0, 0, 0)
                });

            var btn = new Button
            {
                Height = 32,
                Margin = new Thickness(0, 0, 6, 0),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x6B))
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock
            {
                Text = "",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            });
            sp.Children.Add(new TextBlock { Text = Loc.T("phone.close"), VerticalAlignment = VerticalAlignment.Center });
            btn.Content = sp;
            btn.Click += (_, __) => ClosePhoneApp();
            phoneActionStack.Children.Add(btn);
        }

        private void ClosePhoneApp()
        {
            StopCameraPlayer();
            phoneCallTimer?.Stop();
            phoneAppOverlay.Visibility = Visibility.Collapsed;
            // Restore HWND-based controls
            mainContent.Visibility = Visibility.Visible;
            if (currentMediaSource != null)
            {
                bool isVid = VideoExtensions.Contains(Path.GetExtension(currentMediaSource.LocalPath));
                mainMedia.Visibility = isVid ? Visibility.Visible : Visibility.Collapsed;
                mainImage.Visibility = isVid ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        public void phoneAppBackBtn_Click(object sender, RoutedEventArgs e)
            => ClosePhoneApp();

        /* LOCALIZATION */
        private void ApplyLocalization()
        {
            // Tab bar
            tbTabStatus.Text    = Loc.T("tab.status");
            tbTabPhone.Text     = Loc.T("tab.phone");
            tbTabInventory.Text = Loc.T("tab.inventory");
            tbTabJournal.Text   = Loc.T("tab.journal");
            tbTabContacts.Text  = Loc.T("tab.contacts");
            tbTabSettings.Text  = Loc.T("tab.settings");

            // Toolbar
            tbQuickSave.Text = Loc.T("btn.quicksave");
            tbQuickLoad.Text = Loc.T("btn.quickload");
            tbSave.Text      = Loc.T("btn.save");
            tbLoad.Text      = Loc.T("btn.load");

            // Settings panel
            tbSettingsTitle.Text            = Loc.T("settings.title");
            debugToggle.Content             = Loc.T("settings.debug");
            videoAutoplayToggle.Content     = Loc.T("settings.autoplay");
            tbVolLabel.Text                 = Loc.T("settings.volume");
            tbLangLabel.Text                = Loc.T("settings.language");
            tbSettingsRestart.Text          = Loc.T("settings.restart");
            tbSettingsBack.Text             = Loc.T("settings.backscene");

            // Overlays
            tbWardrobeTitle.Text  = Loc.T("wardrobe.title");
            tbMediaCloseHint.Text = Loc.T("media.close_hint");

            // Phone app labels
            tbPhoneTelefon.Text     = Loc.T("phone.telefon");
            tbPhoneNachrichten.Text = Loc.T("phone.nachrichten");
            tbPhoneKamera.Text      = Loc.T("phone.kamera");
            tbPhoneMedien.Text      = Loc.T("phone.medien");
            tbPhoneInstagram.Text   = Loc.T("phone.instagram");

            // Setup button
            setupContinueBtn.Content = Loc.T("setup.continue");

            // Rebuild dynamic panels so their strings update
            UpdateInventoryPanel();
            UpdateJournalPanel();
            if (setupOverlay.Visibility != Visibility.Visible && wardrobeOverlay.Visibility == Visibility.Visible)
                RebuildWardrobePanel();
        }

        private void PopulateLangDropdown()
        {
            langDropdown.SelectionChanged -= langDropdown_Changed;
            langDropdown.Items.Clear();
            var keys = Loc.LanguageNames.Keys.ToList();
            foreach (var key in keys)
                langDropdown.Items.Add(new ComboBoxItem { Content = Loc.LanguageNames[key], Tag = key });
            int idx = keys.IndexOf(Loc.CurrentLanguage);
            langDropdown.SelectedIndex = idx >= 0 ? idx : 0;
            langDropdown.SelectionChanged += langDropdown_Changed;
        }

        public void langDropdown_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_settingsReady) return;
            if (langDropdown.SelectedItem is ComboBoxItem item && item.Tag is string lang)
            {
                Loc.SetLanguage(lang);
                SaveAppSettings();
            }
        }

        /* SETUP SCREEN */
        private void ShowSetupScreen()
        {
            setupStack.Children.Clear();
            _setupFields.Clear();

            setupStack.Children.Add(new TextBlock
            {
                Text       = Loc.T("setup.title"),
                FontSize   = 20, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin     = new Thickness(0, 0, 0, 4)
            });
            setupStack.Children.Add(new TextBlock
            {
                Text       = Loc.T("setup.subtitle"),
                FontSize   = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                Margin     = new Thickness(0, 0, 0, 20)
            });

            // MC
            AddSetupSectionHeader(Loc.T("setup.mc"));
            var mc = GetCharacter("mc");
            string Get(Character ch, string key, string fallback = "") =>
                ch?.Properties.TryGetValue(key, out var v) == true ? v.ToString() : fallback;

            AddSetupField("mc.firstname", Loc.T("setup.firstname"),  Get(mc, "firstname", "Trixie"));
            AddSetupField("mc.lastname",  Loc.T("setup.lastname"),   Get(mc, "lastname",  "Smith"));
            AddSetupField("mc.nickname",  Loc.T("setup.nickname"),   Get(mc, "nickname",  ""));
            AddSetupField("mc.age",       Loc.T("setup.age"),        Get(mc, "age",       "18"));

            // NPCs
            var npcKeys = new[]
            {
                ("brother", Loc.T("setup.brother")),
                ("mother",  Loc.T("setup.mother")),
                ("father",  Loc.T("setup.father"))
            };
            foreach (var (npcId, sectionLabel) in npcKeys)
            {
                var ch = GetCharacter(npcId);
                AddSetupSectionHeader(sectionLabel);
                AddSetupField(npcId + ".firstname",        Loc.T("setup.firstname"),        Get(ch, "firstname"));
                AddSetupField(npcId + ".age",              Loc.T("setup.age"),              Get(ch, "age"));
                AddSetupField(npcId + ".relation",         Loc.T("setup.relation"),         Get(ch, "relation"));
                AddSetupField(npcId + ".relation_reverse", Loc.T("setup.relation_reverse"), Get(ch, "relation_reverse"));
            }

            setupContinueBtn.Content = Loc.T("setup.continue");
            mainContent.Visibility   = Visibility.Hidden;
            mainMedia.Visibility     = Visibility.Hidden;
            mainImage.Visibility     = Visibility.Hidden;
            setupOverlay.Visibility  = Visibility.Visible;
        }

        private void AddSetupSectionHeader(string label)
        {
            setupStack.Children.Add(new TextBlock
            {
                Text       = label,
                FontSize   = 13, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin     = new Thickness(0, 16, 0, 8)
            });
        }

        private void AddSetupField(string key, string label, string defaultValue)
        {
            setupStack.Children.Add(new TextBlock
            {
                Text       = label,
                FontSize   = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                Margin     = new Thickness(0, 0, 0, 3)
            });
            var tb = new TextBox { Text = defaultValue, Margin = new Thickness(0, 0, 0, 10) };
            _setupFields[key] = tb;
            setupStack.Children.Add(tb);
        }

        public void setupContinueBtn_Click(object sender, RoutedEventArgs e)
        {
            string Field(string key) =>
                _setupFields.TryGetValue(key, out var tb) ? tb.Text.Trim() : string.Empty;

            // MC
            var mc = GetCharacter("mc");
            if (mc != null)
            {
                string fn = Field("mc.firstname");
                if (!string.IsNullOrEmpty(fn)) mc.Properties["firstname"] = fn;
                mc.Properties["lastname"] = Field("mc.lastname");
                string nick = Field("mc.nickname");
                if (string.IsNullOrEmpty(nick))
                    mc.Properties.Remove("nickname");
                else
                    mc.Properties["nickname"] = nick;
                if (int.TryParse(Field("mc.age"), out int mcAge))
                    mc.Properties["age"] = mcAge;
            }

            // NPCs
            foreach (string npcId in new[] { "brother", "mother", "father" })
            {
                var ch = GetCharacter(npcId);
                if (ch == null) continue;
                string fn = Field(npcId + ".firstname");
                if (!string.IsNullOrEmpty(fn)) ch.Properties["firstname"] = fn;
                if (int.TryParse(Field(npcId + ".age"), out int age))
                    ch.Properties["age"] = age;
                ch.Properties["relation"]         = Field(npcId + ".relation");
                ch.Properties["relation_reverse"] = Field(npcId + ".relation_reverse");
            }

            setupOverlay.Visibility = Visibility.Collapsed;
            mainContent.Visibility  = Visibility.Visible;
            SetIntroLayout(false);

            currentRoom   = _config.TryGetValue("start_room",   out var sr) ? sr : "home_room";
            currentAction = _config.TryGetValue("start_action", out var sa) ? sa : "start";
            navigationHistory.Clear();
            UpdateBackButton();
            UpdateTemplateVariables();
            InitContent();
        }

        /* SCRIBAN FUNCTIONS
           Scriban auto-discovers *static* public methods on ScriptObject subclasses
           and renames PascalCase → snake_case at lookup time:
             TimeStr      → time_str
             AdvanceTime  → advance_time
             AttrChange   → attr_change
             GetCharacter → get_character
           State is accessed via the _instance static back-reference. */
        private class GameFunctions : ScriptObject
        {
            public static string TimeStr(string format)
                => _instance.gameTime.ToString(format);

            public static string AdvanceTime(int minutes)
            {
                _instance.gameTime = _instance.gameTime.AddMinutes(minutes);
                return string.Empty;
            }

            public static string AttrChange(string charId, string attrName, int delta)
            {
                var character = _instance.GetCharacter(charId);
                if (character?.Properties?.TryGetValue("attributes", out var attrs) == true
                    && attrs is JObject attrsObj
                    && attrsObj.TryGetValue(attrName, out var attrToken)
                    && attrToken is JObject attrObj)
                {
                    int current = attrObj.Value<int>("value");
                    int min     = attrObj.Value<int>("min");
                    int max     = attrObj.Value<int>("max");
                    attrObj["value"] = Math.Clamp(current + delta, min, max);
                }
                return string.Empty;
            }

            public static Character GetCharacter(string id)
                => _instance.GetCharacter(id);

            // — Attribute —

            public static int GetAttr(string charId, string attrName)
            {
                var ch = _instance.GetCharacter(charId);
                if (ch?.Properties?.TryGetValue("attributes", out var attrs) == true
                    && attrs is JObject attrsObj
                    && attrsObj.TryGetValue(attrName, out var tok)
                    && tok is JObject attrObj)
                    return attrObj.Value<int>("value");
                return 0;
            }

            public static string SetAttr(string charId, string attrName, int value)
            {
                var ch = _instance.GetCharacter(charId);
                if (ch?.Properties?.TryGetValue("attributes", out var attrs) == true
                    && attrs is JObject attrsObj
                    && attrsObj.TryGetValue(attrName, out var tok)
                    && tok is JObject attrObj)
                {
                    int min = attrObj.Value<int>("min");
                    int max = attrObj.Value<int>("max");
                    attrObj["value"] = Math.Clamp(value, min, max);
                }
                return string.Empty;
            }

            // — Inventory —

            public static string AddItem(string itemId)
            {
                var inv = _instance.inventory;
                inv[itemId] = (inv.ContainsKey(itemId) ? inv[itemId] : 0) + 1;
                _instance.UpdateInventoryPanel();
                return string.Empty;
            }

            public static string RemoveItem(string itemId)
            {
                var inv = _instance.inventory;
                if (inv.ContainsKey(itemId))
                {
                    inv[itemId]--;
                    if (inv[itemId] <= 0) inv.Remove(itemId);
                    _instance.UpdateInventoryPanel();
                }
                return string.Empty;
            }

            public static bool HasItem(string itemId)
                => _instance.inventory.TryGetValue(itemId, out int c) && c > 0;

            public static int ItemCount(string itemId)
                => _instance.inventory.TryGetValue(itemId, out int c) ? c : 0;

            // — Quests —

            public static string StartQuest(string questId)
            {
                _instance.questProgress[questId] = 0;
                _instance.UpdateJournalPanel();
                return string.Empty;
            }

            public static string AdvanceQuest(string questId)
            {
                _instance.questProgress[questId] =
                    (_instance.questProgress.TryGetValue(questId, out int s) ? s : 0) + 1;
                _instance.UpdateJournalPanel();
                return string.Empty;
            }

            public static int QuestStep(string questId)
                => _instance.questProgress.TryGetValue(questId, out int s) ? s : -1;

            public static bool QuestActive(string questId)
            {
                if (!_instance.questProgress.TryGetValue(questId, out int step) || step < 0) return false;
                var def = _instance.questDefinitions.FirstOrDefault(q => q.Id == questId);
                return def != null && step < def.Steps.Count;
            }

            public static bool QuestDone(string questId)
            {
                if (!_instance.questProgress.TryGetValue(questId, out int step)) return false;
                var def = _instance.questDefinitions.FirstOrDefault(q => q.Id == questId);
                return def != null && step >= def.Steps.Count;
            }

            // — Schedules —

            public static string CharLocation(string charId)
                => _instance.GetCharCurrentLocation(charId) ?? string.Empty;

            public static bool CharAt(string charId, string location)
                => _instance.GetCharCurrentLocation(charId) == location;

            public static string CharActivity(string charId)
                => _instance.GetCharCurrentActivity(charId) ?? string.Empty;

            // — Randomness —

            public static int RandomInt(int min, int max)
                => new Random().Next(min, max + 1);

            // — Clothing —

            public static string ClothingState()
                => _instance.GetClothingState();

            public static bool IsWearing(string subtype)
                => _instance.wornClothing.ContainsKey(subtype);

            public static string WearingItem(string subtype)
                => _instance.wornClothing.TryGetValue(subtype, out var id) ? id : string.Empty;

            public static bool WearingHasTag(string subtype, string tag)
            {
                if (!_instance.wornClothing.TryGetValue(subtype, out var itemId)) return false;
                var def = _instance.clothingDefinitions.FirstOrDefault(c => c.Id == itemId);
                return def?.Tags?.Contains(tag, StringComparer.OrdinalIgnoreCase) ?? false;
            }

            public static bool AnyClothingHasTag(string tag)
            {
                return _instance.wornClothing.Values.Any(itemId =>
                {
                    var def = _instance.clothingDefinitions.FirstOrDefault(c => c.Id == itemId);
                    return def?.Tags?.Contains(tag, StringComparer.OrdinalIgnoreCase) ?? false;
                });
            }

            public static string QuestObjective(string questId)
            {
                if (!_instance.questProgress.TryGetValue(questId, out int step) || step < 0)
                    return string.Empty;
                var def = _instance.questDefinitions.FirstOrDefault(q => q.Id == questId);
                if (def == null || step >= def.Steps.Count) return string.Empty;
                return def.Steps[step].Description;
            }

            // Embeds a random media file from images/<path> as inline HTML.
            // Walks up the folder hierarchy if no files are found (same logic as ShowRandomMedia).
            // Videos are shown as a thumbnail image (IE WebBrowser has no HTML5 video).
            public static string InlineMedia(string relativePath)
            {
                string search = relativePath.Trim('/').Replace('\\', '/');
                while (true)
                {
                    if (!string.IsNullOrEmpty(search))
                    {
                        string dir = Path.GetFullPath(Path.Combine(_instance.imagesPath, search));
                        if (Directory.Exists(dir))
                        {
                            var files = Directory.GetFiles(dir, "*.*");
                            if (files.Length > 0)
                            {
                                string file = files[new Random().Next(files.Length)];
                                return BuildInlineMediaTag(file);
                            }
                        }
                    }
                    int lastSlash = search.LastIndexOf('/');
                    if (lastSlash < 0) break;
                    search = search[..lastSlash];
                }
                return string.Empty;
            }

            private static string BuildInlineMediaTag(string file)
            {
                const string style = "max-width:100%;display:block;margin:8px auto;border-radius:6px;";
                bool isVideo = VideoExtensions.Contains(Path.GetExtension(file).ToLower());

                if (!isVideo)
                    return $"<img src=\"{FileUri(file)}\" style=\"{style}\">";

                // For video: extract thumbnail via Shell, save as temp PNG, embed as <img>
                var thumb = _instance.GetVideoThumbnail(file);
                if (thumb == null) return string.Empty;

                string tmpFile = Path.Combine(Path.GetTempPath(),
                    $"acvn_thumb_{Math.Abs(file.GetHashCode())}.png");
                if (!File.Exists(tmpFile))
                {
                    using var fs = File.Create(tmpFile);
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(thumb));
                    enc.Save(fs);
                }
                return $"<img src=\"{FileUri(tmpFile)}\" style=\"{style}\">";
            }

            private static string FileUri(string path)
                => "file:///" + path.Replace('\\', '/');

            // — Daily variables —

            // Registers a variable as daily (reset on sleep) and initialises it if not yet set.
            public static string RegisterDaily(string name, object defaultValue)
            {
                _instance._dailyDefaults[name] = defaultValue;
                if (_instance.gameVars[name] == null)
                    _instance.gameVars[name] = defaultValue;
                return string.Empty;
            }

            // Resets all registered daily variables to their default values.
            public static string ResetDaily()
            {
                foreach (var kv in _instance._dailyDefaults)
                    _instance.gameVars[kv.Key] = kv.Value;
                return string.Empty;
            }
        }
    }
}
