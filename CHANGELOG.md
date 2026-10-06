# DD2 Estate: changes

## 0.1.0 — the first public version

Made for Darkest Dungeon II 2.04.85095 with BepInEx 5.4.23.x.

What is in it:

- A new main-menu entry, **Estate**, with an estate of its own in a profile of its own.
- The Hamlet with its buildings: Stage Coach, Tavern, Abbey, Sanitarium, Guild, Blacksmith, Survivalist, Nomad
  Wagon, Graveyard, the Ancestor's memoirs; building upgrades, town events, the weekly loop.
- The Estate Map with the week's quests, provisioning, and corridor-and-room dungeons (Ruins, Warrens, Weald,
  Cove): torchlight, curios, traps, obstacles, hunger, camping, scouting, secret rooms.
- Darkest Dungeon II's heroes, paths, skills, fights, monsters, bosses, trinkets and quirks inside that loop;
  gold and prices in Darkest Dungeon's numbers.
- The first game's pictures, sound and narration, read from your own install of it while you play.
- An **Estate** tab in the game's options (fonts, loading screens, sound, the first game's folder and more).

## Known issues

- **Balance is not done.** Fights, prices and rewards have not been tuned.
- **The Darkest Dungeon is a stand-in:** five boss fights in a row. There is no ending.
- **It has been run on one PC only** (Windows, Steam, 16:9, mouse and keyboard, English). Not tried: other
  screen shapes, a controller, a Darkest Dungeon that is not from Steam, other languages.
- **The question for the first game's folder** (shown when it is not found) was tested with the folder filled
  in by a script, not typed by hand. If it does not take your typing, close the game and write the folder into
  `BepInEx\config\ph13.dd2.estate.cfg`, section `[Paths]`, key `DarkestDungeon1`.
- **An update of Darkest Dungeon II may break parts of the mod.**
- Achievements are off while the Estate is running.
- The mod's own words are English whatever the game's language is.

Use a fresh profile, or keep backups of your saves. `BepInEx\LogOutput.log` says what happened when something
goes wrong.
