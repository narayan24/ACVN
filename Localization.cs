using System;
using System.Collections.Generic;

public static class Loc
{
    private static string _lang = "de";
    public static string CurrentLanguage => _lang;
    public static event Action LanguageChanged;

    public static readonly Dictionary<string, string> LanguageNames = new()
    {
        ["de"] = "Deutsch",
        ["en"] = "English"
    };

    public static void SetLanguage(string lang)
    {
        if (!_strings.ContainsKey(lang)) return;
        _lang = lang;
        LanguageChanged?.Invoke();
    }

    public static string T(string key)
    {
        if (_strings.TryGetValue(_lang, out var d) && d.TryGetValue(key, out var v)) return v;
        if (_strings.TryGetValue("de", out var de) && de.TryGetValue(key, out var dv)) return dv;
        return key;
    }

    // Format variant: T("key", arg0, arg1, …)
    public static string T(string key, params object[] args)
    {
        try { return string.Format(T(key), args); }
        catch { return T(key); }
    }

    private static readonly Dictionary<string, Dictionary<string, string>> _strings = new()
    {
        ["de"] = new Dictionary<string, string>
        {
            // Tabs
            ["tab.status"]        = "Status",
            ["tab.phone"]         = "Telefon",
            ["tab.inventory"]     = "Inventar",
            ["tab.journal"]       = "Journal",
            ["tab.contacts"]      = "Kontakte",
            ["tab.settings"]      = "Einstellungen",

            // Toolbar
            ["btn.quicksave"]     = "Quick Save",
            ["btn.quickload"]     = "Quick Load",
            ["btn.save"]          = "Speichern",
            ["btn.load"]          = "Laden",

            // Settings
            ["settings.title"]         = "Einstellungen",
            ["settings.debug"]         = "Debug-Ausgabe aktivieren",
            ["settings.autoplay"]      = "Videos automatisch abspielen",
            ["settings.volume"]        = "Lautstärke",
            ["settings.restart"]       = "Spiel neu starten",
            ["settings.backscene"]     = "Vorherige Szene",
            ["settings.language"]      = "Sprache",
            ["settings.display"]       = "Anzeige",
            ["settings.show_hidden"]   = "Versteckte Attribute anzeigen",
            ["settings.mods"]              = "MODS",
            ["settings.mods.none"]         = "Keine Mods gefunden. Lege Mod-Ordner unter mods/ ab.",
            ["settings.mods.restart_hint"] = "Änderungen werden beim nächsten Neustart übernommen.",

            // Wardrobe
            ["wardrobe.title"]       = "Kleiderschrank",
            ["wardrobe.status"]      = "Status",
            ["wardrobe.dressed"]     = "Angezogen",
            ["wardrobe.underwear"]   = "Unterwäsche",
            ["wardrobe.naked"]       = "Nackt",
            ["wardrobe.wear"]        = "Anziehen",
            ["wardrobe.unwear"]      = "Ausziehen",
            ["wardrobe.discard"]     = "Wegwerfen",
            ["wardrobe.back"]        = "← Zurück",
            ["wardrobe.worn"]        = "Getragen",
            ["wardrobe.no_items"]    = "Keine Kleidungsstücke vorhanden.",
            ["wardrobe.tags"]        = "Tags",
            ["wardrobe.inhib_hint"]  = "Inhibition zu hoch (Benötigt: ≤ {0})",
            ["wardrobe.bra"]         = "BH",
            ["wardrobe.panties"]     = "Unterhose",
            ["wardrobe.clothes"]     = "Kleidung",
            ["wardrobe.shoes"]       = "Schuhe",
            ["wardrobe.undress_all"] = "Alles ausziehen",
            ["wardrobe.outfits"]     = "Gespeicherte Outfits",
            ["wardrobe.save_outfit"] = "Speichern",
            ["wardrobe.load_outfit"] = "Laden",

            // Inventory
            ["inv.clothing"]     = "Kleidung",
            ["inv.items"]        = "Items",
            ["inv.empty"]        = "–  Keine Einträge",

            // Journal
            ["journal.active"]   = "Aktive Quests",
            ["journal.open"]     = "Offene Quests",
            ["journal.done"]     = "Beendete Quests",
            ["journal.empty"]    = "–  Keine Einträge",

            // Media modal
            ["media.close_hint"] = "✕  Klicken zum Schließen",

            // Relationships
            ["rel.no_contacts"]  = "Keine Kontakte.",
            ["rel.years"]        = "Jahre",

            // Phone
            ["phone.telefon"]    = "Telefon",
            ["phone.nachrichten"]= "Nachrichten",
            ["phone.kamera"]     = "Kamera",
            ["phone.medien"]     = "Medien",
            ["phone.instagram"]  = "Instagram",
            ["phone.no_image"]   = "Kein Bild verfügbar",
            ["phone.no_media"]   = "Keine Medien gespeichert.",
            ["phone.close"]      = "Schließen",
            ["phone.connecting"] = "Wird verbunden…",
            ["phone.unavailable"]= "ist gerade nicht erreichbar.",
            ["phone.no_sms"]     = "Ich habe aktuell keinen Grund,\ndieser Person eine Nachricht zu schreiben.",

            // Age check
            ["age.denied_title"] = "Altersbeschränkung",
            ["age.denied_msg"]   = "Dieses Spiel ist nur für Personen ab 18 Jahren zugänglich.\nDas Programm wird jetzt beendet.",

            // Confirmations
            ["confirm.discard"]        = "\"{0}\" wirklich wegwerfen?",
            ["confirm.restart"]        = "Bist du sicher? Nicht gespeicherter Fortschritt geht verloren!",
            ["confirm.restart.title"]  = "Spiel neustarten",
            ["confirm.discard.title"]  = "Wegwerfen",
            ["confirm.save.done"]      = "Spielstand gespeichert.",
            ["confirm.quicksave.done"] = "Quicksave gespeichert.",
            ["confirm.noQuicksave"]    = "Kein Quicksave gefunden.",

            // Setup screen
            ["setup.title"]           = "Spieler einrichten",
            ["setup.subtitle"]        = "Lege Namen, Alter und Beziehungen der Figuren fest.",
            ["setup.mc"]              = "Spielfigur",
            ["setup.firstname"]       = "Vorname",
            ["setup.lastname"]        = "Nachname",
            ["setup.nickname"]        = "Spitzname (optional)",
            ["setup.age"]             = "Alter",
            ["setup.brother"]         = "Bruder / Mitbewohner",
            ["setup.mother"]          = "Mutter / Vermieterin",
            ["setup.father"]          = "Vater / Vermieter",
            ["setup.relation"]        = "Beziehung zur Spielfigur",
            ["setup.relation_reverse"]= "Beziehung der Spielfigur",
            ["setup.continue"]        = "Weiter",
        },

        ["en"] = new Dictionary<string, string>
        {
            // Tabs
            ["tab.status"]        = "Status",
            ["tab.phone"]         = "Phone",
            ["tab.inventory"]     = "Inventory",
            ["tab.journal"]       = "Journal",
            ["tab.contacts"]      = "Contacts",
            ["tab.settings"]      = "Settings",

            // Toolbar
            ["btn.quicksave"]     = "Quick Save",
            ["btn.quickload"]     = "Quick Load",
            ["btn.save"]          = "Save",
            ["btn.load"]          = "Load",

            // Settings
            ["settings.title"]         = "Settings",
            ["settings.debug"]         = "Enable debug output",
            ["settings.autoplay"]      = "Auto-play videos",
            ["settings.volume"]        = "Volume",
            ["settings.restart"]       = "Restart game",
            ["settings.backscene"]     = "Previous scene",
            ["settings.language"]      = "Language",
            ["settings.display"]       = "Display",
            ["settings.show_hidden"]   = "Show hidden attributes",
            ["settings.mods"]              = "MODS",
            ["settings.mods.none"]         = "No mods found. Place mod folders inside mods/.",
            ["settings.mods.restart_hint"] = "Changes take effect on the next restart.",

            // Wardrobe
            ["wardrobe.title"]       = "Wardrobe",
            ["wardrobe.status"]      = "Status",
            ["wardrobe.dressed"]     = "Dressed",
            ["wardrobe.underwear"]   = "Underwear",
            ["wardrobe.naked"]       = "Naked",
            ["wardrobe.wear"]        = "Wear",
            ["wardrobe.unwear"]      = "Take off",
            ["wardrobe.discard"]     = "Discard",
            ["wardrobe.back"]        = "← Back",
            ["wardrobe.worn"]        = "Worn",
            ["wardrobe.no_items"]    = "No clothing items available.",
            ["wardrobe.tags"]        = "Tags",
            ["wardrobe.inhib_hint"]  = "Inhibition too high (Required: ≤ {0})",
            ["wardrobe.bra"]         = "Bra",
            ["wardrobe.panties"]     = "Panties",
            ["wardrobe.clothes"]     = "Clothes",
            ["wardrobe.shoes"]       = "Shoes",
            ["wardrobe.undress_all"] = "Undress all",
            ["wardrobe.outfits"]     = "Saved Outfits",
            ["wardrobe.save_outfit"] = "Save",
            ["wardrobe.load_outfit"] = "Load",

            // Inventory
            ["inv.clothing"]     = "Clothing",
            ["inv.items"]        = "Items",
            ["inv.empty"]        = "–  No entries",

            // Journal
            ["journal.active"]   = "Active Quests",
            ["journal.open"]     = "Open Quests",
            ["journal.done"]     = "Completed Quests",
            ["journal.empty"]    = "–  No entries",

            // Media modal
            ["media.close_hint"] = "✕  Click to close",

            // Relationships
            ["rel.no_contacts"]  = "No contacts.",
            ["rel.years"]        = "years old",

            // Phone
            ["phone.telefon"]    = "Phone",
            ["phone.nachrichten"]= "Messages",
            ["phone.kamera"]     = "Camera",
            ["phone.medien"]     = "Media",
            ["phone.instagram"]  = "Instagram",
            ["phone.no_image"]   = "No image available",
            ["phone.no_media"]   = "No media saved.",
            ["phone.close"]      = "Close",
            ["phone.connecting"] = "Connecting…",
            ["phone.unavailable"]= "is currently unavailable.",
            ["phone.no_sms"]     = "I have no reason to message\nthis person right now.",

            // Age check
            ["age.denied_title"] = "Age Restriction",
            ["age.denied_msg"]   = "This game is only accessible to persons 18 years of age or older.\nThe application will now close.",

            // Confirmations
            ["confirm.discard"]        = "Really discard \"{0}\"?",
            ["confirm.restart"]        = "Are you sure? Unsaved progress will be lost!",
            ["confirm.restart.title"]  = "Restart Game",
            ["confirm.discard.title"]  = "Discard",
            ["confirm.save.done"]      = "Game saved.",
            ["confirm.quicksave.done"] = "Quick save saved.",
            ["confirm.noQuicksave"]    = "No quick save found.",

            // Setup screen
            ["setup.title"]           = "Character Setup",
            ["setup.subtitle"]        = "Configure names, ages and relationships.",
            ["setup.mc"]              = "Player Character",
            ["setup.firstname"]       = "First Name",
            ["setup.lastname"]        = "Last Name",
            ["setup.nickname"]        = "Nickname (optional)",
            ["setup.age"]             = "Age",
            ["setup.brother"]         = "Brother / Flatmate",
            ["setup.mother"]          = "Mother / Landlady",
            ["setup.father"]          = "Father / Landlord",
            ["setup.relation"]        = "Relationship to player",
            ["setup.relation_reverse"]= "Player's relationship",
            ["setup.continue"]        = "Continue",
        }
    };
}
