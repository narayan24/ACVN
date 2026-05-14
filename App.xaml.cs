using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace ACVN
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ApplyThemeFromSettings();
        }

        /// <summary>Read the saved theme setting and apply the matching ResourceDictionary.</summary>
        internal static void ApplyThemeFromSettings()
        {
            string settingsPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

            string savedTheme = "System";
            try
            {
                if (File.Exists(settingsPath))
                {
                    dynamic s = JsonConvert.DeserializeObject<dynamic>(
                        File.ReadAllText(settingsPath));
                    string t = s?.Theme?.ToString();
                    if (!string.IsNullOrEmpty(t)) savedTheme = t;
                }
            }
            catch { }

            LoadThemeDict(ResolveThemeName(savedTheme));
        }

        /// <summary>Resolve "System" to "Dark" or "Light" based on Windows registry.</summary>
        internal static string ResolveThemeName(string theme)
        {
            if (theme == "System")
                return IsSystemLightTheme() ? "Light" : "Dark";
            return theme;
        }

        /// <summary>Returns true when Windows Apps use the light theme.</summary>
        internal static bool IsSystemLightTheme()
        {
            try
            {
                object val = Registry.GetValue(
                    @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1);
                return val is int i && i == 1;
            }
            catch { return true; }
        }

        /// <summary>Swap the active theme ResourceDictionary at runtime.</summary>
        internal static void LoadThemeDict(string themeName)
        {
            var dict = new ResourceDictionary
            {
                Source = new Uri($"Themes/{themeName}.xaml", UriKind.Relative)
            };
            Current.Resources.MergedDictionaries.Clear();
            Current.Resources.MergedDictionaries.Add(dict);
        }
    }
}
