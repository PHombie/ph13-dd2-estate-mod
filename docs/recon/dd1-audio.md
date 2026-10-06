# DD1's voice, music and ambience through DD2's FMOD

The Ancestor's voice for the narration lines, the hamlet's music and ambience, the corridors' ambience and
exploration music, the camp's music, and the town's click sounds: all played from the player's own DD1
install, nothing of DD1's shipped or copied.

Files: `tools/dd1_audio.py` (offline reader and reference), `src/DD2Estate/Dd1/Dd1AudioBank.cs` (bank reader),
`Dd1AudioProject.cs` (names, mixer, where an event lives), `Dd1AudioEngine.cs` (what plays when),
`Dd1Audio.cs` (FMOD voices and channel groups), `src/DD2Estate/Dd2/EstateAudio.cs` (what the estate plays,
DD2's volumes and pause, the MusicMgrBhv patch), `src/DD2Estate/Dev/AudioDev.cs` (bridge commands).

**Nobody has heard any of this yet.** Section 9 says what is proven offline and what only the game can show.

## 1. The banks

`audio/master_banks/master_bank.bank`, `master_bank.strings.bank`, `audio/secondary_banks/*.bank` (39 files) and
22 more in `dlc/*/audio/secondary_banks` and `dlc/*/features/*/audio/secondary_banks`: 62 banks in this install
(three heroes' banks come twice, with two DLCs; the first one found is used). `audio/*.load_order.json` say what DD1 loads
when: `base.system` the two master banks; `base.app` title_screen, music, general, ui_shared, voiceover,
ambience; `base.town` town, ui_town; `base.campaign` raid_screen; `base.raid` ui_dungeon, props_shared,
en_shared; `base.dungeon` the en_* and props_* banks; `base.heroes` the hero_* banks; `base.narration`
voiceover_shared_courtyard.

A bank is an FMOD Studio 1.x bank, format version 103 (`FMT ` chunk: 103, 99). DD2 runs FMOD 2.02.25
(`FMOD/VERSION.cs:7`, number 131609 = 0x00020219) and is not asked to load them as banks; the journal's
finding that it cannot stands unchecked here. What is used instead: the sample data inside a bank is a plain
FSB5, which FMOD's core API opens out of the bank file by offset and length.

Layout (every number checked by `tools/dd1_audio.py banks`, which parses all 62 banks without an error and
finds each FSB5's own header length equal to the length `SNDH` gives):

```
RIFF <size> "FEV "
  FMT   u32 103, u32 99
  LIST "PROJ"            the project's objects, 9 KB .. 390 KB
    BNKI                 bank id
    LIST per object kind, each object a LIST or chunk of its own (see the table)
    SNDH                 array of (u32 file offset, u32 length): the bank's FSB5s (one, or two: a Vorbis and a FADPCM one)
    HASH ...
  SND                    one per FSB5; the FSB5 starts on a 32-byte boundary inside it
```

Objects are 16-byte GUIDs (printed the way `System.Guid` and FMOD print them). Chunks are padded to even
sizes. Arrays are written two ways: of fixed-size elements as `u16 count*2+1` then `u16 element size` (the
size is absent when the count is 0, so an empty one is `01 00`); of variable-size elements as `u16 count*2`,
each element led by its `u16` size (empty: `00 00`). A string is `u16` length and bytes.

| chunk (under PROJ) | what | fields read |
|---|---|---|
| `EVTS/EVNT/EVTB` | event | guid, snapshot guid (zero for a sound event), timeline, input bus, master track bus, i32 max instances, u32 priority, u8, u32, array of parameter layouts |
| `TLNS/TMLN/TLNB` | timeline | guid, event, array "async" (instrument guid, u32 start, u32 length), array "locked" (same), an array always empty, markers (variable: guid, u32 position, name), tempo markers (32 bytes each, not used) |
| `TLNS/TMLN/TRNS/TRAN/TRNB` | transition | guid, destination marker guid, u32 start, u32 end, conditions (parameter guid, f32 min, f32 max), 8 bytes, f32 chance in percent, u32 priority |
| `.../CTRO` after a TMLN or a PMLO | which controllers the timeline position / the parameter drives | array of controller guids |
| `PMLS/PMLO/PMLB` | parameter layout | guid, parameter, event, array of instruments placed on the parameter (not played, see 8) |
| `PRMS/PARM/PRMB` | parameter | guid, 5 bytes, name, f32 min, f32 max, 8 bytes, f32 seek speed (units a second) |
| `CTRS/CTRL` | controller (automation) | guid, target guid, curve guid, u32 property (0 volume dB, 1 pitch, 3 snapshot intensity) |
| `CRVS/CURV` | curve | guid, guid, array of points (x as f32 for a parameter or u32 for a timeline position, f32 y, f32 shape, 4 bytes) |
| `WAIS/WAIT/WAIB` | wave instrument | guid, wave asset guid |
| `MUIS/MUIT/MUIB` + `PLST` | multi instrument (a pool) | guid; PLST: u32 mode (0 in 16 pools, 2 in 924), u32, array (instrument guid, f32 weight) |
| `SPIS/SPIT/SPIB` + `PLST` | scatterer | guid, u32 polyphony, u32 spawns in all (0: no limit), f32 shortest and f32 longest pause in seconds; PLST as above |
| `EVIS/EVIT/EVIB` | event instrument | guid, event guid (a sound event or a snapshot) |
| `INST` after any instrument | common part | owner timeline guid, f32 volume dB, f32 pitch, i32, u8 loop, 52 bytes, u16 24, guid of the audio track it plays on |
| `WAVS/WAV ` | wave asset | guid, u16 12, u32 FSB5 index in SNDH, u32 sample index, u32 flags (2: streamed) |
| `IBSS/IBUS/IBSB`, `MBSS/MBUS/MBSB`, `GBSS/GBUS/GBSB`, `RBSS/RBUS/RBSB` + `BUS ` | input bus, master track, group (an audio track of an event; in the master bank a mixer group), return | guid, u16, output guid; BUS: u8, u32, two arrays of effect guids, u16 8, f32 fader dB |
| `SNAS/SNAP/SNAB` | snapshot | guid, u32, array (4 bytes, target guid, u32 property, f32 value) |

Timeline positions and lengths are samples at 48000 Hz whatever the rate of the waves (44100 for nearly all).

`master_bank.strings.bank` holds the names in its `STDT` chunk: `u32 1`; an array of 8-byte nodes (u24 offset
into the pool, u8 key byte, u24 first child, u8 child count: a radix tree); an array of the GUIDs, sorted;
`u16` pool size and the pool of zero-terminated strings; `u16` count and a u24 per GUID: its leaf node; `u16`
count and a u24 per node: its parent (0xFFFFFF for the root). A path is the pool strings from the root down to
the leaf. 3189 names: 3059 `event:/`, 63 `bank:/`, 40 `bus:/`, 24 `snapshot:/`, 3 `vca:/`.

FSB5 (as `Dd1AudioBank.Samples` and the tool read it; FMOD reads it itself when playing): `"FSB5"`, u32
version (1), u32 sample count, u32 size of the sample headers, u32 size of the name table, u32 size of the
data, u32 codec (15 Vorbis, 16 FADPCM), 32 more bytes (60 in all). Per sample a 64-bit word: bit 0 more chunks
follow, 4 bits rate index, 2 bits channels, 27 bits data offset / 32, 30 bits length in samples; then chunks
(`u32`: bit 0 more, 24 bits size, 7 bits kind; kind 1 channels, 2 rate, 3 loop). The name table is `count`
u32 offsets and the strings. `voiceover.bank`: FSB5 at 353792, version 1, 408 samples, headers 6528, names
12868, data 173710304, codec 16; `voiceover_shared_courtyard.bank`: at 32032, 36 samples, codec 16. Music,
ambience and voice are FADPCM, 44100 Hz stereo, flagged streamed; the town, ui and monster banks are Vorbis.

## 2. The mapping rule and its coverage

DD1 names a sound by its event path: `"/vo/good/camp_04"` in `audio/narration.json` is `event:/vo/good/camp_04`.

1. strings bank: path -> event GUID;
2. the bank that holds an `EVTB` with that GUID (looked for first where its kind lives, `Dd1AudioProject.Homes`:
   `/vo/` voiceover, voiceover_shared_courtyard; `/music/` music; `/ambience/` ambience; `/town/` town; `/ui/`
   ui_town, ui_shared, ui_dungeon; `/general/` general, raid_screen; then every other bank, read one at a time);
3. event -> timeline -> instruments -> wave assets -> (FSB5 index, sample index).

**Sample names are not the rule.** They follow the event names only loosely (`/vo/recruit/occultist` is
`vo_narr_town_rec_occ`): of the 522 narration events only 127 would be found by "vo_narr_ + path with
underscores". The bank's own graph is exact, so that is what the mod reads, at runtime, from the user's files;
the only table of the mod's own is the list of bank names above.

Coverage (`python tools/dd1_audio.py coverage`):

| file | entries with a clip | distinct events | resolve to a sample | shape |
|---|---|---|---|---|
| `audio/narration.json` | 451 | 392 | **392** | 386 one sample, 6 a pool of two takes |
| arena DLC | 70 | 44 | 44 | one sample |
| crimson court | 34 | 32 | 31 (`/vo/crimson_court/town_CCQ_01_return` is in no bank) | one sample |
| shieldbreaker | 17 | 10 | 10 | one sample |
| color of madness | 57 | 45 | 45 | one sample |

All 3059 event paths resolve to a bank. By shape: 1776 one sample, 652 a pool, 485 layered (several boxes on
the timeline), 45 sequenced (transitions between markers: the music), 44 beds (loops and scatterers: the
ambience), 3 one loop, 53 empty, 1 placed on a parameter only.

The plugin's reader against the tool: a console build of `Dd1AudioBank.cs` + `Dd1AudioProject.cs` wrote, for
every one of the 3059 events, bank, box counts, markers, transitions, route dB, max instances, category,
parameters (range, seek, controllers) and the set of reachable samples; the same from the tool; the two files
are identical. The FSB5 sample tables (name, length) of six banks, 1254 samples: identical.

## 3. What the estate plays

`python tools/dd1_audio.py estate` prints these in full; `event <path>` any one.

| where | event | what it is | DD1's level |
|---|---|---|---|
| hamlet | `music/mus_town` | see below | master track -13 dB |
| hamlet | `ambience/town/general` | two looped beds (`amb_town_gen_base`; `amb_townfair` at -42 dB unless the parameter `town_event` is 1), a bell scatterer (three bells and a blank at 73%), a scatterer of 26 one-shots; the cursor loops every 39.75 s and strikes the scatterers again | bed -11.4 dB; -5.4 while the music rests (snapshot) |
| a building's window | `ambience/town/abbey, blacksmith, graveyard, guild, sanitarium, statue, tavern` | a bed, pools and scatterers each (`camping_trainer` exists and is empty; the coach and the wagon have none) | -1.4 dB and their tracks |
| corridors | `ambience/dungeon/crypts, weald, warrens, cove`, `ambience/dungeon/quest/plot_darkest_dungeon_1..4` | a base bed and a dark bed crossfaded by `darkness` (crypts: from 6 to 10), a Fanatic's layer on `fire`; their scatterers stand on tracks DD1 left at -80 dB in the four regions (they sound in the Darkest Dungeon) | -1.4 .. -6.4 dB |
| corridors | `music/mus_exploration` | four levels (intro 4.8 s, loop 128 s each) cut to the timeline together, crossfaded by `darkness` at 2..4, 5..7, 8..10 | master track -8 dB |
| camp | `music/mus_camp`, `ambience/local/campfire` | built like the town's music; a loop | -8 dB, -1.4 dB |
| a narration line | `/vo/...` | one sample (six: a pool of two) | bus:/all_sum/vo -7 dB |
| clicks | `town/*`, `ui/town/*`, `ui/shared/*` | one sample, a pool, or layers (`ui/town/buy`: the purchase, coin rings and sparkles from scatterers) | -1.4 dB |

The town's music (`event:/music/mus_town`), as DD1 built it:

```
locked    0.00 ..   3.53 s  Town_Stereo_Mix_INTRO          marker intro      0.00
locked   35.29 .. 120.00 s  Town_Stereo_Mix_LOOP_1         marker loop1     35.29
locked  141.18 .. 225.88 s  Town_Stereo_Mix_LOOP_2         marker loop2a   141.18
locked  247.06 .. 331.76 s  Town_Stereo_Mix_LOOP_2         marker loop2b   247.06
locked  331.76 .. 337.06 s  Town_Stereo_Mix_OUTRO
locked  388.24 .. 564.70 s  mus_town_stemmed               marker stripped 390.00
at   3.53 -> loop1  if play_variance = 0    (-> loop2a if 1)
at 120.00 -> loop2a if play_variance = 0    (-> loop2b if 1)
at 225.88 -> loop2b if play_variance = 0    (-> loop1  if 1)
at 331.76 -> loop1 with 29.5% if 0 (-> loop2a with 30% if 1), else on into the outro
at 559.41 -> stripped with 50%
at 608.82 -> intro
snapshot town_ambience       0 .. 610.59 s, intensity by position: 1 at 0, 0 from 3.53 to 331.76, 1 from 336.18
snapshot town_building_music 0 .. 610.59 s, intensity = the parameter "inside"
```

So: intro, loop 1, loop 2 twice, three times in ten round again, else the outro, 51 s without music (the
ambience comes up 6 dB: the snapshot sets `bus:/all_sum/sfx/ambience/general` from -9 to -3), the stripped
version for 171 s (every second time once more), 44 s of rest, and the intro again. The offline run of the
plugin's engine walks exactly this (section 4's offline run). `inside` = 1 takes
`bus:/all_sum/music` to -5 dB.

## 4. The engine (`Dd1AudioEngine.cs`): the part of FMOD Studio that is done by hand

DD2's FMOD plays samples; what an event does with them is the mod's to do. The engine has no Unity or FMOD
type in it and runs offline against a host that only writes down what it is asked.

* A cursor per playing event, 48000 positions a second, on its category's clock. The timeline is acted on
  0.6 s ahead of the clock (`Lookahead`), so the file of a sample that follows another is open in time and is
  handed over with its exact start.
* "Locked" boxes (FMOD's timelocked instruments): the sample starts where the cursor enters, offset by how far
  into the box that is, and is cut on the sample where the cursor leaves. "Async" boxes: the sample starts
  when the cursor enters and plays out; a looping one plays until the cursor leaves, then fades (0.5 s). A box
  that ends exactly on a loop point is never left; a looping bed left and entered again within 0.1 s plays on
  (DD1's beds end a few samples before their loop point).
* Transitions at a position are judged in priority order: parameter conditions, then the chance; the first
  that applies moves the cursor to its marker. A loop region is a transition to a marker with its own GUID.
* Pools pick by weight (mode 0: in order). Scatterers spawn on entering and then after a pause drawn between
  their two bounds, up to their polyphony and their total. An event instrument plays its event as a child
  (stopped when its box is left); a snapshot holds mixer faders at `own + (value - own) * intensity`.
* A voice's volume is the dB sum of: its instrument, the pools around it, its audio tracks, the event's
  master track, (for a child: the event instrument and its tracks in the parent), the input bus and the
  mixer groups to `bus:/`. A fader with automation takes the curve's value instead of its own (several
  curves add up), the curve read at the parameter's value or the cursor's position. At or below -41.9 dB is
  silence: a one-shot is then not struck at all, a loop or a locked piece is not opened until it comes up.
* A parameter moves to what it is set to at its seek speed (`inside` 0.94 a second, `darkness` 3 and 0.5).
* An event of one box and no transition (386 of the 392 narration lines, most clicks) is simply played whole
  as soon as its file is open. An event with locked pieces and more starts 0.15 s after it is asked for, so
  its first pieces start on the sample instead of late.
* DD1's cap on instances of an event (a hover: 3): the oldest gives way.

Checked offline (a throwaway console project that compiles `Dd1AudioBank.cs`, `Dd1AudioProject.cs` and
`Dd1AudioEngine.cs` as they are and plays events against a host that records the calls; it is not in the
repo): every one of the 3059 events
played for 30 s and stopped, no exception, at most 13 voices at once; the town music's order and times as in
section 3; the hamlet's ambience following the music's snapshot (0.53 -> 0.27 over the intro); the exploration
music's four levels crossfading on `darkness`; the ambience of every building and dungeon bounded (2..14 voices).

## 5. FMOD (`Dd1Audio.cs`)

FMODUnity's wrapper, decompiled to `_ref/dd2-decomp/FMODUnity/` (`ilspycmd -p -o` on
`Darkest Dungeon II_Data/Managed/FMODUnity.dll`; the csproj already referenced the DLL).

| call | where in the wrapper | used for |
|---|---|---|
| `RuntimeManager.IsInitialized`, `.CoreSystem`, `.StudioSystem` | `FMODUnity/RuntimeManager.cs:151, 145, 143` | the game's core and studio systems; nothing is touched before the game has them up |
| `System.getSoftwareFormat` | `FMOD/System.cs:72` | output rate: DSP clock units |
| `System.createChannelGroup`, `System.getMasterChannelGroup`, `ChannelGroup.addGroup` | `FMOD/System.cs:373, 410`, `FMOD/ChannelGroup.cs:15` | a root group on the core's master group and four under it: Music, Voice, Ambience, Sfx |
| `System.createStream(file, MODE, ref CREATESOUNDEXINFO, out Sound)` | `FMOD/System.cs:338`, `FMOD/CREATESOUNDEXINFO.cs:10,12,22,44`, `FMOD/MODE.cs` | one voice: the bank file, `CREATESTREAM | NONBLOCKING | _2D | IGNORETAGS | LOOP_OFF or LOOP_NORMAL`, `fileoffset`/`length` of the FSB5 from SNDH, `initialsubsound` = the sample, `suggestedsoundtype = FSB` |
| `Sound.getOpenState` | `FMOD/Sound.cs:118` | polled each frame until READY (its return value is the load's error) |
| `Sound.getSubSound(index)`, `Sound.getLength` | `FMOD/Sound.cs:70, 92` | the sample; polled again until READY (a stream seeks) |
| `System.playSound(sub, group, paused: true)` | `FMOD/System.cs:390` | then volume, pitch, position, delay, and unpaused |
| `Channel.setVolume`, `setPitch`, `setPosition(ms)`, `setPaused`, `isPlaying`, `stop` | `FMOD/Channel.cs:105, 130, 30, 95, 185, 90` | pitch = 2^(semitones/12) |
| `ChannelGroup.getDSPClock` | `FMOD/ChannelGroup.cs:192` | the group's clock, once a frame |
| `Channel.setDelay(start, end, stopchannels)` | `FMOD/Channel.cs:220` | a start in the future and every end, on the sample |
| `Channel.addFadePoint`, `removeFadePoints` | `FMOD/Channel.cs:235, 245` | the fade before an end |
| `ChannelGroup.setVolume`, `setPaused`, `setMute`, `release`; `Sound.release` | `FMOD/ChannelGroup.cs:82, 72, 117, 10`; `FMOD/Sound.cs:10` | the categories; a sound still loading is released once it is open |

A stream plays one of its samples at a time, so each voice opens the bank file for itself; nothing is loaded
whole. At most 48 voices.

The clock. Each category has a clock the engine reads, in seconds. While the group's DSP clock moves, it is
that clock (divided by the rate), and an engine time converts to a DSP clock value exactly: two samples that
follow each other on the timeline get the same count for the end of one and the start of the other. When the
DSP clock has not moved for 0.1 s (FMOD may let a group that plays nothing stand still: not known), real time
is counted instead and a voice whose time comes is started at once; once it sounds the clock runs again. A
paused category's clock stands still, and so do its timelines.

Fail soft: no DD1 install, no strings bank, FMOD not up, a bank missing or of another format, an event that
does not exist, any FMOD error code: one warning per distinct cause in the log (`DD1 audio: ...`), the sound is
not there, nothing throws into the game's update. `audio.state` lists the causes under `complaints`.

## 6. DD2's side (`Dd2/EstateAudio.cs`)

**Volumes.** DD2's four sliders are options `master_volume`, `music_volume`, `sfx_volume`, `vo_volume`
(`Assets.Code.UI.Options/OptionsValue.cs:131-137`), applied as VCA volumes: `AudioMgr.SetVolume` looks up
`vca:/Master`, `vca:/Music`, `vca:/SFX`, `vca:/VO` (`Assets.Code.Audio/AudioVolumeUtils.cs:18`,
`AudioVolumeType.cs:3`) and calls `VCA.setVolume(0..1)` (`AudioMgr.cs:774-805`, set from the options at
`AudioMgr.cs:94-122`). The mod reads them back every frame (`Studio.System.getVCA`, `VCA.getVolume`:
`FMOD.Studio/System.cs:69`, `VCA.cs:36`) and sets its groups to master x music, master x vo, master x sfx
(ambience and clicks), times `EstateAudio.Level`.

**Mute and pause.** The game mutes its master bus when the window loses focus unless the option allows audio
in the background (`AudioMgr.cs:170-188` -> `RuntimeManager.MuteAllEvents`, `RuntimeManager.cs:1055`) and pauses
it with the application (`RuntimeManager.cs:1047`). The mod's groups are not under that bus, so the bus's
`getMute`/`getPaused` (`FMOD.Studio/Bus.cs:62, 52`) are copied to them every frame. The pause menu raises
`EventGamePauseChanged` (`Assets.Code.Presentation.Events/EventGamePauseChanged.cs`), on which the game pauses
its "pausable" buses and plays `snapshot:/mute_paused_buses` (`AudioMgr.cs:213-232`; which buses is prefab
data); the mod listens to the same event (`EventManager.AddListener`, `Assets.Code.Events/EventManager.cs:54`):
voice and clicks are held, music and ambience go to half (DESIGN CALL; DD1's own pause snapshot takes its mix
down 2 dB, the ambience 5.6, and filters it).

**What DD2 plays in the hub mode: nothing.** `MusicMgrBhv` is called for every mode entered
(`GameModeMgr.CallGameModeEnterStart`, `Assets.Code.Game/GameModeMgr.cs:781`, through `CallOnAllInstallers`).
`MusicMgrBhv.OnGameModeEnterStart` (`Assets.Code.Audio.Music/MusicMgrBhv.cs:265`) has a case per vanilla mode;
for the mod's ESTATE mode it falls to the last else, logs `Unhandled GameModeType "ESTATE" for music.`
(`:414`), and calls `HandleMusicChange(null)` (`:682`) which is `Stop(false)` (`:769`): the menu's or the
fight's music is released with its fade. The ambience behaviours each answer one mode and stop on leaving it
(`InnAmbienceBhv.cs:43-84` INN, `CombatAmbienceBhv.cs:31-48` COMBAT, `CombatResultsAmbienceBhv.cs:15-35`
RESULTS, `MainMenuAmbienceBhv.cs:37-53` MAIN_MENU). So the hub is silent of DD2 by the game's own logic, and a
fight gets DD2's music back by the same code on entering COMBAT. The patch `Dd2MusicStaysOutOfTheEstate`
(prefix on `OnGameModeEnterStart`) does for the hub mode exactly what that else branch does, `Stop(false)`,
and skips the original: the same silence without the warning in the log, and not resting on a fall-through.
Not known: which music a fight in the estate gets. `MusicMgrBhv` picks by combat source, battle configuration,
Kingdoms faction of the enemies, then the active biome's music; with none of them it stops the music and the
fight has only DD2's combat ambience. That is DD2's choice of music for a Kingdoms fight and was left alone.

**Place.** Once a frame: hamlet (`EstateSession.InHub`, view Hamlet), dungeon (view Dungeon with a
`DungeonRun.Current`), or neither (a fight, a mode change, the menu). On a change everything of the old place
fades over 1.5 s and the new place's events start; a fight therefore fades DD1 out under DD2's own audio and
the place starts again afterwards (the town's music from its intro). In the hamlet `RosterWindow.Current.Id`
sets the music's `inside` and swaps the town's ambience for the building's (INFERRED that DD1 takes the town's
ambience away inside: its files only show the music's snapshot). In a dungeon `darkness = (100 - light) / 10`
(INFERRED from where the layers change: 2, 4..5, 7..8, 10 put DD1's four light bands on the torch), and
`CampScreen.IsOpen` swaps the exploration music for `mus_camp` and adds the campfire. The Darkest Dungeon's
ambience follows `Dd1Install.DarkestQuestArt`; its fourth descent has no exploration music (DD1's
`snapshot:/darkest_4_explore_music_off`). `play_variance` is a coin per visit (INFERRED: DD1's code sets it
where the files do not show).

**The Ancestor.** `EstateAudio.Speak(source)` takes the audio event from the end of a narration line's source
(`"camp /vo/good/camp_04"`); the mod's own story lines have none. While he speaks, music and ambience are at
-5 dB (DESIGN CALL: DD1 has a compressor on its music bus keyed by the voice bus, whose settings are effect
data). `SpokenUntil` is the time his line ends, `LineIsOver(readingDeadline)` whether the words may go. When
the band is hidden by anyone (a fight, `DungeonRun.Start`, the bridge) the voice fades with it.

**Clicks** (only what can be seen from outside the screens):

| moment | how it is noticed | DD1 event |
|---|---|---|
| a building is clicked | `HamletScene.BuildingClicked` | `town/enter_coach, enter_abbey, enter_blacksmith, enter_graveyard, enter_guild, enter_sanitarium, enter_statue, enter_tavern` (DD1 has none for the wagon and the survivalist) |
| a building's window closes | `RosterWindow.IsOpen` falling | `ui/town/building_zoomout` |
| a building step is built | `UpgradeRules.Built` | `town/building_upgrade_<building>` (else `town/building_upgrade`) |
| the Estate Map opens / the provisions open | `QuestPanel.IsOpen`, `ProvisionScreen.IsOpen` rising | `ui/town/embark_button`, `ui/town/provision_button` |
| the party sets off | hamlet -> dungeon | `ui/town/set_off_button` |
| a hero into / out of the party | `EstateSession.PartySize` | `ui/town/character_add`, `character_remove` |
| a hero hired | `RosterLifecycle.Count` up while the coach's window is open | `town/stage_coach_purchase` (INFERRED from the naming: every building's action sound is `<building>_purchase...`) |
| gold spent with a window or the provisions open | `EstateState.Gold` down | `ui/town/buy` |
| gold gained at the wagon | `EstateState.Gold` up | `ui/town/sell` |
| the town event's notice opens | `TownEventPanel.IsOpen` rising | `town/town_event_display_<tone>` |

## 7. Snippets for files this work does not own

Both were applied to a scratch copy of the project and built: 0 errors.

`Estate/Narration.cs`, `Present()`: two places.

```csharp
            if (_showing != null)
            {
                // A spoken line belongs to the voice: no click sends it away while he speaks, and it goes when he
                // has done (DD1 times its subtitles by the clip). A line without a voice keeps its reading time.
                NarrationBox.KeepUntil = EstateAudio.SpokenUntil;
                // Clicked away, or its time is up. On another stage (a fight began) it simply goes.
                if (!NarrationBox.IsOpen) _showing = null;
                else if (stage == null || EstateAudio.LineIsOver(_hideAt))        // was: Time.unscaledTime >= _hideAt
```

```csharp
                _hideAt = Time.unscaledTime + Mathf.Clamp(ReadBase + ReadPerCharacter * next.Text.Length, ReadMin, ReadMax);
                // The voice, for a line DD1 has one for (its audio event is the end of the line's source); the mod's own words have none.
                EstateAudio.Speak(next.Source);
```

`Speak` must be called for every line shown (it also forgets the previous line's voice). Nothing else is
needed: `Narration.Hush`, the session's end and a fight hide the band, and the voice goes with the band.

`Estate/NarrationBox.cs`: the "keep until" time.

```csharp
        public static bool IsOpen => _instance != null && _instance.gameObject.activeSelf;

        /// <summary>
        /// Until this time (Time.unscaledTime) a click does not send the line away: a spoken line stays while
        /// the voice lasts. Set by whoever shows a spoken line; every new text starts without it.
        /// </summary>
        public static float KeepUntil;
```
in `Show`, after `_instance._shownAt = Time.unscaledTime;`: `KeepUntil = 0f;`
in `Update`: `if (Time.unscaledTime - _shownAt < MinimumShown || Time.unscaledTime < KeepUntil) return;`

(`SpokenUntil` is now + what is left of the voice + 0.4 s while he speaks, and 0 otherwise, so a paused game
does not let the words run out under the voice.) The comments in `NarrationBox.cs`, `Narration.cs` and
`NarrationRules.cs` that say the clips cannot be played are out of date.

Optional, `Plugin.cs`, with the other `Config.Bind` calls: a switch and a level for players.

```csharp
            EstateAudio.Enabled = Config.Bind("Audio", "Dd1Audio", true,
                "Play Darkest Dungeon (1)'s own narration voice, town music and ambience from your DD1 install.").Value;
            EstateAudio.Level = Config.Bind("Audio", "Dd1Level", 1f,
                "Volume of DD1's sound against DD2's own (0..1), on top of the game's sliders.").Value;
```

Sounds that need a call from inside a screen (one line each, safe anywhere: `EstateAudio.Ui("...")` is silent
without DD1 audio or outside the estate):

| where | call |
|---|---|
| `UI/UiKit` buttons: hover, click, a refused click, back | `ui/town/button_mouse_over`, `ui/town/button_click`, `ui/town/button_invalid` (or `button_click_locked`), `ui/town/button_click_back` |
| Blacksmith: weapon / armour bought | `town/blacksmith_purchase_weapon`, `town/blacksmith_purchase_armor` |
| Guild / Survivalist: a skill bought | `town/trainer_purchase_skill` |
| Sanitarium: a treatment paid | `town/sanitarium_treatment`, `town/sanitarium_disease_treatment` |
| Tavern / Abbey: a hero placed | `town/tavern_bar`, `tavern_gambling`, `tavern_brothel`, `town/abbey_meditation`, `abbey_prayer`, `abbey_flagellation` |
| a roster row picked up / a hero let go | `ui/town/character_pickup`, `ui/town/let_go` |
| a trinket put on / taken off, the trinket box | `ui/town/character_equip`, `character_unequip`, `ui/town/trinket_open`, `trinket_close` |
| a quest picked on the Estate Map | `ui/town/dungeon_select` |
| a page turned (memoirs, a scroll) | `ui/town/page_open`, `ui/town/page_close` |

## 8. Left out, and why

* DD1's banks loaded as banks: not tried (the journal's finding); the core route does not need it.
* Effects: reverbs (`snapshot:/Reverbs/verb_crypts` and the others set a reverb's parameters), filters, the
  music bus's compressor, sends and returns. They are effect chunks with their own parameter blocks; a wrong
  reverb is worse than none, and none of it can be judged by reading.
* Modulators (random volume and pitch per instrument, `MODS`), pitch automation (the crypts' dark bed drops
  0.13 semitones in full darkness), the shape of automation curves (straight lines here), tempo markers and
  quantised transitions (DD1's music uses none in the events played).
* Instruments placed on a parameter instead of the timeline (`PMLB` with entries: footsteps, combat stingers:
  163 of the 400 layouts, none in an event the estate plays).
* Parameters the estate has no source for: `town_event` (the town fair's crowd in the ambience),
  `distance_to_goal`, `fire` (the Fanatic's pyre), `monster_intensity`. They stay at 0; `audio.param` sets them.
* DD1's fight sounds and fight music (`mus_battle_*`), hero and monster sounds, props and curios, the raid
  screen: combat is DD2's; curio and prop sounds would be the next thing to hook (props_*.bank,
  `event:/props/curios/*`, same engine).
* Narration events of DD1's the estate never triggers (fights, torch, curios: `docs/recon/town-events.md`).
* The hamlet's second ambience `ambience/town/general_2` (when DD1 uses it is not in the files).
* The clicks of section 7's table.
* A sample is always streamed, also the short ones DD1 loads into memory: one file open per voice. A click
  therefore sounds a frame or two after it is asked for.

## 9. Proven offline, and what only the game can show

Proven by reading and running outside the game:
* every bank parses; SNDH offsets and lengths match the FSB5 headers; the strings table adds up to its size;
* 392 of 392 narration events of `narration.json` resolve to samples; all 3059 events to a bank;
* the plugin's reader equals the tool on every event and on 1254 sample names and lengths;
* the engine plays every event without an exception and stops it; the town's music, the ambience, the
  snapshots and the darkness crossfades do what section 3 and 4 say, against a host that records calls;
* the plugin builds with 0 errors; the two narration snippets build in a scratch copy.

Only a run in the game can show:
1. that `createStream` accepts an FSB5 embedded in a Studio 1.x bank by `fileoffset`/`length` with
   `initialsubsound`, for FADPCM (voice, music, ambience) and for Vorbis (town, ui): FMOD 2.02 must know the
   Vorbis setup of these old samples. The log says `DD1 audio: opening ... (ERR_...)` once per cause if not;
2. that the samples sound at all, at sensible levels against DD2's (DD1's own mix in dB is kept; `Level` and
   the sliders scale it), and that DD2's sliders, the focus mute and the pause do what section 6 says;
3. whether a channel group's DSP clock stands still while nothing plays in it (either way is handled; the
   difference is whether the first sample after silence starts on the sample or within a frame);
4. the seams: intro -> loop 1 -> loop 2 of the town's music (cut and started on the same DSP clock count),
   the loops of the ambience beds (streams with LOOP_NORMAL), the jump inside `mus_town_stemmed`;
5. that DD2 is silent in the hub (read from its code, section 6) and what music an estate fight gets;
6. the narration: voice and words together, the band staying until the voice ends, the hush when a fight
   begins, the music stepping back under the voice;
7. every INFERRED and DESIGN CALL above by ear: the town's ambience giving way inside buildings, the darkness
   scale, `play_variance`, the -5 dB under the voice, the pause levels, the scatterers' pauses (taken as
   seconds; first spawn on entering);
8. the camp: DD1's camp music over whatever the Kingdoms camp scene plays itself (`EstateAudio.CampMusic`).

## 10. Bridge and tool

Bridge (`Dev/AudioDev.cs`; all but `audio.speak` work from the main menu on):
`audio.state`, `audio.banks [read=true]`, `audio.samples bank=.. [filter=..]`, `audio.events [prefix=..] [limit=N]`,
`audio.event path=..`, `audio.play path=.. [params=a=1,b=0]`, `audio.play sample=<name> [bank=..] [category=Voice] [volume=1]`,
`audio.stop [path=..] [fade=..]`, `audio.param name=.. value=..`,
`audio.volume [level=..] [enabled=..] [dungeonMusic=..] [campMusic=..]`, `audio.speak path=/vo/..`, `audio.hush`.

A first test without the mouse: `audio.play path=/vo/good/camp_04` (FADPCM, one stream), `audio.play
path=ui/town/buy` (Vorbis, scatterers), `audio.play path=music/mus_town`, then `audio.state`: `complaints` empty
and `voices` in stage `Playing` mean FMOD took the files.

Tool: `python tools/dd1_audio.py banks | samples <bank> [text] | events [prefix] | event <path> | coverage |
estate [--brief] | map <out.json>` (`--dd1 <dir>` or `DD1_GAME_DIR`). On Git Bash write event paths without the
leading slash (`vo/good/camp_04`): the shell rewrites `/vo/...` into a Windows path.
