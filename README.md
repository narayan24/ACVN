# ACVN – Adaptive Creative Visual Novel

## Concept

ACVN is a room-based story system inspired by QSP (Quest Soft Player), but more modern and flexible. It is a container providing core features for:

- Navigation between rooms and actions
- Character attributes with min/max clamping
- Persistent story variables
- Inventory management
- Quest system with progress tracking
- Automatic media detection (images, videos)

## Folder structure

```
ACVN/
├── story/
│   ├── chars.json          Character definitions (attributes, properties)
│   ├── items.json          Item catalogue (id, name, type, starting_quantity)
│   ├── clothes.json        Clothing catalogue (id, subtype, inhibition, tags, …)
│   ├── quests.json         Quest definitions (id, name, steps[])
│   ├── config.json         Game-level configuration (adultgame, start_room, …)
│   ├── schedules.json      Daily schedule definitions for NPCs
│   ├── style.css           CSS for all rendered rooms
│   ├── images/             All media files (images, videos) — see below
│   └── rooms/
│       ├── intro.acvn      Intro / age-check screen
│       ├── start.acvn      Game start screen (block "start")
│       └── <room>.acvn     Room definition with multiple blocks
├── savegames/              Save files (*.acvnsave) — created at runtime
├── Localization.cs         All UI texts (DE / EN), call via Loc.T("key")
└── appsettings.json        Settings (volume, autoplay, language)
```

Subfolders under `rooms/` and `images/` use slashes in `.acvn` commands but underscores internally.  
`home_bathroom` → `rooms/home/bathroom.acvn`

---

## Game configuration (`config.json`)

| Key | Values | Description |
|-----|--------|-------------|
| `adultgame` | `"yes"` / `"no"` | Shows age-verification screen before setup if `"yes"` |
| `start_room` | room id | Room to navigate to after character setup |
| `start_action` | action name | Action within start_room (default: `"start"`) |

---

## Media search paths

All media files live under `story/images/`. The engine searches in different locations depending on context.

### Scene background (left column)

Displayed when an action is loaded.

**Pattern:** `story/images/<room>/<action>/`

The engine uses **hierarchy walking**: if no file is found in the exact folder, it moves one level up and tries there — until a file is found or the root is reached.

| Room | Action | Search order |
|------|--------|--------------|
| `start` | `start` | `story/images/start/` |
| `home_room` | `start` | `story/images/home/room/` → `story/images/home/` |
| `home_bathroom` | `shower` | `story/images/home/bathroom/shower/` → `story/images/home/bathroom/` → `story/images/home/` |

If multiple files are found, one is chosen at random. The **debug panel** (Settings → enable) shows the path actually used.

Supported formats: `.png`, `.jpg`, `.jpeg`, `.webp`, `.gif`, `.bmp`, `.mp4`, `.avi`, `.mkv`, `.wmv`, `.mov`, `.webm`, `.flv`

---

### Character portraits

Shown in the Contacts tab and during phone calls.

**Path:** `story/images/chars/<charId>.*`

```
story/images/chars/brother.png
story/images/chars/mother.jpg
```

One file per character. First matching extension wins (`.png` → `.jpg` → `.jpeg` → `.webp` → `.bmp`).

---

### Item images (inventory)

Shown in the item info overlay (click on an item).

**Path:** `story/images/items/<itemId>.*`

```
story/images/items/condom.png
story/images/items/pizza.jpg
```

---

### Clothing images (wardrobe)

Shown in the wardrobe (main view, category grid, detail view).

**Primary path:** The `image` field in `clothes.json`, relative to `story/images/`.

```json
{ "id": "swimsuit", "image": "clothes/swimsuit_special.png", ... }
```

**Fallback (automatic):** `story/images/clothes/<clothingId>.*`

```
story/images/clothes/bra_white.png
story/images/clothes/outfit_casual.jpg
story/images/clothes/sneakers.webp
```

The `image` field is checked first. If the file exists, it is used. Otherwise the fallback via the clothing `id` applies.

---

### Inline media in templates

Scriban function `{{ inline_media "path/to/folder" }}` embeds a random media file as an `<img>` tag in the story HTML. Uses the same hierarchy-walking logic as scene backgrounds.

```
{{ inline_media "park/bench" }}
```
Searches: `story/images/park/bench/` → `story/images/park/` → returns empty string on failure.

---

## Clothing system (`clothes.json`)

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Unique ID (used for image lookup) |
| `name` | string | Display name |
| `description` | string | Shown in the detail view |
| `subtype` | string | `bra` \| `panties` \| `clothes` \| `shoes` |
| `durability` | int | 0–100, for future wear mechanics |
| `daring` | int | 0–100, how daring the item is |
| `inhibition` | int | MC's inhibition must be ≤ this value (0 = always wearable) |
| `tags` | string[] | Queryable with `wearing_has_tag` / `any_clothing_has_tag` |
| `image` | string | Optional: path relative to `story/images/` |

**Clothing state:**
- `dressed` — `clothes` slot occupied
- `underwear` — only `bra` and/or `panties` worn
- `naked` — no clothing worn

**Auto-equip on game start:**  
The engine randomly equips one available item per slot (`bra`, `panties`, `clothes`, `shoes`) at startup — but **only items tagged `"basic"`**. Clothing without this tag is ignored at startup and must be put on manually during the game.

---

## Localisation

All UI texts live in `Localization.cs` in the static `Loc` class.

```csharp
Loc.T("tab.status")              // simple string
Loc.T("confirm.discard", name)   // with format argument ({0})
```

The language is selected via the dropdown in the settings flyout and saved to `appsettings.json`.

To add a new language:
1. Add `Loc.LanguageNames["fr"] = "Français";`
2. Create a matching dictionary in `Loc._strings["fr"]`

---

## Template syntax (Scriban)

Room files are rendered with [Scriban](https://github.com/scriban/scriban).  
Use `{{ ... }}` for output, assignments, and control structures.

### Blocks

Every block starts with `#begin <name>` and ends with `#end`:

```
#begin start
Content of the start block...
#end

#begin shower
Content of the shower action...
#end
```

The default block when entering a room is always `start`.

### Navigation / action buttons

```
[[Label, target_room]]               # room change (action = start)
[[Label, target_room, action]]       # room change + action
[[Label, target_room, #hexcolor]]    # room change with custom button colour
```

Buttons with 2 parts appear as room navigation (larger); 3 parts as action buttons.

---

## Built-in variables

| Variable    | Type               | Description                            |
|-------------|--------------------|----------------------------------------|
| `mc`        | Character          | Player character (from `chars.json`)   |
| `characters`| List\<Character\>  | All characters                         |
| `inventory` | ScriptObject       | Inventory: `inventory.phone` = count   |
| `vars`      | ScriptObject       | Free persistent variables              |

---

## Built-in functions

### Time

| Call                | Returns  | Description                                    |
|---------------------|----------|------------------------------------------------|
| `time_str "format"` | string   | Format current game time (`"HH:mm"` etc.)      |
| `advance_time N`    | *(empty)*| Advance game clock by N minutes                |

```scriban
It is {{ time_str "HH:mm" }}.
{{ advance_time 15 }}
A quarter hour later: {{ time_str "HH:mm" }}.
```

### Characters

| Call                        | Returns   | Description                              |
|-----------------------------|-----------|------------------------------------------|
| `get_character "id"`        | Character | Load character by ID                     |
| `attr_change "id" "attr" N` | *(empty)* | Change attribute by N (min/max clamped)  |
| `get_attr "id" "attr"`      | int       | Read current attribute value             |
| `set_attr "id" "attr" N`    | *(empty)* | Set attribute directly to N             |

```scriban
{{ attr_change "mc" "mood" 10 }}
{{ attr_change "mc" "energy" -5 }}
Mood: {{ get_attr "mc" "mood" }}
{{ set_attr "mc" "energy" 100 }}
```

### Inventory

Items are defined in `items.json` (id, name, type, starting_quantity).

| Call               | Returns  | Description                              |
|--------------------|----------|------------------------------------------|
| `add_item "id"`    | *(empty)*| Add item (+1)                            |
| `remove_item "id"` | *(empty)*| Remove item (-1)                         |
| `has_item "id"`    | bool     | Is at least 1 in inventory?             |
| `item_count "id"`  | int      | Number of that item in inventory         |

```scriban
{{ add_item "phone" }}
{{ if has_item "phone" }}
<p>You have your smartphone with you.</p>
{{ end }}
{{ if (item_count "condom") > 0 }}
[[Use condom, home_room, use_condom]]
{{ end }}
```

The inventory is visible in the **Inventory tab** (right panel) and saved with the game state.

### Quests

Quests are defined in `story/quests.json`. Each quest has an ID, a name, and a list of steps.

| Call                    | Returns  | Description                                          |
|-------------------------|----------|------------------------------------------------------|
| `start_quest "id"`      | *(empty)*| Start quest (step 0 active)                          |
| `advance_quest "id"`    | *(empty)*| Move to the next step                                |
| `quest_step "id"`       | int      | Current step index (-1 = not started)                |
| `quest_active "id"`     | bool     | Quest running and not yet complete                   |
| `quest_done "id"`       | bool     | Quest complete?                                      |
| `quest_objective "id"`  | string   | Description of the current step                      |

```scriban
{{ if (quest_step "tutorial") == -1 }}{{ start_quest "tutorial" }}{{ end }}

{{ if quest_active "tutorial" }}
<p>Objective: {{ quest_objective "tutorial" }}</p>
{{ end }}

{{ if quest_done "tutorial" }}
<p>Tutorial complete!</p>
{{ end }}
```

Quest progress is shown in the **Journal tab** and saved with the game state.

**`quests.json` example:**

```json
{
  "quests": [
    {
      "id": "tutorial",
      "name": "First Steps",
      "steps": [
        { "id": "pickup_phone",    "description": "Pick up the phone!" },
        { "id": "go_to_bathroom",  "description": "Go to the bathroom!" },
        { "id": "shower_and_brush","description": "Shower and brush your teeth!" }
      ]
    }
  ]
}
```

### Free variables (vars)

`vars` is a persistent object for custom state. Assignments are saved with the game state.

```scriban
{{- if vars.quest_flag == null }}{{ vars.quest_flag = false }}{{ end -}}
{{ vars.quest_flag = true }}
{{ if vars.quest_flag == true }}
<p>The flag is set.</p>
{{ end }}
```

**Important:** Only `vars.x = ...` persists. A bare `{{ x = 5 }}` only lives for the current render.

### Daily variables

Variables that reset automatically when the player sleeps. Use `register_daily` instead of manual null-checks.

| Call                           | Returns  | Description                                        |
|--------------------------------|----------|----------------------------------------------------|
| `register_daily "name" default`| *(empty)*| Register as daily var and initialise if null       |
| `reset_daily`                  | *(empty)*| Reset all registered daily vars to their defaults  |

```scriban
{{- register_daily "shower_count" 0 -}}

{{ if vars.shower_count < 5 }}
[[Shower, home_bathroom, shower]]
{{ end }}
```

Call `reset_daily` in the `sleep` action to reset all daily vars at once.

---

## Control structures

```scriban
{{ if condition }}
  ...
{{ else if other_condition }}
  ...
{{ else }}
  ...
{{ end }}
```

---

## Full example – Bathroom

```scriban
#begin start
{{- register_daily "shower_count" 0 -}}
You are in the bathroom. It is {{ time_str "HH:mm" }}.
[[Hallway, home_hallway]]
{{ if vars.shower_count < 5 }}
[[Shower, home_bathroom, shower]]
{{ else }}
<p>You've already showered {{ vars.shower_count }} times today.</p>
{{ end }}
#end

#begin shower
You take a shower.
{{ vars.shower_count = vars.shower_count + 1 }}
{{ attr_change "mc" "hygiene" 20 }}
{{ attr_change "mc" "energy" 5 }}
{{ advance_time 15 }}
<p>You feel fresh. It's now {{ time_str "HH:mm" }}.</p>
[[Dry off, home_bathroom, dryoff]]
#end
```

---

## Save / Load

- **Quicksave / Quickload**: saves/loads to `savegames/quicksave.acvnsave`
- **Save / Load**: file dialog, format `*.acvnsave`
- Save files are AES-128 encrypted (not easily editable)
- Saved: game time, character values, current room, `vars`, inventory, quest progress

---

## Character attributes (`chars.json`)

Visible attributes appear in the Status tab. Hidden ones (`"hidden": true`) are tracked internally only.

```json
{
  "id": "mood",
  "name": "Mood",
  "value": 50,
  "min": 0,
  "max": 100
}
```
