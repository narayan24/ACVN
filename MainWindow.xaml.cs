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
        private string storiesBasePath;
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
        private string _currentTheme = "System";
        private List<ModDefinition> _mods    = new List<ModDefinition>();
        private string              modsPath;
        private IEnumerable<ModDefinition> ActiveMods => _mods.Where(m => m.Enabled);

        private static string SettingsFilePath => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        private class AppSettings
        {
            public double Volume { get; set; } = 80;
            public bool VideoAutoplay { get; set; } = true;
            public string Language { get; set; } = "de";
            public bool ShowHiddenAttributes { get; set; } = false;
            public bool DebugEnabled { get; set; } = false;
            public string Theme { get; set; } = "System";
            public Dictionary<string, bool> ModStates { get; set; } = new();
            public Dictionary<string, Dictionary<string, string>> OutfitPresets { get; set; } = new();
        }

        private Dictionary<string, string> _config = new Dictionary<string, string>();
        private Dictionary<string, bool> _journalExpanded = new Dictionary<string, bool>();
        private Dictionary<string, bool> _inventoryCollapsed = new Dictionary<string, bool>();
        private Dictionary<string, object> _dailyDefaults = new Dictionary<string, object>();

        private List<ClothingDefinition> clothingDefinitions = new List<ClothingDefinition>();
        private HashSet<string> ownedClothing = new HashSet<string>();
        private string _wardrobeState = "main";
        private string _wardrobeCategorySelected;
        private string _wardrobeItemSelected;
        private bool   _wardrobeReadOnly = false;

        private static readonly string[] ClothingSubtypes = { "bra", "panties", "clothes", "shoes" };

        private Dictionary<string, Func<string>> _setupFields = new Dictionary<string, Func<string>>();
        private int _setupStep = 0; // 0 = MC appearance editor, 1 = NPC character setup
        private List<string> _pendingQuestNotifications = new List<string>();

        /// <summary>Look up a themed SolidColorBrush from Application.Resources.</summary>
        private static System.Windows.Media.SolidColorBrush ThemeBrush(string key)
            => Application.Current.TryFindResource(key) as System.Windows.Media.SolidColorBrush
               ?? System.Windows.Media.Brushes.Magenta;

        private static System.Windows.Media.Color ThemeColor(string key)
            => ThemeBrush(key)?.Color ?? System.Windows.Media.Colors.Magenta;

        public MainWindow()
        {
            _instance = this;
            InitializeComponent();

            string rootPath = AppDomain.CurrentDomain.BaseDirectory;
            Directory.SetCurrentDirectory(rootPath);

            storiesBasePath = Path.Combine(rootPath, "../../../story/");
            if (!Directory.Exists(storiesBasePath))
                storiesBasePath = Path.Combine(rootPath, "story/");
            if (!Directory.Exists(storiesBasePath))
            {
                MessageBox.Show("The folder 'story' wasn't found. Not able to start the game!");
                return;
            }

            storyPath = PickStoryPackage(storiesBasePath);
            if (storyPath == null) return;

            roomsPath = Path.Combine(storyPath, "rooms");
            imagesPath = Path.Combine(storyPath, "images");
            saveGamePath = Path.Combine(rootPath, "savegames");
            if (!Directory.Exists(saveGamePath))
            {
                Directory.CreateDirectory(saveGamePath);
            }
            logPath = Path.Combine(rootPath, "game.log");
            CheckFolder(roomsPath);
            CheckFolder(imagesPath);

            currentRoom = "intro";
            currentAction = "start";

            LoadConfig();
            LoadMods();
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
                {
                    string key = prop.Key.ToLower();
                    string val = prop.Value.ToString();
                    // Preserve display fields as-is; lowercase comparison fields
                    _config[key] = key is "name" or "version" or "language" ? val : val.ToLower();
                }
            }
            catch { }
        }

        // ── Mod loading ─────────────────────────────────────────────────────────

        private void LoadMods()
        {
            _mods.Clear();
            modsPath = Path.Combine(storyPath, "mods");
            if (!Directory.Exists(modsPath)) return;

            var modStates = ReadAppSettings().ModStates ?? new Dictionary<string, bool>();

            foreach (var dir in Directory.GetDirectories(modsPath).OrderBy(d => d))
            {
                string modJsonPath = Path.Combine(dir, "mod.json");
                if (!File.Exists(modJsonPath)) continue;
                try
                {
                    dynamic cfg = JsonConvert.DeserializeObject<dynamic>(
                        File.ReadAllText(modJsonPath, System.Text.Encoding.UTF8));
                    string modId = System.IO.Path.GetFileName(dir);
                    bool defaultEnabled = true;
                    _mods.Add(new ModDefinition
                    {
                        Path        = dir,
                        Id          = modId,
                        Name        = cfg?.name?.ToString() ?? modId,
                        Version     = cfg?.version?.ToString() ?? string.Empty,
                        Author      = cfg?.author?.ToString() ?? string.Empty,
                        Description = cfg?.description?.ToString() ?? string.Empty,
                        Priority    = cfg?.priority != null ? (int)cfg.priority : 50,
                        Enabled     = modStates.TryGetValue(modId, out bool en) ? en : defaultEnabled
                    });
                }
                catch { }
            }

            _mods = _mods.OrderBy(m => m.Priority).ThenBy(m => m.Id).ToList();
        }

        private void BuildModsSettingsUI()
        {
            modsSettingsStack.Children.Clear();
            tbSettingsModsHeader.Text = Loc.T("settings.mods");

            if (_mods.Count == 0)
            {
                tbSettingsModsHint.Text       = Loc.T("settings.mods.none");
                tbSettingsModsHint.Visibility = Visibility.Visible;
                return;
            }

            tbSettingsModsHint.Visibility = Visibility.Collapsed;

            foreach (var mod in _mods)
            {
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var info = new StackPanel { Orientation = Orientation.Vertical };
                info.Children.Add(new TextBlock
                {
                    Text       = mod.Name + (string.IsNullOrEmpty(mod.Version) ? string.Empty : $"  v{mod.Version}"),
                    Foreground = ThemeBrush("Text.Primary"),
                    FontSize   = 12
                });
                if (!string.IsNullOrEmpty(mod.Author))
                    info.Children.Add(new TextBlock
                    {
                        Text       = mod.Author,
                        Foreground = ThemeBrush("Mod.Author"),
                        FontSize   = 10
                    });
                Grid.SetColumn(info, 0);

                var cb = new CheckBox
                {
                    IsChecked         = mod.Enabled,
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag               = mod.Id
                };
                Grid.SetColumn(cb, 1);
                cb.Checked   += ModToggle_Changed;
                cb.Unchecked += ModToggle_Changed;

                row.Children.Add(info);
                row.Children.Add(cb);
                modsSettingsStack.Children.Add(row);
            }

            modsSettingsStack.Children.Add(new TextBlock
            {
                Text            = Loc.T("settings.mods.restart_hint"),
                FontSize        = 10,
                Foreground      = ThemeBrush("Mod.Hint"),
                Margin          = new Thickness(0, 8, 0, 0),
                TextWrapping    = TextWrapping.Wrap
            });
        }

        private void ModToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is string modId)
            {
                var mod = _mods.FirstOrDefault(m => m.Id == modId);
                if (mod != null) mod.Enabled = cb.IsChecked == true;
                SaveAppSettings();
            }
        }

        // ── Story package selection ─────────────────────────────────────────────

        private string PickStoryPackage(string basePath)
        {
            var packages = Directory.GetDirectories(basePath)
                .Where(d => File.Exists(Path.Combine(d, "config.json")))
                .OrderBy(d => d)
                .ToList();

            if (packages.Count == 0)
            {
                MessageBox.Show("No story packages found in 'story/'.\n" +
                    "Each story needs its own subfolder containing a config.json.");
                return null;
            }

            return ShowStoryPicker(packages);
        }

        private string ShowStoryPicker(List<string> packages)
        {
            var win = new Window
            {
                Title = "ACVN – Select Story",
                Width = 440,
                Height = 370,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = ThemeBrush("App.Bg")
            };

            var root = new StackPanel { Margin = new Thickness(24) };

            root.Children.Add(new TextBlock
            {
                Text = "Choose a Story",
                Foreground = ThemeBrush("Text.Primary"),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 14)
            });

            var list = new ListBox
            {
                Background = ThemeBrush("Surface.Bg"),
                Foreground = ThemeBrush("Text.Primary"),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4),
                Height = 160
            };

            foreach (var pkg in packages)
            {
                string label = Path.GetFileName(pkg);
                try
                {
                    dynamic cfg = JsonConvert.DeserializeObject<dynamic>(
                        File.ReadAllText(Path.Combine(pkg, "config.json"), System.Text.Encoding.UTF8));
                    string n = cfg?.name?.ToString();
                    string v = cfg?.version?.ToString();
                    if (!string.IsNullOrWhiteSpace(n)) label = n;
                    if (!string.IsNullOrWhiteSpace(v)) label += $"  v{v}";
                }
                catch { }

                list.Items.Add(new ListBoxItem
                {
                    Content = label,
                    Tag = pkg,
                    Padding = new Thickness(8, 6, 8, 6)
                });
            }

            list.SelectedIndex = 0;
            root.Children.Add(list);

            string result = packages[0];

            var btnBorder = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x33, 0x88, 0xFF)),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 14, 0, 0),
                Padding = new Thickness(0, 10, 0, 10),
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = new TextBlock
                {
                    Text = "▶  Play",
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            };

            btnBorder.MouseLeftButtonDown += (_, _) =>
            {
                if (list.SelectedItem is ListBoxItem li && li.Tag is string p)
                    result = p;
                win.DialogResult = true;
            };
            root.Children.Add(btnBorder);

            win.Content = root;
            win.ShowDialog();
            return result;
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
                ShowMcEditorScreen();
            }
        }

        private string clearPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path ?? string.Empty;
            return Regex.Replace(path, @"_", "/");
        }

        /* CONTENT HANDLING */

        /// <summary>
        /// Parses all #begin…#end blocks from an .acvn file into a dictionary.
        /// Key = block name (e.g. "start", "walk:after"). Value = raw block content.
        /// </summary>
        private static Dictionary<string, string> ParseBlocks(string fileContent)
        {
            var blocks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string pattern = @"#begin\s+(\S+)\s(.*?)#end";
            foreach (Match m in Regex.Matches(fileContent, pattern, RegexOptions.Singleline))
                if (m.Success)
                    blocks[m.Groups[1].Value.Trim()] = m.Groups[2].Value;
            return blocks;
        }

        private void InitContent()
        {
            string filePath = Path.Combine(roomsPath, clearPath(currentRoom) + ".acvn");
            if (!File.Exists(filePath) || !Path.GetExtension(filePath).Equals(".acvn", StringComparison.OrdinalIgnoreCase))
            {
                // Check mod rooms before giving up
                filePath = null;
                foreach (var mod in ActiveMods)
                {
                    string candidate = System.IO.Path.Combine(mod.Path, "rooms", clearPath(currentRoom) + ".acvn");
                    if (File.Exists(candidate)) { filePath = candidate; break; }
                }
                if (filePath == null)
                {
                    string msg = $"File not found: '{Path.Combine(roomsPath, clearPath(currentRoom) + ".acvn")}'";
                    LogError(msg);
                    ShowDebugInfo(msg, isError: true);
                    MessageBox.Show(msg);
                    return;
                }
            }

            try
            {
                // 1. Load story blocks
                string fileContent = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                var blocks = ParseBlocks(fileContent);

                // 2. Merge mod room blocks (in priority order; later mods override earlier)
                foreach (var mod in ActiveMods)
                {
                    string modRoomFile = System.IO.Path.Combine(mod.Path, "rooms", clearPath(currentRoom) + ".acvn");
                    if (!File.Exists(modRoomFile)) continue;
                    try
                    {
                        string modContent = File.ReadAllText(modRoomFile, System.Text.Encoding.UTF8);
                        foreach (var kv in ParseBlocks(modContent))
                            blocks[kv.Key] = kv.Value;   // override or add
                    }
                    catch { }
                }

                // 3. Locate the active block
                if (!blocks.TryGetValue(currentAction, out string blockContent))
                {
                    mainContent.NavigateToString(fileContent);
                    return;
                }

                // 4. Apply :before / :after patches
                if (blocks.TryGetValue(currentAction + ":before", out string before))
                    blockContent = before + blockContent;
                if (blocks.TryGetValue(currentAction + ":after", out string after))
                    blockContent = blockContent + after;

                // 5. Render
                blockContent = RenderTemplate(blockContent);
                ShowRandomMedia(clearPath(currentRoom) + "/" + (currentAction == "start" ? "" : clearPath(currentAction)));
                ParseRooms(blockContent);
                ParseContent(blockContent);
                UpdateStatusBar();
                UpdateInventoryPanel();
                UpdateJournalPanel();
            }
            catch (Exception ex)
            {
                LogError("Exception in InitContent", ex.ToString());
                ShowDebugInfo("Fehler: " + ex.Message, isError: true);
                MessageBox.Show($"Error while reading the file: {ex.Message}");
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
            // Story CSS
            string cssContent = string.Empty;
            if (File.Exists(Path.Combine(storyPath, "style.css")))
                cssContent = File.ReadAllText(Path.Combine(storyPath, "style.css"), System.Text.Encoding.UTF8);

            // Append mod CSS (in priority order)
            foreach (var mod in ActiveMods)
            {
                string modCss = System.IO.Path.Combine(mod.Path, "style.css");
                if (File.Exists(modCss))
                    cssContent += "\n" + File.ReadAllText(modCss, System.Text.Encoding.UTF8);
            }

            // IE=Edge forces the WPF WebBrowser out of IE7 compat mode into IE11,
            // which is required for HTML5 <video> playback and modern CSS.
            string contentClean = "<meta http-equiv=\"X-UA-Compatible\" content=\"IE=Edge\">" +
                                  "<meta charset=\"utf-8\"><style>" + cssContent + "</style>";

            // Prepend any quest notifications that were queued during template rendering
            if (_pendingQuestNotifications.Count > 0)
            {
                contentClean += string.Concat(_pendingQuestNotifications);
                _pendingQuestNotifications.Clear();
            }

            contentClean += Regex.Replace(content, @"#begin.*\n|#end\n*", string.Empty);
            contentClean = Regex.Replace(contentClean, @"\[\[.*?\]\]", string.Empty);
            mainContent.NavigateToString(contentClean);
        }

        private static System.Windows.Media.SolidColorBrush BarBrush(int value, int min, int max, bool invert = false)
        {
            double t = max == min ? 1.0 : Math.Clamp((double)(value - min) / (max - min), 0.0, 1.0);
            if (invert) t = 1.0 - t;
            byte r = (byte)(255 * (1 - t));
            byte g = (byte)(180 * t);
            return new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(r, g, 0x00));
        }

        private static ProgressBar ColoredBar(int value, int min, int max, Thickness margin, bool invert = false)
        {
            var bar = new ProgressBar { Minimum = min, Maximum = max, Value = value, Margin = margin };
            bar.Foreground = BarBrush(value, min, max, invert);
            return bar;
        }

        private void UpdateStatusBar()
        {
            Character mc = GetCharacter("mc");

            if (mc != null)
            {
                statusStack.Children.Clear();

                if (mc.Properties.TryGetValue("attributes", out var attributes) && attributes is JObject attributesObject)
                {
                    bool showHidden = showHiddenToggle.IsChecked == true;
                    bool firstHidden = true;

                    foreach (var attribute in attributesObject)
                    {
                        var attributeValues = attribute.Value as JObject;
                        bool isHidden = attributeValues?.Value<bool?>("hidden") == true;

                        if (isHidden && !showHidden)
                            continue;

                        int min   = attributeValues.Value<int>("min");
                        int max   = attributeValues.Value<int>("max");
                        int value = attributeValues.Value<int>("value");
                        string name = attributeValues.Value<string>("name");

                        // Separator before first hidden attribute
                        if (isHidden && firstHidden)
                        {
                            firstHidden = false;
                            statusStack.Children.Add(new System.Windows.Shapes.Rectangle
                            {
                                Height = 1,
                                Fill   = ThemeBrush("Status.Sep"),
                                Margin = new Thickness(0, 4, 0, 8)
                            });
                        }

                        var wrapper = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

                        var labelBrush = isHidden ? ThemeBrush("Status.Lbl.H") : ThemeBrush("Status.Lbl");
                        var valueBrush = isHidden ? ThemeBrush("Status.Val.H") : ThemeBrush("Status.Val");

                        var labelRow = new Grid();
                        labelRow.Children.Add(new TextBlock
                        {
                            Text = name,
                            HorizontalAlignment = HorizontalAlignment.Left,
                            Foreground = labelBrush,
                            FontSize = isHidden ? 10 : 11
                        });
                        labelRow.Children.Add(new TextBlock
                        {
                            Text = $"{value} / {max}",
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Foreground = valueBrush,
                            FontSize = isHidden ? 10 : 11
                        });

                        wrapper.Children.Add(labelRow);
                        bool invertBar = attribute.Key.Equals("horny", StringComparison.OrdinalIgnoreCase);
                        wrapper.Children.Add(ColoredBar(value, min, max, new Thickness(0, 4, 0, 0), invertBar));

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
                        SetIntroLayout(false);
                        InitContent();
                    }
                    return;

                case "__quickload__":
                    string qpath = Path.Combine(saveGamePath, "quicksave.acvnsave");
                    if (!File.Exists(qpath)) { MessageBox.Show(Loc.T("confirm.noQuicksave")); return; }
                    RestoreGameState(new SaveGameManager().LoadGame(qpath));
                    mainContent.Visibility = Visibility.Visible;
                    SetIntroLayout(false);
                    InitContent();
                    return;

                case "__startgame__":
                    HandleStartGame();
                    return;

                case "__ageyes__":
                    ShowMcEditorScreen();
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
                _wardrobeReadOnly = false;
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
            // Search order: mod images (priority order) → story images.
            // Walk up the hierarchy at each root until a folder with files is found.
            string originalSearch = pathToSearch.TrimEnd('/');

            // Build ordered list of image roots: mods first (they can override story images)
            var imageRoots = ActiveMods
                .Select(m => System.IO.Path.Combine(m.Path, "images"))
                .Where(Directory.Exists)
                .ToList();
            imageRoots.Add(imagesPath);

            string search = originalSearch;
            while (true)
            {
                if (!string.IsNullOrEmpty(search))
                {
                    foreach (var root in imageRoots)
                    {
                        string dir = Path.GetFullPath(Path.Combine(root, search));
                        if (!Directory.Exists(dir)) continue;
                        var files = Directory.GetFiles(dir, "*.*");
                        if (files.Length == 0) continue;

                        int    idx        = new Random().Next(0, files.Length);
                        string fileName   = Path.GetFileName(files[idx]);
                        bool   isFallback = search != originalSearch;
                        DisplayMedia(files[idx]);
                        if (isFallback)
                        {
                            string msg = $"Media fallback [{originalSearch}] → [{search}]: {fileName}";
                            ShowDebugInfo(msg, isError: false);
                            LogError(msg);
                        }
                        else
                        {
                            ShowDebugInfo($"Media [{search}]: {fileName}", isError: false);
                        }
                        return;
                    }
                }

                int lastSlash = search.LastIndexOf('/');
                if (lastSlash < 0)
                {
                    mainMedia.Visibility           = Visibility.Collapsed;
                    mainImage.Visibility           = Visibility.Collapsed;
                    mediaFullscreenHint.Visibility = Visibility.Collapsed;
                    string noMediaMsg = $"No media found for: {originalSearch}";
                    ShowDebugInfo(noMediaMsg, isError: true);
                    LogError(noMediaMsg);
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

        public void showHiddenToggle_Changed(object sender, RoutedEventArgs e)
        {
            SaveAppSettings();
            UpdateStatusBar();
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
                showHiddenToggle.IsChecked = s.ShowHiddenAttributes;
                debugToggle.IsChecked = s.DebugEnabled;
                debugEnabled = s.DebugEnabled;
                if (debugEnabled)
                    debugPanel.Visibility = Visibility.Visible;
                if (!string.IsNullOrEmpty(s.Language))
                    Loc.SetLanguage(s.Language);
                _currentTheme = s.Theme ?? "System";
                // Theme is already applied by App.OnStartup; just sync the dropdown
                PopulateThemeDropdown();
            }
            catch { /* ignore corrupt settings */ }
        }

        private void SaveAppSettings()
        {
            if (!_settingsReady) return;
            try
            {
                var existing = ReadAppSettings();
                existing.Volume               = volumeSlider.Value;
                existing.VideoAutoplay        = videoAutoplay;
                existing.Language             = Loc.CurrentLanguage;
                existing.ShowHiddenAttributes = showHiddenToggle.IsChecked == true;
                existing.DebugEnabled         = debugEnabled;
                existing.Theme                = _currentTheme;
                existing.ModStates            = _mods.ToDictionary(m => m.Id, m => m.Enabled);
                File.WriteAllText(SettingsFilePath, JsonConvert.SerializeObject(existing, Formatting.Indented));
            }
            catch { }
        }

        private AppSettings ReadAppSettings()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var s = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(SettingsFilePath));
                    if (s != null) return s;
                }
            }
            catch { }
            return new AppSettings();
        }

        private void WriteAppSettings(AppSettings s)
        {
            try { File.WriteAllText(SettingsFilePath, JsonConvert.SerializeObject(s, Formatting.Indented)); }
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
            mainContent.Visibility   = Visibility.Visible;
            gameTime = DateTime.Now;
            gameVars.Clear();
            navigationHistory.Clear();
            LoadMods();
            GetCharacters();
            LoadItems();
            LoadClothes();
            LoadQuests();
            LoadSchedules();
            InitInventoryFromDefaults();
            questProgress.Clear();
            wornClothing.Clear();
            InitClothingFromDefaults();
            AutoEquipOnStart();
            UpdateTemplateVariables();
            BuildModsSettingsUI();
            ShowIntroScreen();
        }

        public void settingsButton_Click(object sender, RoutedEventArgs e)
        {
            bool show = settingsPanel.Visibility != Visibility.Visible;
            settingsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            // WebBrowser is HWND-based and always renders above WPF overlays (airspace problem).
            // Follow the same hide/restore pattern used by other overlays (wardrobe, phone, etc.).
            mainContent.Visibility = show ? Visibility.Hidden : Visibility.Visible;
        }

        private void mainContent_Navigating(object sender, System.Windows.Navigation.NavigatingCancelEventArgs e)
        {
            // Intercept acvn:// links written directly in content HTML.
            // Format: acvn://room_id/action  (action is optional; defaults to "start")
            if (e.Uri != null && e.Uri.Scheme == "acvn")
            {
                e.Cancel = true;
                string room   = e.Uri.Host;
                string action = e.Uri.AbsolutePath.TrimStart('/');
                ExecuteCommand(string.IsNullOrEmpty(action)
                    ? new[] { "", room }
                    : new[] { "", room, action });
            }
        }

        public void cheatButton_Click(object sender, RoutedEventArgs e)
        {
            settingsPanel.Visibility = Visibility.Collapsed;
            mainContent.Visibility   = Visibility.Hidden;
            BuildCheatUI();
            cheatOverlay.Visibility  = Visibility.Visible;
        }

        public void cheatCloseButton_Click(object sender, RoutedEventArgs e)
        {
            cheatOverlay.Visibility  = Visibility.Collapsed;
            mainContent.Visibility   = Visibility.Visible;
        }

        private void BuildCheatUI()
        {
            cheatStack.Children.Clear();
            if (characters == null) return;

            // MC first, then NPCs — only those with attributes
            var ordered = characters
                .Where(c => c.Properties.ContainsKey("attributes"))
                .OrderByDescending(c => c.IsMainCharacter)
                .ThenBy(c => c.Id);

            foreach (var ch in ordered)
            {
                if (!ch.Properties.TryGetValue("attributes", out var attrsToken)) continue;
                if (attrsToken is not JObject attrsObj) continue;

                // Display name
                string displayName = ch.IsMainCharacter
                    ? (ch.Properties.TryGetValue("firstname", out var mcFn) ? $"{mcFn} (MC)" : "Player (MC)")
                    : ch.Properties.TryGetValue("firstname", out var fn) ? fn.ToString() : ch.Id;

                // ── Card ────────────────────────────────────────────────
                var cardInner = new StackPanel { Margin = new Thickness(0) };

                // Card header
                cardInner.Children.Add(new TextBlock
                {
                    Text       = displayName,
                    FontSize   = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = ThemeBrush("Text.Primary"),
                    Margin     = new Thickness(0, 0, 0, 8)
                });
                cardInner.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Height = 1,
                    Fill   = ThemeBrush("Sep.Dark"),
                    Margin = new Thickness(0, 0, 0, 10)
                });

                bool firstAttr = true;
                foreach (var kv in attrsObj)
                {
                    if (kv.Value is not JObject ao) continue;

                    string attrKey  = kv.Key;
                    string attrName = ao.Value<string>("name") ?? kv.Key;
                    int    min      = ao.Value<int>("min");
                    int    max      = ao.Value<int>("max");
                    int    val      = ao.Value<int>("value");
                    bool   hidden   = ao.Value<bool?>("hidden") == true;
                    string charId   = ch.Id;

                    if (!firstAttr)
                        cardInner.Children.Add(new System.Windows.Shapes.Rectangle
                        {
                            Height = 1,
                            Fill   = ThemeBrush("Sep.Dark"),
                            Margin = new Thickness(0, 6, 0, 6)
                        });
                    firstAttr = false;

                    // Label + live value display
                    var labelBrush = hidden ? ThemeBrush("Status.Lbl.H") : ThemeBrush("Status.Lbl");
                    var valDisplay = new TextBlock
                    {
                        Text                = $"{val}",
                        FontSize            = 11,
                        FontWeight          = FontWeights.SemiBold,
                        Foreground          = ThemeBrush("Status.Val"),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment   = VerticalAlignment.Center,
                        MinWidth            = 36
                    };
                    var rangeDisplay = new TextBlock
                    {
                        Text                = $"/ {max}",
                        FontSize            = 10,
                        Foreground          = ThemeBrush("Text.Muted"),
                        VerticalAlignment   = VerticalAlignment.Center,
                        Margin              = new Thickness(2, 0, 0, 0)
                    };

                    var headerRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                    headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var nameBlock = new TextBlock
                    {
                        Text              = attrName + (hidden ? "  ·" : ""),
                        FontSize          = 11,
                        Foreground        = labelBrush,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(nameBlock,   0);
                    Grid.SetColumn(valDisplay,  1);
                    Grid.SetColumn(rangeDisplay, 2);
                    headerRow.Children.Add(nameBlock);
                    headerRow.Children.Add(valDisplay);
                    headerRow.Children.Add(rangeDisplay);

                    // Slider
                    var slider = new Slider
                    {
                        Minimum             = min,
                        Maximum             = max,
                        Value               = val,
                        TickFrequency       = 1,
                        IsSnapToTickEnabled = true,
                        Margin              = new Thickness(0, 2, 0, 0)
                    };

                    // Capture loop vars for closure
                    var capturedCh      = ch;
                    var capturedKey     = attrKey;
                    var capturedDisplay = valDisplay;

                    slider.ValueChanged += (_, __) =>
                    {
                        int newVal = (int)slider.Value;
                        capturedDisplay.Text = $"{newVal}";
                        if (capturedCh.Properties.TryGetValue("attributes", out var a)
                            && a is JObject ao2
                            && ao2.TryGetValue(capturedKey, out var tok)
                            && tok is JObject attrObj2)
                        {
                            attrObj2["value"] = newVal;
                        }
                        UpdateStatusBar();
                    };

                    cardInner.Children.Add(headerRow);
                    cardInner.Children.Add(slider);
                }

                var card = new Border
                {
                    Background      = ThemeBrush("Subtle.Bg"),
                    BorderBrush     = ThemeBrush("Border.Primary"),
                    BorderThickness = new Thickness(1),
                    CornerRadius    = new CornerRadius(6),
                    Padding         = new Thickness(16, 12, 16, 14),
                    Margin          = new Thickness(0, 0, 0, 12),
                    Child           = cardInner
                };

                cheatStack.Children.Add(card);
            }

            if (cheatStack.Children.Count == 0)
                cheatStack.Children.Add(new TextBlock
                {
                    Text       = "No characters with attributes found.",
                    FontSize   = 12,
                    Foreground = ThemeBrush("Text.Muted"),
                    Margin     = new Thickness(0, 8, 0, 0)
                });
        }

        public void debugToggle_Changed(object sender, RoutedEventArgs e)
        {
            debugEnabled = debugToggle.IsChecked == true;
            if (!debugEnabled)
                debugPanel.Visibility = Visibility.Collapsed;
            SaveAppSettings();
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
            itemDefinitions.Clear();
            string path = Path.Combine(storyPath, "items.json");
            if (File.Exists(path))
                MergeItemsFromJson(File.ReadAllText(path, System.Text.Encoding.UTF8));

            foreach (var mod in ActiveMods)
            {
                string modPath = System.IO.Path.Combine(mod.Path, "items.json");
                if (!File.Exists(modPath)) continue;
                try { MergeItemsFromJson(File.ReadAllText(modPath, System.Text.Encoding.UTF8)); }
                catch { }
            }
        }

        private void MergeItemsFromJson(string json)
        {
            dynamic data = JsonConvert.DeserializeObject<dynamic>(json);
            foreach (var item in data.items)
            {
                string rawId     = item.id?.ToString();
                string fallbackId = item.name?.ToString()?.ToLower()?.Replace(" ", "_");
                string id        = rawId ?? fallbackId;
                itemDefinitions.RemoveAll(i => i.Id == id);
                // Parse optional effects dict: { "hunger": 30, "energy": 10, ... }
                var effects = new Dictionary<string, int>();
                if (item.effects != null)
                {
                    try
                    {
                        foreach (var prop in item.effects)
                            effects[prop.Name] = (int)prop.Value;
                    }
                    catch { }
                }

                itemDefinitions.Add(new ItemDefinition
                {
                    Id               = id,
                    Type             = item.type?.ToString(),
                    Subtype          = item.subtype?.ToString(),
                    Name             = item.name?.ToString(),
                    Description      = item.description?.ToString(),
                    StartingQuantity = item.starting_quantity != null ? (int)item.starting_quantity : 0,
                    Effects          = effects
                });
            }
        }

        private void LoadClothes()
        {
            clothingDefinitions.Clear();
            string path = Path.Combine(storyPath, "clothes.json");
            if (File.Exists(path))
                MergeClothesFromJson(File.ReadAllText(path, System.Text.Encoding.UTF8));

            foreach (var mod in ActiveMods)
            {
                string modPath = System.IO.Path.Combine(mod.Path, "clothes.json");
                if (!File.Exists(modPath)) continue;
                try { MergeClothesFromJson(File.ReadAllText(modPath, System.Text.Encoding.UTF8)); }
                catch { }
            }
        }

        private void MergeClothesFromJson(string json)
        {
            dynamic data = JsonConvert.DeserializeObject<dynamic>(json);
            foreach (var item in data.clothes)
            {
                string id = item.id?.ToString();
                clothingDefinitions.RemoveAll(c => c.Id == id);
                var def = new ClothingDefinition
                {
                    Id          = id,
                    Name        = item.name?.ToString(),
                    Description = item.description?.ToString(),
                    Subtype     = item.subtype?.ToString(),
                    Durability  = item.durability  != null ? (int)item.durability  : 100,
                    Daring      = item.daring      != null ? (int)item.daring      : 0,
                    Inhibition  = item.inhibition  != null ? (int)item.inhibition  : 0,
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
            ownedClothing = new HashSet<string>(
                clothingDefinitions.Where(c => c.StartingQuantity > 0).Select(c => c.Id));
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
            questDefinitions.Clear();
            string path = Path.Combine(storyPath, "quests.json");
            if (File.Exists(path))
                MergeQuestsFromJson(File.ReadAllText(path, System.Text.Encoding.UTF8));

            foreach (var mod in ActiveMods)
            {
                string modPath = System.IO.Path.Combine(mod.Path, "quests.json");
                if (!File.Exists(modPath)) continue;
                try { MergeQuestsFromJson(File.ReadAllText(modPath, System.Text.Encoding.UTF8)); }
                catch { }
            }
        }

        private void MergeQuestsFromJson(string json)
        {
            dynamic data = JsonConvert.DeserializeObject<dynamic>(json);
            foreach (var quest in data.quests)
            {
                string id = quest.id.ToString();
                questDefinitions.RemoveAll(q => q.Id == id);
                var def = new QuestDefinition
                {
                    Id   = id,
                    Name = quest.name.ToString(),
                    Hint = quest.hint?.ToString()
                };
                foreach (var step in quest.steps)
                    def.Steps.Add(new QuestStepDef
                    {
                        Id          = step.id.ToString(),
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

            // — Clothing section —
            var clothingContent = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
            var clothingItems = ownedClothing
                .Select(id => clothingDefinitions.FirstOrDefault(c => c.Id == id))
                .Where(d => d != null).ToList();

            if (clothingItems.Count == 0)
                AddInvEmptyNote(clothingContent);
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
                        Foreground        = ThemeBrush("Text.Primary"),
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
                        Foreground        = ThemeBrush("Text.Hint"),
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
                        _wardrobeReadOnly     = true;
                        ShowWardrobe(resetState: false);
                    };
                    clothingContent.Children.Add(btn);
                }

            AddInvCollapsibleSection("clothing", Loc.T("inv.clothing"), clothingContent);

            // — Items section —
            var itemsContent = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
            var visibleItems = inventory.Where(kv => kv.Value > 0).ToList();
            if (visibleItems.Count == 0)
                AddInvEmptyNote(itemsContent);
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
                        Foreground        = ThemeBrush("Text.Primary"),
                        FontSize          = 13,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(nameBlock, 0);
                    row.Children.Add(nameBlock);

                    var qtyBlock = new TextBlock
                    {
                        Text              = $"×{kv.Value}",
                        Foreground        = ThemeBrush("Inv.Count"),
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
                    itemsContent.Children.Add(btn);
                }

            AddInvCollapsibleSection("items", Loc.T("inv.items"), itemsContent);
        }

        /// <summary>Adds a collapsible section header + content block to the inventory panel.</summary>
        private void AddInvCollapsibleSection(string key, string title, StackPanel content)
        {
            bool collapsed = _inventoryCollapsed.TryGetValue(key, out bool c) && c;
            content.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;

            var chevron = new TextBlock
            {
                Text                = collapsed ? "▼" : "▲",
                FontSize            = 9,
                Foreground          = ThemeBrush("Journal.Chevron"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Center
            };

            var headerGrid = new Grid();
            headerGrid.Children.Add(new TextBlock
            {
                Text       = title.ToUpper(),
                FontSize   = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeBrush("Inv.Section"),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerGrid.Children.Add(chevron);

            var header = new Border
            {
                Background   = ThemeBrush("Journal.Hdr.Bg"),
                CornerRadius = new CornerRadius(4),
                Padding      = new Thickness(6, 5, 6, 5),
                Margin       = new Thickness(0, 6, 0, 0),
                Cursor       = System.Windows.Input.Cursors.Hand,
                Child        = headerGrid
            };

            string capturedKey = key;
            header.MouseLeftButtonUp += (_, __) =>
            {
                bool vis               = content.Visibility == Visibility.Visible;
                content.Visibility     = vis ? Visibility.Collapsed : Visibility.Visible;
                chevron.Text           = vis ? "▼" : "▲";
                _inventoryCollapsed[capturedKey] = vis; // true = collapsed
            };

            inventoryStack.Children.Add(header);
            inventoryStack.Children.Add(content);
        }

        private void AddInvEmptyNote(StackPanel target)
        {
            target.Children.Add(new TextBlock
            {
                Text       = Loc.T("inv.empty"),
                FontSize   = 11,
                Foreground = ThemeBrush("Inv.Empty"),
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
                Foreground = ThemeBrush("Journal.Chevron"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Center
            };
            var headerGrid = new Grid();
            headerGrid.Children.Add(new TextBlock
            {
                Text       = title,
                FontSize   = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeBrush("Text.SectionHdr")
            });
            headerGrid.Children.Add(chevron);

            var header = new Border
            {
                Background    = ThemeBrush("Journal.Hdr.Bg"),
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
                    Foreground = ThemeBrush("Journal.Empty"),
                    Margin     = new Thickness(8, 4, 0, 0)
                });
                return;
            }

            foreach (var def in quests)
            {
                questProgress.TryGetValue(def.Id, out int step);

                string icon  = kind == "done" ? "✓ " : kind == "aktiv" ? "◉ " : "○ ";
                System.Windows.Media.Brush nameBrush = kind == "done"
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0xAA, 0x55))
                    : kind == "aktiv"
                        ? ThemeBrush("Text.Primary")
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x99, 0x99, 0x99));

                content.Children.Add(new TextBlock
                {
                    Text       = icon + def.Name,
                    FontSize   = 13,
                    FontWeight = kind == "aktiv" ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = nameBrush,
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
            MergeCharactersFromJson(json);

            // Merge characters from active mods (mod entry with same ID overrides story entry)
            foreach (var mod in ActiveMods)
            {
                string modPath = System.IO.Path.Combine(mod.Path, "chars.json");
                if (!File.Exists(modPath)) continue;
                try { MergeCharactersFromJson(File.ReadAllText(modPath, System.Text.Encoding.UTF8)); }
                catch { }
            }
        }

        private void MergeCharactersFromJson(string json)
        {
            dynamic data = JsonConvert.DeserializeObject<dynamic>(json);
            foreach (var charData in data.chars)
            {
                string id = charData.id?.ToString();
                // Remove existing entry with same ID so mod can override
                characters.RemoveAll(c => c.Id == id);
                var character = new Character { Id = id };
                foreach (var property in charData)
                {
                    if (property.Name == "id") continue;
                    if (property.Name == "is_main_character")
                    {
                        character.IsMainCharacter = (bool)property.Value;
                        continue;
                    }
                    character.Properties[property.Name] = property.Value;
                }
                characters.Add(character);
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
                SetIntroLayout(false);
                InitContent();
            }
        }

        public void quickSaveButton_Click(object sender, RoutedEventArgs e)
        {
            new SaveGameManager().SaveGame(BuildGameState(), Path.Combine(saveGamePath, "quicksave.acvnsave"));
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
            SetIntroLayout(false);
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
                    Background = ThemeBrush("Journal.Hdr.Bg"),
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
                    Foreground = ThemeBrush("Text.Primary")
                });
                if (!string.IsNullOrEmpty(age))
                    inner.Children.Add(new TextBlock
                    {
                        Text = $"{age} {Loc.T("rel.years")}",
                        FontSize = 11,
                        Foreground = ThemeBrush("Mod.Author"),
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
                            Foreground = ThemeBrush("Status.Lbl"),
                            HorizontalAlignment = HorizontalAlignment.Left
                        });
                        row.Children.Add(new TextBlock
                        {
                            Text = $"{val}/{max}",
                            FontSize = 10,
                            Foreground = ThemeBrush("Status.Val"),
                            HorizontalAlignment = HorizontalAlignment.Right
                        });
                        inner.Children.Add(row);
                        inner.Children.Add(ColoredBar(val, min, max, new Thickness(0, 2, 0, 0)));
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
                    Foreground = ThemeBrush("Text.Muted"),
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
            wardrobeCard.Width  = ActualWidth  * 0.90;
            wardrobeCard.Height = ActualHeight * 0.88;
            mainContent.Visibility = Visibility.Hidden;
            mainMedia.Visibility   = Visibility.Hidden;
            mainImage.Visibility   = Visibility.Hidden;
            RebuildWardrobePanel();
            wardrobeOverlay.Visibility = Visibility.Visible;
        }

        private void CloseWardrobe()
        {
            wardrobeOverlay.Visibility = Visibility.Collapsed;
            if (!_wardrobeReadOnly)
            {
                mainContent.Visibility = Visibility.Visible;
                InitContent();
            }
            else
            {
                mainContent.Visibility = Visibility.Visible;
                if (currentMediaSource != null)
                {
                    bool isVid = VideoExtensions.Contains(Path.GetExtension(currentMediaSource.LocalPath));
                    mainMedia.Visibility = isVid ? Visibility.Visible : Visibility.Collapsed;
                    mainImage.Visibility = isVid ? Visibility.Collapsed : Visibility.Visible;
                }
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

            AddWardrobeTile(grid, "clothes", row: 0, col: 0, rowSpan: 3, large: true);
            AddWardrobeTile(grid, "bra",     row: 0, col: 1);
            AddWardrobeTile(grid, "panties", row: 1, col: 1);
            AddWardrobeTile(grid, "shoes",   row: 2, col: 1);

            wardrobeStack.Children.Add(grid);

            // ── Undress all ──────────────────────────────────────────────
            var undressBtn = new Button
            {
                Content    = Loc.T("wardrobe.undress_all"),
                Margin     = new Thickness(0, 10, 0, 0),
                Padding    = new Thickness(14, 6, 14, 6),
                Background = new SolidColorBrush(Color.FromRgb(0x5A, 0x1A, 0x1A)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor     = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            undressBtn.Click += (_, __) =>
            {
                wornClothing.Clear();
                RebuildWardrobePanel();
            };
            wardrobeStack.Children.Add(undressBtn);

            // ── Outfit presets ───────────────────────────────────────────
            wardrobeStack.Children.Add(new TextBlock
            {
                Text       = Loc.T("wardrobe.outfits"),
                FontSize   = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                Margin     = new Thickness(0, 14, 0, 4)
            });

            // Save row: text box + save button
            var saveRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var nameBox = new TextBox
            {
                Width           = 180,
                Background      = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                Foreground      = Brushes.White,
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                BorderThickness = new Thickness(1),
                Padding         = new Thickness(6, 4, 6, 4),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            var saveBtn = new Button
            {
                Content    = Loc.T("wardrobe.save_outfit"),
                Margin     = new Thickness(6, 0, 0, 0),
                Padding    = new Thickness(10, 4, 10, 4),
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x50, 0x30)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor     = System.Windows.Input.Cursors.Hand
            };
            saveBtn.Click += (_, __) =>
            {
                string name = nameBox.Text.Trim();
                if (string.IsNullOrEmpty(name)) return;
                var settings = ReadAppSettings();
                settings.OutfitPresets[name] = new Dictionary<string, string>(wornClothing);
                WriteAppSettings(settings);
                RebuildWardrobePanel();
            };
            saveRow.Children.Add(nameBox);
            saveRow.Children.Add(saveBtn);
            wardrobeStack.Children.Add(saveRow);

            // Saved preset list
            var settings2 = ReadAppSettings();
            foreach (var kv in settings2.OutfitPresets)
            {
                string presetName = kv.Key;
                var preset        = kv.Value;
                var row = new Border
                {
                    Background      = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
                    CornerRadius    = new CornerRadius(4),
                    Padding         = new Thickness(8, 6, 8, 6),
                    Margin          = new Thickness(0, 2, 0, 2),
                    BorderBrush     = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38)),
                    BorderThickness = new Thickness(1)
                };
                var rowPanel = new Grid();
                rowPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var nameLabel = new TextBlock
                {
                    Text              = presetName,
                    Foreground        = Brushes.White,
                    FontSize          = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var loadBtn = new Button
                {
                    Content    = Loc.T("wardrobe.load_outfit"),
                    Margin     = new Thickness(6, 0, 4, 0),
                    Padding    = new Thickness(8, 3, 8, 3),
                    Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x3A, 0x5A)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor     = System.Windows.Input.Cursors.Hand
                };
                loadBtn.Click += (_, __) =>
                {
                    wornClothing.Clear();
                    foreach (var p in preset) wornClothing[p.Key] = p.Value;
                    RebuildWardrobePanel();
                };
                var deleteBtn = new Button
                {
                    Content    = "✕",
                    Padding    = new Thickness(6, 3, 6, 3),
                    Background = new SolidColorBrush(Color.FromRgb(0x44, 0x22, 0x22)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor     = System.Windows.Input.Cursors.Hand
                };
                deleteBtn.Click += (_, __) =>
                {
                    var s = ReadAppSettings();
                    s.OutfitPresets.Remove(presetName);
                    WriteAppSettings(s);
                    RebuildWardrobePanel();
                };

                Grid.SetColumn(nameLabel,  0);
                Grid.SetColumn(loadBtn,    1);
                Grid.SetColumn(deleteBtn,  2);
                rowPanel.Children.Add(nameLabel);
                rowPanel.Children.Add(loadBtn);
                rowPanel.Children.Add(deleteBtn);
                row.Child = rowPanel;
                wardrobeStack.Children.Add(row);
            }
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
                MinHeight       = large ? 320 : 140,
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
                        Height  = large ? 200 : 88,
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

            if (!_wardrobeReadOnly && !canWear && !isWorn)
                wardrobeStack.Children.Add(new TextBlock
                {
                    Text       = Loc.T("wardrobe.inhib_hint", def.Inhibition),
                    FontSize   = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)),
                    Margin     = new Thickness(0, 0, 0, 4)
                });

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };

            if (!_wardrobeReadOnly)
            {
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
            string html = $"<meta http-equiv=\"X-UA-Compatible\" content=\"IE=Edge\">" +
                          $"<meta charset=\"utf-8\"><style>body{{background:#111;color:#ddd;font-family:'Segoe UI';}}{css}</style>";
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
            if (tbThemeLabel != null) tbThemeLabel.Text = Loc.T("theme.label");
            PopulateThemeDropdown();
            tbSettingsRestart.Text          = Loc.T("settings.restart");
            // Game settings tab
            tbSettingsDisplayHeader.Text    = Loc.T("settings.display");
            tbSettingShowHidden.Text        = Loc.T("settings.show_hidden");
            tbSettingsModsHeader.Text       = Loc.T("settings.mods");
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
            BuildModsSettingsUI();
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

        private void PopulateThemeDropdown()
        {
            if (themeDropdown == null) return;
            _settingsReady = false;
            themeDropdown.Items.Clear();
            var options = new[]
            {
                ("System", Loc.T("theme.system")),
                ("Dark",   Loc.T("theme.dark")),
                ("Light",  Loc.T("theme.light"))
            };
            foreach (var (value, label) in options)
            {
                themeDropdown.Items.Add(new System.Windows.Controls.ComboBoxItem
                {
                    Content = label,
                    Tag     = value
                });
            }
            // Select current
            foreach (System.Windows.Controls.ComboBoxItem item in themeDropdown.Items)
                if (item.Tag as string == _currentTheme) { themeDropdown.SelectedItem = item; break; }
            if (themeDropdown.SelectedIndex < 0) themeDropdown.SelectedIndex = 0;
            _settingsReady = true;
        }

        public void themeDropdown_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_settingsReady) return;
            if (themeDropdown.SelectedItem is System.Windows.Controls.ComboBoxItem item
                && item.Tag is string theme)
            {
                _currentTheme = theme;
                string resolved = App.ResolveThemeName(theme);
                App.LoadThemeDict(resolved);
                RefreshDynamicPanels();
                SaveAppSettings();
            }
        }

        /// <summary>Rebuild all C#-generated panels so they pick up new theme brushes.</summary>
        private void RefreshDynamicPanels()
        {
            UpdateStatusBar();
            UpdateJournalPanel();
            UpdateInventoryPanel();
            BuildModsSettingsUI();
            // Relationships panel only if it has content:
            if (relationshipsStack.Children.Count > 0) UpdateRelationshipsPanel();
        }

        /* SETUP SCREENS
           Step 0: MC appearance editor  (ShowMcEditorScreen)
           Step 1: NPC character setup   (ShowSetupScreen)
           Driven by _setupStep; setupContinueBtn_Click advances between them. */

        private void ShowMcEditorScreen()
        {
            _setupStep = 0;
            setupStack.Children.Clear();
            _setupFields.Clear();

            var mc = GetCharacter("mc");
            string Prop(string key, string fallback = "") =>
                mc?.Properties.TryGetValue(key, out var v) == true ? v.ToString() : fallback;

            setupStack.Children.Add(new TextBlock
            {
                Text       = Loc.T("setup.mc.title"),
                FontSize   = 20, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin     = new Thickness(0, 0, 0, 4)
            });
            setupStack.Children.Add(new TextBlock
            {
                Text       = Loc.T("setup.mc.subtitle"),
                FontSize   = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                Margin     = new Thickness(0, 0, 0, 20)
            });

            AddSetupSectionHeader(Loc.T("setup.mc.general"));
            AddSetupField("mc.age", Loc.T("setup.age"), Prop("age", "18"));

            AddSetupSectionHeader(Loc.T("setup.mc.body"));
            AddSetupSlider("mc.height",   Loc.T("setup.height"),    140, 200, int.TryParse(Prop("height", "165"), out int h) ? h : 165, "cm");
            AddSetupSlider("mc.weight",   Loc.T("setup.weight"),    40,  100, int.TryParse(Prop("weight", "58"),  out int w) ? w : 58,  "kg");
            AddSetupCombo ("mc.cup_size", Loc.T("setup.cup_size"),
                new[] { "A", "B", "C", "D", "DD", "E", "F", "G" }, Prop("cup_size", "B"));

            AddSetupSectionHeader(Loc.T("setup.mc.eyes"));
            AddSetupCombo("mc.eye_color", Loc.T("setup.eye_color"),
                new[] { "Blue", "Green", "Brown", "Grey", "Hazel", "Amber", "Black" }, Prop("eye_color", "Brown"));
            AddSetupCombo("mc.eye_size",  Loc.T("setup.eye_size"),
                new[] { "Small", "Normal", "Large", "Very Large" }, Prop("eye_size", "Normal"));

            AddSetupSectionHeader(Loc.T("setup.mc.hair"));
            AddSetupCombo("mc.hair_color",  Loc.T("setup.hair_color"),
                new[] { "Blonde", "Brunette", "Black", "Red", "Auburn", "Platinum", "Silver" }, Prop("hair_color", "Brunette"));
            AddSetupCombo("mc.hair_length", Loc.T("setup.hair_length"),
                new[] { "Pixie Cut", "Short", "Medium", "Long", "Very Long" }, Prop("hair_length", "Medium"));

            setupContinueBtn.Content = Loc.T("setup.next");
            mainContent.Visibility   = Visibility.Hidden;
            mainMedia.Visibility     = Visibility.Hidden;
            mainImage.Visibility     = Visibility.Hidden;
            setupOverlay.Visibility  = Visibility.Visible;
        }

        private void ShowSetupScreen()
        {
            _setupStep = 1;
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

            string Get(Character ch, string key, string fallback = "") =>
                ch?.Properties.TryGetValue(key, out var v) == true ? v.ToString() : fallback;

            // Only characters explicitly marked as main characters in chars.json
            var mainChars = characters.Where(c => c.IsMainCharacter).ToList();

            if (mainChars.Count == 0)
            {
                setupStack.Children.Add(new TextBlock
                {
                    Text       = Loc.T("setup.no_main_chars"),
                    FontSize   = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88))
                });
            }
            else
            {
                foreach (var ch in mainChars)
                {
                    // Section header: use existing firstname as label, fall back to id
                    string headerLabel = Get(ch, "firstname", ch.Id);
                    AddSetupSectionHeader(headerLabel);

                    string prefix = ch.Id + ".";
                    AddSetupField(prefix + "firstname",        Loc.T("setup.firstname"),        Get(ch, "firstname"));
                    AddSetupField(prefix + "lastname",         Loc.T("setup.lastname"),         Get(ch, "lastname"));
                    AddSetupField(prefix + "nickname",         Loc.T("setup.nickname"),         Get(ch, "nickname"));
                    AddSetupField(prefix + "age",              Loc.T("setup.age"),              Get(ch, "age"));
                    AddSetupField(prefix + "relation",         Loc.T("setup.relation"),         Get(ch, "relation"));
                    AddSetupField(prefix + "relation_reverse", Loc.T("setup.relation_reverse"), Get(ch, "relation_reverse"));
                }
            }

            setupContinueBtn.Content = Loc.T("setup.continue");
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
            AddSetupLabel(label);
            var tb = new TextBox { Text = defaultValue, Margin = new Thickness(0, 0, 0, 10) };
            _setupFields[key] = () => tb.Text.Trim();
            setupStack.Children.Add(tb);
        }

        private void AddSetupCombo(string key, string label, string[] options, string selectedValue)
        {
            AddSetupLabel(label);
            var cb = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
            foreach (var opt in options) cb.Items.Add(opt);
            cb.SelectedItem = options.Contains(selectedValue) ? selectedValue : options[0];
            _setupFields[key] = () => cb.SelectedItem?.ToString() ?? options[0];
            setupStack.Children.Add(cb);
        }

        private void AddSetupSlider(string key, string label, int min, int max, int defaultValue, string unit)
        {
            // Label row: "Height   165 cm"
            var valueLabel = new TextBlock
            {
                Text       = $"{defaultValue} {unit}",
                FontSize   = 11,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Center
            };
            var headerRow = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerRow.Children.Add(new TextBlock
            {
                Text       = label,
                FontSize   = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(valueLabel, 1);
            headerRow.Children.Add(valueLabel);
            setupStack.Children.Add(headerRow);

            var slider = new Slider
            {
                Minimum      = min,
                Maximum      = max,
                Value        = Math.Clamp(defaultValue, min, max),
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                Margin       = new Thickness(0, 0, 0, 12)
            };
            slider.ValueChanged += (_, args) =>
                valueLabel.Text = $"{(int)args.NewValue} {unit}";

            _setupFields[key] = () => ((int)slider.Value).ToString();
            setupStack.Children.Add(slider);
        }

        private void AddSetupLabel(string text)
        {
            setupStack.Children.Add(new TextBlock
            {
                Text       = text,
                FontSize   = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                Margin     = new Thickness(0, 0, 0, 3)
            });
        }

        public void setupContinueBtn_Click(object sender, RoutedEventArgs e)
        {
            string Field(string key) =>
                _setupFields.TryGetValue(key, out var getter) ? getter() : string.Empty;

            if (_setupStep == 0)
            {
                // ── Step 0: save MC appearance ──
                var mc = GetCharacter("mc");
                if (mc != null)
                {
                    if (int.TryParse(Field("mc.age"), out int mcAge)) mc.Properties["age"] = mcAge;
                    if (int.TryParse(Field("mc.height"), out int ht))  mc.Properties["height"] = ht;
                    if (int.TryParse(Field("mc.weight"), out int wt))  mc.Properties["weight"] = wt;
                    mc.Properties["cup_size"]    = Field("mc.cup_size");
                    mc.Properties["eye_color"]   = Field("mc.eye_color");
                    mc.Properties["eye_size"]    = Field("mc.eye_size");
                    mc.Properties["hair_color"]  = Field("mc.hair_color");
                    mc.Properties["hair_length"] = Field("mc.hair_length");
                }

                // Advance to NPC setup (or skip if none defined)
                var mainChars = characters.Where(c => c.IsMainCharacter).ToList();
                if (mainChars.Count > 0)
                {
                    ShowSetupScreen();
                }
                else
                {
                    StartGame();
                }
            }
            else
            {
                // ── Step 1: save NPC fields ──
                foreach (var ch in characters.Where(c => c.IsMainCharacter))
                {
                    string prefix = ch.Id + ".";
                    string fn = Field(prefix + "firstname");
                    if (!string.IsNullOrEmpty(fn)) ch.Properties["firstname"] = fn;
                    ch.Properties["lastname"] = Field(prefix + "lastname");
                    string nick = Field(prefix + "nickname");
                    if (string.IsNullOrEmpty(nick)) ch.Properties.Remove("nickname");
                    else ch.Properties["nickname"] = nick;
                    if (int.TryParse(Field(prefix + "age"), out int age)) ch.Properties["age"] = age;
                    ch.Properties["relation"]         = Field(prefix + "relation");
                    ch.Properties["relation_reverse"] = Field(prefix + "relation_reverse");
                }

                StartGame();
            }
        }

        private void StartGame()
        {
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

            public static int GameHour()
                => _instance.gameTime.Hour;

            public static string AdvanceTime(int minutes)
            {
                _instance.gameTime = _instance.gameTime.AddMinutes(minutes);
                return string.Empty;
            }

            public static string AdvanceToHour(int targetHour)
            {
                var t = _instance.gameTime;
                var target = t.Date.AddHours(targetHour);
                if (target <= t)
                    target = target.AddDays(1);
                _instance.gameTime = target;
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

            /// <summary>Add <paramref name="count"/> units of an item at once (e.g. earn currency).</summary>
            public static string AddItems(string itemId, int count)
            {
                var inv = _instance.inventory;
                inv[itemId] = (inv.ContainsKey(itemId) ? inv[itemId] : 0) + count;
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

            /// <summary>
            /// Consume one unit of a usable item (food / consumable) and apply its effects to mc.
            /// Returns an HTML summary string of the applied effects, or an error message if
            /// the item is not owned or has no effects defined.
            /// Use can_use_item first to guard calls in script.
            /// </summary>
            public static string UseItem(string itemId)
            {
                var inv  = _instance.inventory;
                var defs = _instance.itemDefinitions;

                if (!inv.TryGetValue(itemId, out int qty) || qty <= 0)
                    return $"<p style=\"color:#FF6B6B; font-size:11px\">You don't have any {itemId}.</p>";

                var def = defs.FirstOrDefault(d => d.Id == itemId);
                if (def == null || def.Effects == null || def.Effects.Count == 0)
                    return $"<p style=\"color:#888; font-size:11px\">You can't use {itemId} like that.</p>";

                // Consume one unit
                inv[itemId]--;
                if (inv[itemId] <= 0) inv.Remove(itemId);
                _instance.UpdateInventoryPanel();

                // Apply effects to mc using the same JObject pattern as AttrChange
                foreach (var kv in def.Effects)
                    AttrChange("mc", kv.Key, kv.Value);
                _instance.UpdateTemplateVariables();
                _instance.UpdateStatusBar();

                // Build readable effect summary
                var parts = def.Effects.Select(kv =>
                {
                    string sign  = kv.Value >= 0 ? "+" : "";
                    string color = kv.Value >= 0 ? "#4CAF50" : "#FF6B6B";
                    string label = kv.Key.Substring(0, 1).ToUpper() + kv.Key.Substring(1);
                    return $"<span style=\"color:{color}\">{sign}{kv.Value} {label}</span>";
                });
                return $"<p style=\"font-size:11px\">{string.Join(" &nbsp;|&nbsp; ", parts)}</p>";
            }

            /// <summary>Returns true if the player owns at least one of the item and it has effects defined.</summary>
            public static bool CanUseItem(string itemId)
            {
                var inv  = _instance.inventory;
                var defs = _instance.itemDefinitions;
                if (!inv.TryGetValue(itemId, out int qty) || qty <= 0) return false;
                var def = defs.FirstOrDefault(d => d.Id == itemId);
                return def?.Effects != null && def.Effects.Count > 0;
            }

            /// <summary>Unlock a clothing item so it appears in the wardrobe (e.g. after buying it in a shop).</summary>
            public static string UnlockClothing(string clothingId)
            {
                _instance.ownedClothing.Add(clothingId);
                // Only refresh wardrobe if it is already visible — don't force-open it.
                if (_instance.wardrobeOverlay.Visibility == Visibility.Visible)
                    _instance.ShowWardrobe(resetState: false);
                return string.Empty;
            }

            /// <summary>Returns true if the player owns (has unlocked) a clothing item.</summary>
            public static bool HasClothing(string clothingId)
                => _instance.ownedClothing.Contains(clothingId);

            /// <summary>Remove <paramref name="count"/> units of an item at once (e.g. spend currency).</summary>
            public static string SpendItem(string itemId, int count)
            {
                var inv = _instance.inventory;
                if (!inv.ContainsKey(itemId)) return string.Empty;
                inv[itemId] = Math.Max(0, inv[itemId] - count);
                if (inv[itemId] == 0) inv.Remove(itemId);
                _instance.UpdateInventoryPanel();
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
                // Queue a banner notification for the content area
                var def = _instance.questDefinitions.FirstOrDefault(q => q.Id == questId);
                if (def != null)
                {
                    string html = $"<div style=\"background:#182818;border-left:3px solid #4CAF50;padding:8px 12px;margin:0 0 10px;border-radius:0 4px 4px 0;font-size:0.9em;\">" +
                                  $"📋 <strong>New Quest: {System.Net.WebUtility.HtmlEncode(def.Name)}</strong>";
                    if (def.Steps.Count > 0)
                        html += $"<br><span style=\"color:#9e9e9e\">{System.Net.WebUtility.HtmlEncode(def.Steps[0].Description)}</span>";
                    html += "</div>";
                    _instance._pendingQuestNotifications.Add(html);
                }
                return string.Empty;
            }

            public static string AdvanceQuest(string questId)
            {
                int newStep = (_instance.questProgress.TryGetValue(questId, out int s) ? s : 0) + 1;
                _instance.questProgress[questId] = newStep;
                _instance.UpdateJournalPanel();
                // Queue a banner notification for the content area
                var def = _instance.questDefinitions.FirstOrDefault(q => q.Id == questId);
                if (def != null)
                {
                    string html;
                    if (newStep >= def.Steps.Count)
                    {
                        // Quest complete
                        html = $"<div style=\"background:#182818;border-left:3px solid #66BB6A;padding:8px 12px;margin:0 0 10px;border-radius:0 4px 4px 0;font-size:0.9em;\">" +
                               $"✓ <strong>Quest Complete: {System.Net.WebUtility.HtmlEncode(def.Name)}</strong></div>";
                    }
                    else
                    {
                        string nextObj = def.Steps[newStep].Description;
                        html = $"<div style=\"background:#201e14;border-left:3px solid #F5A623;padding:8px 12px;margin:0 0 10px;border-radius:0 4px 4px 0;font-size:0.9em;\">" +
                               $"📋 <strong>New Objective</strong><br><span style=\"color:#9e9e9e\">{System.Net.WebUtility.HtmlEncode(nextObj)}</span></div>";
                    }
                    _instance._pendingQuestNotifications.Add(html);
                }
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
                => Random.Shared.Next(min, max + 1);

            // — Debug —

            // Renders an inline debug block inside the content area when debug mode is on.
            // Usage: {{ debug_log "label" value1 value2 ... }}
            // Values are concatenated with spaces. Returns empty string when debug is off.
            public static string DebugLog(params object[] args)
            {
                if (!_instance.debugEnabled) return string.Empty;
                string msg = string.Join(" ", args.Select(a => a?.ToString() ?? "null"));
                return $"<div style=\"background:#1a1a2e;border-left:3px solid #7c4dff;color:#b388ff;" +
                       $"font-family:monospace;font-size:11px;margin:4px 0;padding:3px 8px;" +
                       $"border-radius:2px\">🐛 {System.Net.WebUtility.HtmlEncode(msg)}</div>";
            }

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
            // Accepts both forward slashes and backslashes as path separators.
            // Videos are shown as a thumbnail image (IE WebBrowser has no HTML5 video).
            public static string InlineMedia(string relativePath)
            {
                // Normalise: trim leading/trailing slashes (both / and \), unify separators to /
                string search = relativePath.Trim('/', '\\').Replace('\\', '/');

                // Search order: mod images first (they can override story images), then story
                var imageRoots = _instance.ActiveMods
                    .Select(m => System.IO.Path.Combine(m.Path, "images"))
                    .Where(Directory.Exists)
                    .ToList();
                imageRoots.Add(_instance.imagesPath);

                while (true)
                {
                    if (!string.IsNullOrEmpty(search))
                    {
                        foreach (var root in imageRoots)
                        {
                            string dir = Path.GetFullPath(Path.Combine(root, search));
                            if (!Directory.Exists(dir)) continue;
                            var files = Directory.GetFiles(dir, "*.*");
                            if (files.Length > 0)
                                return BuildInlineMediaTag(files[new Random().Next(files.Length)]);
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
                string uri     = FileUri(file);
                bool   isVideo = VideoExtensions.Contains(Path.GetExtension(file).ToLower());

                if (!isVideo)
                {
                    // Images: scale to container width, never upscale beyond natural size
                    const string imgStyle =
                        "max-width:100%;width:100%;display:block;margin:8px auto;border-radius:6px;";
                    return $"<img src=\"{uri}\" style=\"{imgStyle}\">";
                }

                // Videos: HTML5 <video> element — requires IE=Edge meta tag (set in ParseContent).
                // autoplay + loop = scene atmosphere; controls = user can pause/seek.
                // width:100% fills the content column; background:#000 letterboxes narrow clips.
                const string vidStyle =
                    "width:100%;display:block;margin:8px auto;border-radius:6px;background:#000;";
                return $"<video src=\"{uri}\" autoplay loop controls " +
                       $"style=\"{vidStyle}\"></video>";
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
