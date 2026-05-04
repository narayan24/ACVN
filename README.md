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
story/
├── chars.json          Charakter-Definitionen (Attribute, Eigenschaften)
├── items.json          Gegenstands-Katalog (id, name, type, starting_quantity)
├── quests.json         Quest-Definitionen (id, name, steps[])
├── style.css           CSS für alle gerenderten Räume
├── images/             Mediendateien (Bilder, Videos)
│   └── <raum>/
│       └── <aktion>/   Medien, die bei dieser Aktion angezeigt werden
└── rooms/
    ├── start.acvn      Startbildschirm (Block "start")
    └── <raum>/
        └── <raum>.acvn Raumdefinition mit mehreren Blöcken
```

Unterordner unter `rooms/` und `images/` verwenden Schrägstriche in `.acvn`-Befehlen, aber Unterstriche intern.  
`home_bathroom` → `rooms/home/bathroom.acvn`

## Medien-Lookup

Bei jeder Aktion sucht die Engine nach Medien in:

```
story/images/<raum>/<aktion>/
```

| Aktueller Raum | Aktion  | Gesuchter Ordner                       |
|----------------|---------|----------------------------------------|
| `start`        | `start` | `story/images/start/`                  |
| `home_room`    | `start` | `story/images/home/room/`              |
| `home_bathroom`| `shower`| `story/images/home/bathroom/shower/`   |

Eine zufällige Datei aus dem Ordner wird angezeigt. Fehlt der Ordner, wird das Medienfenster ausgeblendet.  
Das **Debug-Panel** am unteren Rand zeigt Pfade und Fehler (aktivierbar im Settings-Menü).

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
