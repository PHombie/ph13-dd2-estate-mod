using System.Collections.Generic;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.Quirk;
using Assets.Code.Source;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dev;
using DD2Estate.Estate;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// Test commands of the dungeon HUD for the dev bridge (python tools/bridge.py run hud.state, ... run
    /// hud.select guid=3, ... run hud.tab inventory=true, ... run hud.press what=retreat, ... run hud.loot
    /// items=gold:500,torch:2,ruby:1, ... run hud.take index=-1). They do what the screen does, without the
    /// mouse; hud.loot and hud.ask open a window that no rule of the expedition asked for, to look at it.
    ///
    /// DD1's feedback over the scene has its own: raid.announce key=trap (the banner), raid.pop hero=1 kind=heal
    /// value=5 (a pop text over a hero), raid.tray hero=1 icons=deathsdoor,camp_buff (status icons over a
    /// health bar), raid.effect hero=1 damage=4 (the real thing, by the game's own calls) and raid.feedback
    /// (what all three show now).
    /// </summary>
    [EstateModule]
    internal static class RaidDev
    {
        private static void Register()
        {
            AgentBridge.Register("hud.state", o => DungeonHud.Describe());
            AgentBridge.Register("hud.select", o => DungeonHud.Select((uint)o["guid"]) ? "selected" : (object)"not a living hero of the party");
            AgentBridge.Register("hud.tab", o =>
            {
                DungeonHud.ShowInventory((bool?)o["inventory"] ?? true);
                return DungeonHud.Describe();
            });
            // what: retreat (the flag: DD1's question), choice / continue (the quest-complete choice), close, home
            // {"what":"retreat","blur":false}: the question over a plain shade instead of DD1's blurred screen.
            AgentBridge.Register("hud.press", o =>
            {
                if (o["blur"] != null) RaidConfirm.Blur = (bool)o["blur"];
                return DungeonHud.DevPress((string)o["what"] ?? "");
            });
            // The layout as read from DD1's files, to compare with docs/recon/dd1-raid-ui.md.
            AgentBridge.Register("hud.layout", o =>
            {
                var l = RaidLayout.Current;
                return new
                {
                    safeLeft = l.SafeLeft, safeRight = l.SafeRight, panelTop = l.PanelTop,
                    torchY = l.TorchY, torchGauge = new[] { l.TorchGaugeOffset.x, l.TorchGaugeOffset.y, l.TorchGaugeSize.x, l.TorchGaugeSize.y },
                    basicScroll = new[] { l.BasicPos.x, l.BasicPos.y }, sidebarScroll = new[] { l.SidebarPos.x, l.SidebarPos.y },
                    sidebarBody = new[] { l.SidebarBody.x, l.SidebarBody.y },
                    questInfo = new[] { l.QuestPos.x, l.QuestPos.y }, trayY = l.TrayY,
                    mapClip = new[] { l.MapClip.xMin, l.MapClip.xMax, l.MapClip.yMin, l.MapClip.yMax }, mapTile = l.MapTile,
                    bag = new[] { l.BagStart.x, l.BagStart.y, l.BagOffset.x, l.BagOffset.y, l.BagColumns }
                };
            });
            // DD1's loot scroll with the items named, taken into the bag as far as it holds them:
            // {"items":"gold:500,torch:2,ruby:1","title":"Treasure!"}. Amounts in DD1 units.
            // {"source":"battle"} gives it DD1's heading and line for where the loot came from: chest (the
            // default), battle ("Victory!"), camping, quest, hero_death ("Reclaimed:"), none.
            // A trinket is named by its DD2 item id: {"items":"trinket:<id>:1","rarity":"rare"} (the DD1 rarity it
            // counts as found under: its card and its price at home; without one it keeps the one it has).
            // {"find":true} makes it a find of the expedition's own: it is in the save while it lies on the scroll.
            AgentBridge.Register("hud.loot", o =>
            {
                var run = DungeonRun.Current;
                if (run == null) return "no expedition";
                var source = FoundLoot.Parse((string)o["source"]);
                var stacks = new List<ItemStack>();
                foreach (var part in ((string)o["items"] ?? "gold:500,torch:2").Split(','))
                {
                    var pair = part.Trim().Split(':');
                    var item = InventoryContent.Items.Find(pair.Length > 2 ? pair[0] + ":" + pair[1] : pair[0]);
                    if (item == null) return "no such item: " + pair[0];
                    if (item.Type == ItemTypes.Trinket)
                    {
                        if (Trinkets.Find(item.Id) == null) return "no such DD2 trinket: " + item.Id;
                        Trinkets.NoteGrade(item.Id, (string)o["rarity"]);
                    }
                    stacks.Add(new ItemStack { Item = item, Amount = pair.Length > 1 && int.TryParse(pair[pair.Length - 1], out var amount) ? amount : 1 });
                }
                if ((bool?)o["find"] == true) return run.DevFind(source, stacks) ? DungeonHud.Describe() : "the expedition is in the middle of something";
                DungeonHud.Loot((string)o["title"] ?? FoundLoot.Title(source), (string)o["text"] ?? FoundLoot.Description(source),
                    stacks, i => stacks[i].Amount = run.Give(stacks[i].Item, stacks[i].Amount), () => { });
                return DungeonHud.Describe();
            });
            // A card of the open loot scroll, as a click on it; index -1 is Take All.
            AgentBridge.Register("hud.take", o => DungeonHud.DevLoot((int?)o["index"] ?? -1) ? DungeonHud.Describe() : "no loot scroll is open");
            // A question nobody waits for an answer to: {"text":"...","options":"Yes,No"}.
            AgentBridge.Register("hud.ask", o =>
            {
                var options = ((string)o["options"] ?? "Yes,No").Split(',');
                DungeonHud.Ask((string)o["text"] ?? "A question.", options, i => Plugin.Log.LogInfo("hud.ask answered: " + i));
                return DungeonHud.Describe();
            });
            RegisterFeedback();
            RegisterPlay();
        }

        // ---- what the player does on the dungeon's screen, without the mouse ---------------------------------

        private static void RegisterPlay()
        {
            // The mod's own log lines under the quest info (off: DD1 has no such element): {"show":true} puts them back.
            AgentBridge.Register("hud.log", o =>
            {
                if (o["show"] != null) DungeonHud.ShowLogLines = (bool)o["show"];
                return new { shown = DungeonHud.ShowLogLines, lines = DungeonRun.Current?.LogText(8) };
            });
            // A card of the bag as the right button uses it: {"slot":3} (on the selected hero, or offered to the
            // curio or obstacle whose scroll is up). Carried and let go: {"slot":3,"to":"slot"} (the scroll's item
            // slot), {"to":"hero:GUID"}, {"to":"cell:7"} (another cell of the bag: stacks join, two kinds change places).
            AgentBridge.Register("hud.item", o => DungeonHud.DevBag((int?)o["slot"] ?? -1, (string)o["to"]));
            // DD1's line of user information over the pointer: {"text":"You don't have a shovel!"}.
            AgentBridge.Register("hud.inform", o =>
            {
                DungeonHud.Inform((string)o["text"] ?? RaidText.Get("str_user_information_no_shovel", "You don't have a shovel!"));
                return DungeonHud.Describe();
            });
            // A hero's speech balloon: {"hero":1,"text":"That item had no effect."} (hero: the rank, 1 leads; or "guid").
            AgentBridge.Register("hud.bark", o =>
            {
                DungeonHud.Bark(HeroOf(o), (string)o["text"] ?? RaidText.Get("str_curio_item_had_no_effect", "That item had no effect."));
                return DungeonHud.Barks?.Describe();
            });
            // The torch: what it shows and what its tooltip says. {"dd2":true} DD2's own widget in its place,
            // {"reduce":true} / {"snuff":true} as a shift-click (shift-ctrl-click) on it.
            AgentBridge.Register("hud.torch", o =>
            {
                if (o["dd2"] != null) Dd2Torch.Enabled = (bool)o["dd2"];
                if ((bool?)o["snuff"] == true) DungeonRun.Current?.ReduceTorch(true);
                else if ((bool?)o["reduce"] == true) DungeonRun.Current?.ReduceTorch(false);
                return DungeonHud.Torch?.Describe();
            });
            // The party's order: {} how it marches; {"hero":3,"to":1} the hero of rank 3 takes rank 1 (or "guid" and
            // "onto": two guids), as a hero carried onto another's place; {"reset":true} DD1's "Default Party order".
            AgentBridge.Register("party.order", o =>
            {
                var run = DungeonRun.Current;
                if (run == null) return "no expedition";
                object done = null;
                if ((bool?)o["reset"] == true) done = run.RestoreOrder();
                else if (o["to"] != null || o["onto"] != null)
                {
                    var order = DungeonRun.MarchingOrder();
                    var hero = HeroOf(o);
                    var rank = ((int?)o["to"] ?? 0) - 1;
                    var onto = o["onto"] != null ? (uint)o["onto"] : rank >= 0 && rank < order.Count ? order[rank] : 0u;
                    done = run.MoveHero(hero, onto);
                }
                var names = new List<object>();
                foreach (var guid in DungeonRun.MarchingOrder())
                {
                    var actor = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(guid);
                    names.Add(new { guid, name = actor?.ActorName, frontRank = actor != null && actor.GetIsRankSet() ? actor.GetFrontRank() : -1, disarm = actor != null ? run.DisarmChance(actor) : 0 });
                }
                return new { done, canReorder = run.CanReorder, changed = run.OrderChanged, order = names, hud = DungeonHud.Party };
            });
            // The map: its scale (0.5: a room is 32 px wide), where the party's mark stands on DD1's screen (DD1: 1285, 881)
            // and whether a destination is marked. {"zoom":1} / {"zoom":-1} the wheel by steps, {"zoom":0} DD1's scale again.
            AgentBridge.Register("hud.map", o =>
            {
                var map = DungeonHud.Map;
                if (map == null) return "no HUD";
                if (o["zoom"] != null) map.DevZoom((int)o["zoom"]);
                return map.Describe();
            });
            // The banner's row of skills: {} what stands in its places; {"place":5} a click on that place (5 is DD1's
            // "move": chosen, hud.select of a companion makes the selected hero take their place; 6 is "pass").
            AgentBridge.Register("hud.skill", o => DungeonHud.DevSkill((int?)o["place"] ?? 0));
            // The curio the party stands at: {} what it is, DD1's words for it and the item of the bag it reacts to;
            // {"turn":true} the party turns to it (a click on it, or W): its window, or for a curio without one
            // (sconce, crate, sack, discarded pack) the thing itself.
            AgentBridge.Register("curio.here", o =>
            {
                var run = DungeonRun.Current;
                if (run == null) return "no expedition";
                var curio = run.Exploration.CurioHere;
                var turned = (bool?)o["turn"] == true && run.TurnToCurio();
                return new
                {
                    curio, turned,
                    title = curio != null ? PropText.CurioTitle(curio) : null,
                    content = curio != null ? PropText.CurioContent(curio) : null,
                    itemSlot = run.ItemForCurio(),
                    hud = DungeonHud.Describe()
                };
            });
            // DD1's sentence for what an interaction with a curio did, shown in the banner as the dungeon shows it:
            // {"curio":"discarded_pack","type":"Loot"} ("The pack contains loot!"), with "item" and "value" where
            // the sentence hangs on them. Nothing happens to the party.
            AgentBridge.Register("curio.result", o =>
            {
                var run = DungeonRun.Current;
                if (run == null) return "no expedition";
                var outcome = new CurioOutcome { CurioId = (string)o["curio"] ?? "discarded_pack", ItemId = (string)o["item"], Type = (string)o["type"] ?? CurioResultTypes.Loot, Value = (string)o["value"] };
                var text = run.CurioResultText(outcome);
                if (text != null) DungeonHud.Announce(RaidAnnounce.Curio, text);
                return new { text, banner = DungeonHud.Banner?.Describe() };
            });
        }

        // ---- DD1's feedback over the scene: the banner, the pop text, the status icons --------------------

        // The hero a command means: {"guid":3}, or {"hero":2} for the second in marching order (1 leads).
        private static uint HeroOf(JObject o)
        {
            if (o["guid"] != null) return (uint)o["guid"];
            var party = DungeonHud.Party;
            var rank = ((int?)o["hero"] ?? 1) - 1;
            return party != null && rank >= 0 && rank < party.Count ? party[rank] : 0u;
        }

        private static object Feedback()
        {
            return new
            {
                listening = RaidFeedback.Listening,
                banner = DungeonHud.Banner?.Describe(),
                pops = DungeonHud.PopTexts?.Describe(),
                trays = DungeonHud.Trays?.Describe()
            };
        }

        private static void RegisterFeedback()
        {
            // Everything of the three at once: what the banner shows, the texts in the air, the trays and their icons.
            AgentBridge.Register("raid.feedback", o => Feedback());
            // DD1's announcement banner. {"key":"trap"}: one of trap, disarm, ambush, surprised, monsters_surprised,
            // new_quirk, new_quirk_negative, curio_purge, curio_no_purge, deaths_door, curio. {"subject":"Dismas"}
            // fills a name in (a hero for deaths_door, a quirk for new_quirk and curio_purge). {"text":"..."} any
            // words instead of DD1's; {"pos":"left|right|top|bottom"} another of DD1's four places; {"seconds":3}
            // another time up; {"colour":"notable"} another DD1 colour. Without a key: what there is to ask for.
            AgentBridge.Register("raid.announce", o =>
            {
                var banner = DungeonHud.Banner;
                if (banner == null) return "no dungeon HUD on screen";
                var key = (string)o["key"];
                if (key == null)
                {
                    var keys = new List<object>();
                    foreach (var known in RaidAnnounce.All)
                        keys.Add(new { key = known.Key, words = known.Words(null), seconds = RaidLayout.Current.AnnounceTime(known.Time), place = known.Place.ToString(), colour = known.ColourId });
                    var l = RaidLayout.Current;
                    return new
                    {
                        keys,
                        places = new { left = V(l.AnnounceLeft), right = V(l.AnnounceRight), top = V(l.AnnounceCentreTop), bottom = V(l.AnnounceCentreBottom), text = V(l.AnnounceText) },
                        state = banner.Describe()
                    };
                }
                var what = RaidAnnounce.Find(key);
                if (what == null) return "no such banner: " + key;
                AnnouncePlace? place = null;
                switch ((string)o["pos"])
                {
                    case "left": place = AnnouncePlace.Left; break;
                    case "right": place = AnnouncePlace.Right; break;
                    case "top": place = AnnouncePlace.CentreTop; break;
                    case "bottom": place = AnnouncePlace.CentreBottom; break;
                }
                var colour = o["colour"] != null ? Dd1.Dd1Fonts.Colour((string)o["colour"], what.Colour) : what.Colour;
                var words = (string)o["text"] ?? what.Words((string)o["subject"]);
                var wait = banner.Show(what.Key, words, colour, place ?? what.Place, (float?)o["seconds"] ?? RaidLayout.Current.AnnounceTime(what.Time));
                return new { readIn = wait, state = banner.Describe() };
            });
            // DD1's pop text over a hero. {"hero":1,"kind":"heal","value":5}: kind is one of damage, heal, stress,
            // stress_heal, full, cured, buff, debuff, death_avoided, deathblow (or DD1's own name: hero_heal,
            // stress_damage, stress_reduce). {"text":"..."} any words in the kind's colours and motion.
            // {"all":true} every kind over the hero, one after another. {"anchor":149} the height above a hero's
            // feet the layout's offsets are counted from (-1: DD1's icon_world_y_offset). Without a kind: the kinds.
            AgentBridge.Register("raid.pop", o =>
            {
                var pops = DungeonHud.PopTexts;
                if (pops == null) return "no dungeon HUD on screen";
                if (o["anchor"] != null) RaidPopText.Anchor = (float)o["anchor"];
                var hero = HeroOf(o);
                if ((bool?)o["all"] == true)
                {
                    var shown = 0;
                    foreach (var kind in RaidPop.All)
                        if (pops.Show(hero, kind, kind.Word == null ? (7 + shown).ToString() : null)) shown++;
                    return shown > 0 ? pops.Describe() : "no such hero on screen";
                }
                var name = (string)o["kind"];
                if (name == null)
                {
                    var kinds = new List<object>();
                    foreach (var kind in RaidPop.All)
                        kinds.Add(new { kind = kind.Kind, word = kind.Word, start = V(kind.Start), rise = kind.Rise, time = kind.Time, icon = kind.Icon != null });
                    return new { kinds, state = pops.Describe() };
                }
                var pop = RaidPop.Find(name);
                if (pop == null) return "no such pop text: " + name;
                var text = (string)o["text"] ?? (o["value"] != null ? ((int)o["value"]).ToString() : pop.Word == null ? "7" : null);
                return pops.Show(hero, pop, text) ? pops.Describe() : "no such hero on screen";
            });
            // The status icons over a hero's health bar, whatever the hero's state: {"hero":1,"icons":
            // "deathsdoor,mortality,disease,camp_buff,town_event"}; {"hero":1,"icons":""} gives the hero's own
            // back, {"clear":true} everybody's. {"hero":1,"words":"camp_buff"} an icon's tooltip as it would read
            // now. Without arguments: the trays and what stands over them.
            AgentBridge.Register("raid.tray", o =>
            {
                var trays = DungeonHud.Trays;
                if (trays == null) return "no dungeon HUD";
                if ((bool?)o["clear"] == true) RaidTrays.DevIcons.Clear();
                var hero = HeroOf(o);
                if (o["words"] != null)
                {
                    var icon = TrayIcons.Find((string)o["words"]);
                    var actor = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(hero);
                    if (icon == null || actor == null) return "no such icon or hero";
                    return new { key = icon.Key, art = icon.Art, row = icon.Left ? "left" : "right", shows = icon.Shows(actor, DungeonRun.Current), words = icon.Words(actor, DungeonRun.Current) };
                }
                if (o["icons"] != null)
                {
                    if (hero == 0u) return "no such hero";
                    var wanted = new HashSet<string>();
                    foreach (var part in ((string)o["icons"]).Split(','))
                    {
                        var key = part.Trim();
                        if (key.Length == 0) continue;
                        if (TrayIcons.Find(key) == null) return "no such icon: " + key;
                        wanted.Add(key);
                    }
                    if (wanted.Count > 0) RaidTrays.DevIcons[hero] = wanted;
                    else RaidTrays.DevIcons.Remove(hero);
                }
                var l = RaidLayout.Current;
                var keys = new List<string>();
                foreach (var icon in TrayIcons.All) keys.Add(icon.Key);
                return new
                {
                    keys,
                    layout = new { left = V(l.TrayIconLeft), leftStep = l.TrayIconLeftSpacing, right = V(l.TrayIconRight), rightStep = l.TrayIconRightSpacing, hotSpot = V(l.TrayIconSize), origin = l.TrayCharX },
                    forced = RaidTrays.DevIcons.Count,
                    trays = trays.Describe()
                };
            });
            // What a hero really goes through, by the game's own calls, to see what the screen makes of it (THE
            // HERO CHANGES: back the save up first): {"hero":1,"damage":4}, {"heal":6}, {"stress":2}, {"calm":1},
            // {"wound":0.2} (DD2's wound: a share of the hero's health, -0.2 mends it), {"quirk":"<id>"} or
            // {"quirk":"positive|negative|disease"} for one at random, {"unquirk":"<id>"}.
            AgentBridge.Register("raid.effect", o =>
            {
                if (DungeonRun.Current == null || !Dd2.EstateSession.InHub) return "no expedition on screen";
                var hero = HeroOf(o);
                var actor = hero != 0u ? SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(hero) : null;
                if (actor == null || !actor.IsLiving) return "no such living hero";
                if (o["damage"] != null) actor.ApplyHealthDamage((float)o["damage"], false, false, actor, DeathType.EFFECT, SourceType.STORY, "dev", false);
                if (o["heal"] != null) actor.ApplyHealthHeal((float)o["heal"], false, SourceType.STORY, false);
                if (o["stress"] != null) actor.ApplyStressDamage((float)o["stress"], false, SourceType.STORY, "dev", 0u);
                if (o["calm"] != null) actor.ApplyStressHeal((float)o["calm"], SourceType.STORY);
                if (o["wound"] != null) actor.ChangeWoundPercent((float)o["wound"], SourceType.STORY);
                var quirks = actor.QuirkContainer;
                if (o["quirk"] != null && quirks != null)
                {
                    var id = (string)o["quirk"];
                    if (id == QuirkDefinition.QUIRK_POSITIVE_TAG || id == QuirkDefinition.QUIRK_NEGATIVE_TAG || id == "disease") quirks.AddRandomQuirks(null, id, 1, SourceType.QUIRK, null, 0u);
                    else if (SingletonMonoBehaviour<Library<string, QuirkDefinition>>.Instance.TryGetLibraryElement(id, out var quirk) && quirk != null) quirks.Add(quirk, SourceType.QUIRK, null, 0u);
                    else return "no such quirk: " + id;
                }
                if (o["unquirk"] != null && quirks != null)
                {
                    QuirkInstance found = null;
                    for (var i = 0; i < quirks.GetNumberOfInstances(); i++)
                        if (quirks.GetInstanceAtIndex(i)?.Definition?.Id == (string)o["unquirk"]) found = quirks.GetInstanceAtIndex(i);
                    if (found == null) return "the hero has no such quirk";
                    quirks.Remove(found, SourceType.HOSPITAL, null, 0u);
                }
                return new
                {
                    hero,
                    hp = actor.HpRounded,
                    hpMax = actor.CurrentHpMax,
                    stress = actor.Stress,
                    wound = actor.WoundPercent,
                    deathsDoor = actor.GetIsStatusActive(ActorStatusType.DEATHS_DOOR),
                    quirks = quirks?.GetIds(),
                    feedback = Feedback()
                };
            });
        }

        private static float[] V(UnityEngine.Vector2 v) => new[] { v.x, v.y };
    }
}
