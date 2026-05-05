# ACVN – Adaptive Creative Visual Novel

## Idee

ACVN ist ein raumbasiertes Story-System, inspiriert von QSP (Quest Soft Player), aber moderner und flexibler. Es ist ein Container mit Grundfunktionen für:

- Navigation zwischen Räumen und Aktionen
- Charakter-Attribute mit Min/Max-Clamp
- Persistente Story-Variablen
- Inventar-Verwaltung
- Quest-System mit Fortschritt
- Automatische Medien-Erkennung (Bilder, Videos)

## Ordnerstruktur

```
ACVN/
├── story/
│   ├── chars.json          Charakter-Definitionen (Attribute, Eigenschaften)
│   ├── items.json          Gegenstands-Katalog (id, name, type, starting_quantity)
│   ├── clothes.json        Kleidungs-Katalog (id, subtype, inhibition, tags, …)
│   ├── quests.json         Quest-Definitionen (id, name, steps[])
│   ├── schedules.json      Tagesplan-Definitionen für NPCs
│   ├── style.css           CSS für alle gerenderten Räume
│   ├── images/             Alle Mediendateien (Bilder, Videos) — siehe unten
│   └── rooms/
│       ├── start.acvn      Startbildschirm (Block "start")
│       └── <raum>.acvn     Raumdefinition mit mehreren Blöcken
├── savegames/              Spielstände (*.acvnsave) — zur Laufzeit erstellt
├── Localization.cs         Alle UI-Texte (DE / EN), Loc.T("key") aufrufen
└── appsettings.json        Einstellungen (Lautstärke, Autoplay, Sprache)
```

Unterordner unter `rooms/` und `images/` verwenden Schrägstriche in `.acvn`-Befehlen, aber Unterstriche intern.  
`home_bathroom` → `rooms/home/bathroom.acvn`

---

## Medien-Suchpfade

Alle Mediendateien liegen unter `story/images/`. Je nach Kontext sucht die Engine an verschiedenen Stellen.

### Szenen-Hintergrund (linke Spalte)

Wird angezeigt, wenn eine Aktion geladen wird.

**Muster:** `story/images/<raum>/<aktion>/`

Die Engine verwendet **Hierarchie-Walking**: Wird im exakten Ordner keine Datei gefunden, steigt sie eine Ebene hoch und probiert dort — bis eine Datei gefunden oder die Wurzel erreicht wird.

| Raum | Aktion | Suchreihenfolge |
|------|--------|-----------------|
| `start` | `start` | `story/images/start/` |
| `home_room` | `start` | `story/images/home/room/` → `story/images/home/` |
| `home_bathroom` | `shower` | `story/images/home/bathroom/shower/` → `story/images/home/bathroom/` → `story/images/home/` |

Werden mehrere Dateien gefunden, wird eine zufällig ausgewählt. Das **Debug-Panel** (Settings → aktivieren) zeigt den tatsächlich verwendeten Pfad.

Unterstützte Formate: `.png`, `.jpg`, `.jpeg`, `.webp`, `.gif`, `.bmp`, `.mp4`, `.avi`, `.mkv`, `.wmv`, `.mov`, `.webm`, `.flv`

---

### Charakter-Portraits

Angezeigt im Kontakte-Tab und während Telefonaten.

**Pfad:** `story/images/chars/<charId>.*`

```
story/images/chars/brother.png
story/images/chars/mother.jpg
```

Pro Charakter eine Datei. Erste gefundene Extension gewinnt (`.png` → `.jpg` → `.jpeg` → `.webp` → `.bmp`).

---

### Item-Bilder (Inventar)

Angezeigt im Item-Info-Overlay (Klick auf Gegenstand).

**Pfad:** `story/images/items/<itemId>.*`

```
story/images/items/condom.png
story/images/items/pizza.jpg
```

---

### Kleidungs-Bilder (Garderobe)

Angezeigt in der Garderobe (Hauptansicht, Kategorie-Grid, Detailansicht).

**Primärer Pfad:** Das `image`-Feld in `clothes.json`, relativ zu `story/images/`.

```json
{ "id": "swimsuit", "image": "clothes/swimsuit_special.png", ... }
```

**Fallback (automatisch):** `story/images/clothes/<clothingId>.*`

```
story/images/clothes/bra_white.png
story/images/clothes/outfit_casual.jpg
story/images/clothes/sneakers.webp
```

Das `image`-Feld wird zuerst geprüft. Existiert die Datei, wird sie verwendet. Sonst greift der Fallback über die Kleidungs-`id`.

---

### Inline-Media in Templates

Scriban-Funktion `{{ inline_media "pfad/zum/ordner" }}` bettet eine zufällige Mediendatei als `<img>`-Tag in den Story-HTML ein. Verwendet dieselbe Hierarchie-Walking-Logik wie Szenen-Hintergründe.

```
{{ inline_media "park/bench" }}
```
Sucht: `story/images/park/bench/` → `story/images/park/` → gibt bei Misserfolg leeren String zurück.

---

## Kleidungssystem (`clothes.json`)

| Feld | Typ | Beschreibung |
|------|-----|-------------|
| `id` | string | Eindeutige ID (wird für Bildsuche verwendet) |
| `name` | string | Anzeigename |
| `description` | string | Gezeigt in der Detailansicht |
| `subtype` | string | `bra` \| `panties` \| `clothes` \| `shoes` |
| `durability` | int | 0–100, für zukünftige Verschleißmechanik |
| `daring` | int | 0–100, wie gewagt das Teil ist |
| `inhibition` | int | MC-Hemmung muss ≤ diesem Wert sein (0 = immer tragbar) |
| `tags` | string[] | Abfragbar mit `wearing_has_tag` / `any_clothing_has_tag` |
| `image` | string | Optional: Pfad relativ zu `story/images/` |

**Kleidungszustand:**
- `dressed` — `clothes`-Slot belegt
- `underwear` — nur `bra` und/oder `panties` vorhanden
- `naked` — kein Kleidungsstück getragen

**Automatisches Anziehen beim Spielstart:**  
Die Engine zieht beim Start für jeden Slot (`bra`, `panties`, `clothes`, `shoes`) zufällig ein verfügbares Kleidungsstück an — aber **nur solche mit dem Tag `"basic"`**. Kleidung ohne diesen Tag wird beim Start ignoriert und muss im Spiel aktiv angezogen werden.

---

## Mehrsprachigkeit

Alle UI-Texte leben in `Localization.cs` in der statischen `Loc`-Klasse.

```csharp
Loc.T("tab.status")              // einfacher String
Loc.T("confirm.discard", name)   // mit Formatargument ({0})
```

Die Sprache wird über das Dropdown in den Einstellungen gewählt und in `appsettings.json` gespeichert.

Um eine neue Sprache hinzuzufügen:
1. `Loc.LanguageNames["fr"] = "Français";` eintragen
2. Passendes Dictionary in `Loc._strings["fr"]` anlegen

---

## Template-Syntax (Scriban)

Raum-Dateien werden mit [Scriban](https://github.com/scriban/scriban) gerendert.  
Blöcke mit `{{ ... }}` für Ausgabe, Zuweisungen und Kontrollstrukturen.

### Blöcke

Jeder Block beginnt mit `#begin <name>` und endet mit `#end`:

```
#begin start
Inhalt des Startblocks...
#end

#begin shower
Inhalt der Dusch-Aktion...
#end
```

Der Standardblock beim Betreten eines Raums ist immer `start`.

### Navigation / Aktions-Buttons

```
[[Label, ziel_raum]]               # Raumwechsel (Aktion = start)
[[Label, ziel_raum, aktion]]       # Raumwechsel + Aktion
```

Buttons mit 2 Teilen erscheinen als Raumnavigation (größer), mit 3 Teilen als Aktionsbutton.

---

## Eingebaute Variablen

| Variable    | Typ                | Beschreibung                          |
|-------------|--------------------|---------------------------------------|
| `mc`        | Character          | Hauptcharakter (aus `chars.json`)     |
| `characters`| List\<Character\>  | Alle Charaktere                       |
| `inventory` | ScriptObject       | Inventar: `inventory.phone` = Anzahl  |
| `vars`      | ScriptObject       | Freie persistente Variablen           |

---

## Eingebaute Funktionen

### Zeit

| Aufruf              | Rückgabe | Beschreibung                                  |
|---------------------|----------|-----------------------------------------------|
| `time_str "format"` | string   | Aktuelle Spielzeit formatieren (`"HH:mm"` etc.) |
| `advance_time N`    | *(leer)* | Spieluhr um N Minuten vorspulen               |

```scriban
Es ist {{ time_str "HH:mm" }}.
{{ advance_time 15 }}
Eine Viertelstunde später: {{ time_str "HH:mm" }}.
```

### Charaktere

| Aufruf                      | Rückgabe  | Beschreibung                            |
|-----------------------------|-----------|------------------------------------------|
| `get_character "id"`        | Character | Charakter anhand ID laden               |
| `attr_change "id" "attr" N` | *(leer)*  | Attribut um N verändern (min/max-Clamp) |
| `get_attr "id" "attr"`      | int       | Aktuellen Attributwert lesen            |
| `set_attr "id" "attr" N`    | *(leer)*  | Attribut direkt auf N setzen            |

```scriban
{{ attr_change "mc" "mood" 10 }}
{{ attr_change "mc" "energy" -5 }}
Stimmung: {{ get_attr "mc" "mood" }}
{{ set_attr "mc" "energy" 100 }}
```

### Inventar

Gegenstände werden in `items.json` definiert (id, name, type, starting_quantity).

| Aufruf             | Rückgabe | Beschreibung                              |
|--------------------|----------|-------------------------------------------|
| `add_item "id"`    | *(leer)* | Gegenstand hinzufügen (+1)                |
| `remove_item "id"` | *(leer)* | Gegenstand entfernen (-1)                 |
| `has_item "id"`    | bool     | Ist mindestens 1 Stück vorhanden?         |
| `item_count "id"`  | int      | Anzahl des Gegenstands im Inventar        |

```scriban
{{ add_item "phone" }}
{{ if has_item "phone" }}
<p>Du hast dein Smartphone dabei.</p>
{{ end }}
{{ if (item_count "condoms") > 0 }}
[[Kondom benutzen, home_room, use_condom]]
{{ end }}
```

Das Inventar ist im **Inventar-Tab** (rechtes Panel) sichtbar und wird in Spielständen gespeichert.

### Quests

Quests werden in `story/quests.json` definiert. Jede Quest hat eine ID, einen Namen und eine Liste von Schritten.

| Aufruf                  | Rückgabe | Beschreibung                                        |
|-------------------------|----------|-----------------------------------------------------|
| `start_quest "id"`      | *(leer)* | Quest starten (Schritt 0 aktiv)                     |
| `advance_quest "id"`    | *(leer)* | Zum nächsten Schritt wechseln                       |
| `quest_step "id"`       | int      | Aktueller Schritt-Index (-1 = nicht gestartet)      |
| `quest_active "id"`     | bool     | Quest läuft und ist noch nicht abgeschlossen        |
| `quest_done "id"`       | bool     | Quest abgeschlossen?                                |
| `quest_objective "id"`  | string   | Beschreibung des aktuellen Schritts                 |

```scriban
{{ if (quest_step "tutorial") == -1 }}{{ start_quest "tutorial" }}{{ end }}

{{ if quest_active "tutorial" }}
<p>Aufgabe: {{ quest_objective "tutorial" }}</p>
{{ end }}

{{ if quest_done "tutorial" }}
<p>Tutorial abgeschlossen!</p>
{{ end }}
```

Quest-Fortschritt wird im **Journal-Tab** angezeigt und mit dem Spielstand gespeichert.

**`quests.json` Beispiel:**

```json
{
  "quests": [
    {
      "id": "tutorial",
      "name": "Erste Schritte",
      "steps": [
        { "id": "pickup_phone",    "description": "Nimm das Telefon auf!" },
        { "id": "go_to_bathroom",  "description": "Geh ins Badezimmer!" },
        { "id": "shower_and_brush","description": "Dusche und putz die Zähne!" }
      ]
    }
  ]
}
```

### Freie Variablen (vars)

`vars` ist ein persistentes Objekt für eigene Zustände. Zuweisungen werden mit dem Spielstand gespeichert.

```scriban
{{- if vars.quest_flag == null }}{{ vars.quest_flag = false }}{{ end -}}
{{ vars.quest_flag = true }}
{{ if vars.quest_flag == true }}
<p>Das Flag ist gesetzt.</p>
{{ end }}
```

**Wichtig:** Nur `vars.x = ...` speichert persistent. Bloße `{{ x = 5 }}` leben nur für den aktuellen Render.

---

## Kontrollstrukturen

```scriban
{{ if bedingung }}
  ...
{{ else if andere_bedingung }}
  ...
{{ else }}
  ...
{{ end }}
```

---

## Vollständiges Beispiel – Badezimmer

```scriban
#begin start
{{ if vars.shower_count == null }}{{ vars.shower_count = 0 }}{{ end }}
Du stehst im Badezimmer. Es ist {{ time_str "HH:mm" }}.
[[Flur, home_hallway]]
{{ if vars.shower_count < 5 }}
[[Duschen, home_bathroom, shower]]
{{ else }}
<p>Du hast heute schon {{ vars.shower_count }} mal geduscht.</p>
{{ end }}
#end

#begin shower
Du duschst dich.
{{ vars.shower_count = vars.shower_count + 1 }}
{{ attr_change "mc" "hygiene" 20 }}
{{ attr_change "mc" "energy" 5 }}
{{ advance_time 15 }}
<p>Du fühlst dich frisch. Es ist {{ time_str "HH:mm" }}.</p>
[[Abtrocknen, home_bathroom, dryoff]]
#end
```

---

## Speichern / Laden

- **Quicksave / Quickload**: speichert/lädt in `savegames/quicksave.acvnsave`
- **Save / Load**: Dateidialog, Format `*.acvnsave`
- Spielstände werden mit AES-128 verschlüsselt gespeichert (nicht einfach editierbar)
- Gespeichert werden: Spielzeit, Charakterwerte, aktueller Raum, `vars`, Inventar, Quest-Fortschritt

---

## Charakter-Attribute (`chars.json`)

Sichtbare Attribute erscheinen im Status-Tab. Versteckte (`"hidden": true`) werden nur intern verfolgt.

```json
{
  "id": "mood",
  "name": "Stimmung",
  "value": 50,
  "min": 0,
  "max": 100
}
```
