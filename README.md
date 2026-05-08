# ACVN – Adaptive Creative Visual Novel Engine

A lightweight, room-based visual novel engine for Windows built with WPF and C#.  
Write interactive stories in plain `.acvn` text files — no programming required.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

---

## Features

- **Story packages** — Multiple self-contained stories live side-by-side under `story/`; pick one at launch
- **Mod system** — Extend any story with drop-in mod folders: add rooms, characters, quests, items, images, and CSS without touching the original files
- **Room & action system** — Navigate between rooms and trigger named action blocks
- **Character system** — Attributes (mood, energy, …), properties, and daily NPC schedules
- **Clothing system** — Slots (bra / panties / clothes / shoes), inhibition gate, wardrobe UI
- **Inventory & quests** — Item tracking, quest steps with in-content notification banners, journal tab
- **Automatic media** — Images and videos load by room/action path with hierarchy fallback
- **Daily variables** — Reset automatically on sleep (`register_daily` / `reset_daily`)
- **Save / Load** — AES-128 encrypted save files, quicksave slot
- **Debug panel** — Shows active media path and template errors; writes `game.log`; state persists across restarts

---

## Prerequisites

| Requirement | Version |
|-------------|---------|
| Windows | 10 / 11 |
| [.NET SDK](https://dotnet.microsoft.com/download) | 9.0 or newer |
| Visual Studio | 2022 (Community is free) — or VS Build Tools |

---

## Getting Started

### 1 — Clone the repository

```
git clone https://github.com/narayan24/ACVN.git
cd ACVN
```

### 2 — Build and run

**Option A – Visual Studio**  
Open `ACVN.sln`, press **F5**.

**Option B – Command line**
```
dotnet run
```

On first launch you will see the **story selection screen** listing every story package found under `story/`. Select one and press Play. If only one package exists, selection is skipped.

### 3 — Create your first story package

Create the folder `story/my-story/` and add a `config.json`:

```json
{
  "name": "My Story",
  "version": "1.0",
  "language": "en",
  "adultgame": "no",
  "start_room": "home_room",
  "start_action": "start"
}
```

Then add your room files under `story/my-story/rooms/` and images under `story/my-story/images/`.

### 4 — Write your first room

Create `story/my-story/rooms/home/room.acvn`:

```
#begin start
{{ if (quest_step "tutorial") == -1 }}{{ start_quest "tutorial" }}{{ end }}
Welcome to my room. It is {{ time_str "HH:mm" }}.
[[Sit at the desk, home_room, sitdown]]
#end

#begin sitdown
You sit down and think for a while.
{{ advance_time 15 }}
{{ attr_change "mc" "mood" 5 }}
<p>A quarter hour later it is {{ time_str "HH:mm" }}.</p>
[[Back, home_room]]
#end
```

### 5 — Add images

Drop images into `story/my-story/images/<room>/<action>/`. The engine picks one at random and falls back up the folder tree if none is found:

```
story/my-story/images/home/room/sitdown/desk_morning.png   ← used for the sitdown action
story/my-story/images/home/room/chair.jpg                  ← fallback for any home/room action
```

Supported formats: `.png .jpg .jpeg .webp .gif .bmp .mp4 .avi .mkv .wmv .mov .webm .flv`

---

## Project Layout

```
ACVN/
├── story/
│   ├── demo/                               ← minimal 2-room demo (included)
│   │   ├── config.json
│   │   ├── chars.json
│   │   ├── quests.json
│   │   ├── rooms/
│   │   └── mods/                           ← mod folder (empty by default)
│   ├── daily-challenges/                   ← full example story package
│   │   ├── config.json                     name, version, language, start_room, …
│   │   ├── chars.json                      Character definitions
│   │   ├── items.json                      Item catalogue
│   │   ├── clothes.json                    Clothing catalogue
│   │   ├── quests.json                     Quest definitions
│   │   ├── schedules.json                  NPC daily schedules
│   │   ├── style.css                       CSS for all rendered room content
│   │   ├── images/                         Art assets — gitignored, add your own
│   │   │   ├── chars/                      <charId>.png
│   │   │   ├── items/                      <itemId>.png
│   │   │   └── clothes/                    <clothingId>.png
│   │   ├── rooms/
│   │   │   ├── intro.acvn                  Title / age-check screen
│   │   │   ├── start.acvn                  Post-setup welcome screen
│   │   │   └── home/
│   │   │       ├── room.acvn
│   │   │       ├── bathroom.acvn
│   │   │       └── …
│   │   └── mods/                           ← mod folder (empty by default)
│   │       └── my-addon/                   ← example installed mod
│   │           ├── mod.json
│   │           ├── rooms/
│   │           └── images/
│   └── my-other-story/                     ← additional story package
│       └── …
├── savegames/                              Created at runtime — gitignored
├── appsettings.json                        Volume, language, debug state, mod toggles
├── MainWindow.xaml(.cs)                    Engine UI and logic
├── Localization.cs                         All UI strings (DE / EN)
├── GameTime.cs                             Time model
├── Character.cs                            Character model
├── Models.cs                               Shared data models
└── SaveGameManager.cs                      Save/load logic
```

> **Room ID ↔ file path mapping**  
> Underscores in room IDs become slashes in file paths:  
> `home_bathroom` → `rooms/home/bathroom.acvn` (within the active story package)

---

## `config.json` Reference

| Key | Type | Description |
|-----|------|-------------|
| `name` | string | Display name shown in the story picker |
| `version` | string | Version string (e.g. `"1.0"`) |
| `language` | string | Default language code (`"en"`, `"de"`, …) |
| `adultgame` | `"yes"` / `"no"` | Shows age-verification screen if `"yes"` |
| `start_room` | string | Room to navigate to after character setup |
| `start_action` | string | Action within `start_room` (default: `"start"`) |

---

## Template Syntax (Scriban)

Room files are rendered with [Scriban](https://github.com/scriban/scriban).

### Blocks

```
#begin start
Default content shown when entering the room.
#end

#begin myaction
Content for the "myaction" block.
#end
```

### Navigation buttons

```
[[Label, target_room]]              # Navigate to a room (loads "start" block)
[[Label, target_room, action]]      # Navigate to a room + specific action block
[[Label, target_room, #FF6B6B]]     # Custom button colour (hex, 3rd param starting with #)
```

> **Rule:** action names (3rd parameter) must **not** contain underscores.

### Built-in variables

| Variable | Type | Description |
|----------|------|-------------|
| `mc` | Character | Player character |
| `characters` | List | All characters |
| `inventory` | ScriptObject | `inventory.phone` = item count |
| `vars` | ScriptObject | Free persistent variables — read/write freely, saved with the game |

```scriban
{{ vars.my_flag = true }}
{{ if vars.my_flag }}You set the flag!{{ end }}
```

---

## Built-in Functions

### Time

| Call | Returns | Description |
|------|---------|-------------|
| `time_str "HH:mm"` | string | Formatted current game time |
| `game_hour` | int | Current hour as integer (0–23) |
| `advance_time N` | — | Advance clock by N minutes |
| `advance_to_hour N` | — | Advance to the next occurrence of hour N |

### Characters

| Call | Returns | Description |
|------|---------|-------------|
| `get_character "id"` | Character | Load character by ID |
| `attr_change "id" "attr" N` | — | Change attribute by N (clamped to min/max) |
| `get_attr "id" "attr"` | int | Read attribute value |
| `set_attr "id" "attr" N` | — | Set attribute directly |
| `char_at "id" "room_id"` | bool | Is character currently in this room? |
| `char_location "id"` | string | Current room ID of character (per schedule) |
| `char_activity "id"` | string | Character's current activity string |

### Inventory

| Call | Returns | Description |
|------|---------|-------------|
| `add_item "id"` | — | Add item (+1) |
| `remove_item "id"` | — | Remove item (−1) |
| `has_item "id"` | bool | At least 1 in inventory? |
| `item_count "id"` | int | Count of item in inventory |

### Quests

| Call | Returns | Description |
|------|---------|-------------|
| `start_quest "id"` | — | Start quest (step 0 active); shows new-quest banner |
| `advance_quest "id"` | — | Move to next step; shows objective or completion banner |
| `quest_step "id"` | int | Current step index (−1 = not started) |
| `quest_active "id"` | bool | Running and not complete? |
| `quest_done "id"` | bool | All steps complete? |
| `quest_objective "id"` | string | Current step description text |

Calling `start_quest` or `advance_quest` automatically prepends a styled notification banner to the current scene:

- 📋 **New Quest** — green border, shows quest name and first objective
- 📋 **New Objective** — orange border, shows next step description  
- ✓ **Quest Complete** — bright green border, shows quest name

### Clothing

| Call | Returns | Description |
|------|---------|-------------|
| `clothing_state` | string | `"dressed"` / `"underwear"` / `"naked"` |
| `is_wearing "subtype"` | bool | Is a clothing slot currently filled? (`"bra"`, `"clothes"`, …) |
| `wearing_item "subtype"` | string | Item ID worn in a slot, or empty string |
| `wearing_has_tag "subtype" "tag"` | bool | Does the item worn in this slot have this tag? |
| `any_clothing_has_tag "tag"` | bool | Does any currently worn item have this tag? |

### Randomness

| Call | Returns | Description |
|------|---------|-------------|
| `random_int min max` | int | Random integer between min and max (inclusive) |

### Daily variables

```scriban
{{- register_daily "shower_count" 0 -}}   {{# initialises to 0 if null; resets on sleep #}}
{{ vars.shower_count = vars.shower_count + 1 }}
```

Call `reset_daily` in your sleep action to reset all registered daily vars to their defaults.

### Inline media

```scriban
{{ inline_media "park/bench" }}
```

Embeds a random image from `images/park/bench/` inline in the HTML (walks up the folder hierarchy on miss). Mod image paths are searched first.

---

## Clothing System (`clothes.json`)

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Unique ID — also used for image lookup |
| `name` | string | Display name |
| `description` | string | Shown in detail view |
| `subtype` | string | `bra` / `panties` / `clothes` / `shoes` |
| `durability` | int | 0–100 (future wear mechanics) |
| `daring` | int | 0–100 |
| `inhibition` | int | MC inhibition must be ≤ this to equip (0 = always wearable) |
| `tags` | string[] | Queryable via `wearing_has_tag` / `any_clothing_has_tag` |
| `image` | string | Optional path relative to `images/` |

Items tagged `"basic"` are auto-equipped at game start (one per slot).

---

## Character Attributes (`chars.json`)

```json
{
  "id": "mood",
  "name": "Mood",
  "value": 50,
  "min": 0,
  "max": 100,
  "hidden": false
}
```

Set `"hidden": true` to track an attribute internally without showing it in the Status tab  
(it can be revealed via the *Show hidden attributes* toggle in Settings).

---

## NPC Schedules (`schedules.json`)

```json
{
  "char_id": "brother",
  "entries": [
    { "days": "weekdays", "from": "22:00", "to": "06:00",
      "location": "home_brother", "activity": "sleeping" },
    { "days": "weekends", "from": "12:00", "to": "14:00",
      "location": "home_livingroom", "activity": "playing video games" }
  ]
}
```

`days` accepts `"weekdays"`, `"weekends"`, or a weekday number (`"1"` = Monday … `"7"` = Sunday).  
`location` can be a single room ID string or an array of IDs (engine picks one at random).

---

## Media Search Paths

### Scene background

Pattern: `images/<room>/<action>/` — walks up on miss. Mod image folders are searched before story images.

| Room | Action | Search order |
|------|--------|--------------|
| `home_room` | `start` | `images/home/room/` → `images/home/` |
| `home_bathroom` | `shower` | `images/home/bathroom/shower/` → `images/home/bathroom/` → `images/home/` |

When a fallback is used, the debug panel shows `Media fallback [requested] → [actual]: file.png` and the event is written to `game.log`.

### Other image paths

| Asset | Path |
|-------|------|
| Character portrait | `images/chars/<charId>.*` |
| Item image | `images/items/<itemId>.*` |
| Clothing image | `images/clothes/<clothingId>.*` (or `image` field in `clothes.json`) |

---

## Mod System

Mods extend a story non-destructively. Drop a folder inside the story's `mods/` directory — no changes to the original story files are needed.

### Folder structure

```
story/my-story/
└── mods/
    └── my-addon/
        ├── mod.json          ← required
        ├── rooms/            ← new or patched .acvn files
        ├── images/           ← additional / replacement media
        ├── chars.json        ← additional characters
        ├── quests.json       ← additional quests
        ├── items.json        ← additional items
        ├── clothes.json      ← additional clothing
        └── style.css         ← appended to story CSS
```

### `mod.json`

```json
{
  "name":        "My Addon",
  "version":     "1.0",
  "author":      "Your Name",
  "description": "Adds a new location and two quests.",
  "priority":    10
}
```

Lower `priority` number = loaded first. When priorities are equal, alphabetical order decides. Default is `50`.

### Enabling / disabling mods

Open the **Game Settings tab** (⚙ icon in the right panel tab bar). A *Mods* section lists every detected mod with an on/off checkbox. Changes are saved automatically and applied on the next game restart (click *Restart game* in the settings flyout, or restart the application).

### Data file merging

`chars.json`, `quests.json`, `items.json`, and `clothes.json` are merged additively:

- **New ID** → entry is appended to the story data
- **Same ID as story** → mod entry replaces the story entry (override)

Mods are merged in priority order; a mod with `priority: 10` overrides one with `priority: 50` if both define the same ID.

### Room block merging

If a mod places a `.acvn` file in `rooms/` with the same name as a story room, its blocks are merged into that room:

| Mod block name | Effect |
|----------------|--------|
| `#begin newaction` | New action added to the room |
| `#begin start` | Completely replaces the story's `start` block |
| `#begin start:before` | Content injected *before* the story's `start` block |
| `#begin start:after` | Content injected *after* the story's `start` block |

Example — a mod adding a new choice to the existing `walk` action without overwriting it:

```
#begin walk:after
{{ attr_change "mc" "mood" 5 }}
[[Head to the river, meadow, river]]
#end
```

### Images

A mod's `images/` folder is searched **before** the story's images, so mods can replace any existing artwork. The same hierarchy-fallback logic applies within mod image folders.

### CSS

A mod's `style.css` is **appended** to the story's stylesheet (in priority order), so mods can override any story style or add new rules.

---

## Save / Load

- **Quicksave / Quickload** → `savegames/quicksave.acvnsave`
- **Save / Load** → file dialog, `*.acvnsave`
- Files are AES-128 encrypted
- Saved state: game time, character values, current room, `vars`, inventory, quest progress, worn clothing

---

## Localisation

All UI strings live in `Localization.cs`:

```csharp
Loc.T("tab.status")              // simple lookup
Loc.T("confirm.discard", name)   // with format argument
```

To add a language:
1. Add `Loc.LanguageNames["fr"] = "Français";`
2. Add a matching dictionary in `Loc._strings["fr"]`

---

## Debugging

Enable the debug panel via the **Settings flyout** (⚙ gear button, bottom-right toolbar) → *Enable debug output*.  
The setting persists across restarts.

The debug panel shows:
- Active media path after every navigation (including fallbacks)
- Template parse and render errors

All media fallbacks, missing-media events, and template errors are also written to `game.log` in the project root.

---

## Contributing

Pull requests are welcome! Please:

1. Fork the repo and create a feature branch (`git checkout -b feature/my-feature`)
2. Keep C# code style consistent with the existing file
3. Test your changes with the included `demo` and `daily-challenges` story packages
4. Open a PR with a clear description of what changed and why

For larger changes, open an issue first to discuss the approach.

---

## License

This project is licensed under the **MIT License** — see [LICENSE](LICENSE) for details.  
You are free to use ACVN as the engine for your own stories, including commercial ones.
