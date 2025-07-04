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
Room files may contain `{{ ... }}` blocks with JavaScript expressions. They are
executed by the embedded [Jint](https://github.com/sebastienros/jint) engine.
Variables like `mc` (main character), `characters` and `gameTime` are available.
Assignments and conditions can be written in plain JavaScript:

```html
{{ if(typeof numberOfShowers === 'undefined'){ numberOfShowers = 0; } }}
{{ if(numberOfShowers < 5){ return "[[Shower, home_bathroom, shower]]"; } }}
```

After rendering a block, changes to variables (e.g. `gameTime`) persist for the
next actions.

