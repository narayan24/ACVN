# ACVN

## The idea
The idea comes from the QSP project. It's basically some kind of room based quest system. While some interessting projects where created with it it's still quite rough to handle and the documentation ist mostly in russian.

So there should be some quite similar solution but with a more modular and more flexible base. Therefore ACVN was created.

It is mainly a container with basic funtionality for moving from room to room, handling object properties (inventory system) und character actions.
It should be easy to handle (i.e. when there is text written - it should simply be shown in the main window. But where QSP left you alone with media handling, ACVN should just receive a source where to look at and should then randomly show the selected media no matter if it is a picture, movie or soundfile.

## Quickstart
* The main content folder is the "story" folder.
* Room definitions are placed in the subfolder "room" and have the acvn extension. While they are basically textfiles for now, at a later point of development they may be encrypted to prevent cheating.
* The "images" folder contains all related media (will maybe renamed to "media" - will make more sense). The folders have to match the room names and furthermore the in the .acvn files defined actions.
* Actions are defined in BB-Code style bei two surrounding square brackets (i.e. `[[Action name, room, action, params]]`)

## Template syntax
Room files are rendered with [Scriban](https://github.com/scriban/scriban).
You can use `{{ ... }}` blocks for variable assignments, value output and
conditions. The following variables are available inside each template block:

* `mc`: the main character object (same structure as in `chars.json`)
* `characters`: the full character list
* `gameTime`: a `DateTime` instance representing the current in-game time
* `get_character(id)`: helper function to fetch a character by its id

Example usage:

```scriban
{{ number_of_showers = number_of_showers | default 0 }}
{{ if number_of_showers < 5 }}
  {{ number_of_showers = number_of_showers + 1 }}
  [[Shower, home_bathroom, shower]]
{{ end }}

<p>The current time is {{ gameTime | date.to_string "dddd, dd. MMMM yyyy HH:mm" }}.</p>
```

Assignments and logic run during rendering, so updated variables (for example
`gameTime`) are persisted for subsequent actions.

