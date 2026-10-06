using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.Dungeon;

namespace DD2Estate.Dev
{
    /// <summary>Named test commands for the dev bridge ({"cmd":"run","name":"..."}).</summary>
    internal static class DevCommands
    {
        private static object DungeonTrinkets()
        {
            var run = DungeonRun.Current;
            if (run == null) return "no expedition";
            var party = Provisioning.Party();
            var bag = new System.Collections.Generic.List<object>();
            for (var slot = 0; slot < run.Bag.SlotCount; slot++)
            {
                var stack = run.Bag.Slot(slot);
                if (stack == null || stack.Item.Type != DD2Estate.Core.ItemTypes.Trinket) continue;
                var id = stack.Item.Id;
                var wearers = new System.Collections.Generic.List<object>();
                foreach (var hero in party)
                    wearers.Add(new { guid = hero.ActorGuid, refusal = DD2Estate.Estate.RealmInventory.WearFromBagRefusal(hero.ActorGuid, slot) });
                bag.Add(new { slot, id, name = DD2Estate.Estate.Trinkets.Name(id), grade = DD2Estate.Estate.Trinkets.Grade(id), wearers });
            }
            var heroes = new System.Collections.Generic.List<object>();
            foreach (var hero in party)
                heroes.Add(new { guid = hero.ActorGuid, name = hero.ActorName, cls = hero.ActorDataId, worn = DD2Estate.Estate.RealmInventory.Worn(hero.ActorGuid) });
            return new { selected = DungeonHud.Selected, free = run.Bag.FreeSlots, bag, heroes, onScroll = run.LootOnScroll.Count };
        }

        public static void Register()
        {
            AgentBridge.Register("estate.enter", o =>
            {
                Plugin.Host.StartCoroutine(EstateSession.Enter());
                return "entering";
            });
            AgentBridge.Register("estate.exit", o =>
            {
                EstateSession.ExitToMenu();
                return "exiting";
            });
            AgentBridge.Register("estate.state", o => EstateSession.Describe());
            AgentBridge.Register("estate.saveinfo", o => EstatePersistence.Describe());
            AgentBridge.Register("estate.save", o =>
            {
                EstatePersistence.SaveNow((string)o["reason"] ?? "dev");
                return EstatePersistence.Describe();
            });
            AgentBridge.Register("estate.wipe", o =>
            {
                if (EstateSession.Active || EstateSession.Starting) return "leave the Estate first";
                if (!EstateProfile.Activate()) return "no Estate profile";
                try { return EstatePersistence.DeleteSave() ? "deleted" : "nothing to delete"; }
                finally { EstateProfile.Deactivate(); }
            });
            // Every camera, how it clears, and whether the Estate holds it at black (EstateSky).
            AgentBridge.Register("estate.cameras", o => EstateSky.Describe());
            // What could be filling the screen right now: large UI graphics and large renderers.
            AgentBridge.Register("estate.screen", o => EstateSky.Screen());
            // Every frame looked at for the blue of a sky; what drew the ones that have it (SkyTrap).
            AgentBridge.Register("estate.skytrap", o => SkyTrap.Set((bool?)o["on"], (string)o["folder"], (int?)o["film"]));
            AgentBridge.Register("input.click", o =>
            {
                Plugin.Host.StartCoroutine(DevInput.Click((float)o["x"], (float)o["y"]));
                return new { focused = UnityEngine.Application.isFocused };
            });
            // The pointer to a place on screen (pixels from the bottom left), without a click: what hovering shows.
            AgentBridge.Register("input.move", o =>
            {
                DevInput.Move((float)o["x"], (float)o["y"]);
                return new { focused = UnityEngine.Application.isFocused };
            });
            // The same as a pointer resting there, told to the UI directly: works while the game is not in front.
            AgentBridge.Register("ui.hover", o => DevInput.Hover((float)o["x"], (float)o["y"]));
            AgentBridge.Register("input.hold", o =>
            {
                var key = (UnityEngine.InputSystem.Key)System.Enum.Parse(typeof(UnityEngine.InputSystem.Key), (string)o["key"], true);
                Plugin.Host.StartCoroutine(DevInput.Hold(key, (float?)o["seconds"] ?? 0.2f));
                return new { focused = UnityEngine.Application.isFocused };
            });
            // A bare dungeon with no quest behind it. The party gets the standard kit, on the house unless free=false;
            // kit=false sets out with empty hands.
            AgentBridge.Register("estate.embark", o =>
            {
                var length = (int?)o["length"] ?? 1;
                var bag = ((bool?)o["kit"] ?? true) ? Provisioning.StandardBag(length, (bool?)o["free"] ?? true) : null;
                DungeonRun.Start((string)o["dungeon"] ?? "crypts", (string)o["quest"] ?? DD2Estate.Core.QuestTypes.Explore, length, (int?)o["tier"] ?? 1, null, bag);
                return "dungeon started";
            });
            AgentBridge.Register("dungeon.state", o =>
            {
                var run = DungeonRun.Current;
                var x = run?.Exploration;
                var reachable = new System.Collections.Generic.List<int>();
                if (x != null)
                    for (var i = 0; i < x.Map.Rooms.Count; i++)
                        if (x.CanMoveTo(i)) reachable.Add(i);
                return new
                {
                    view = EstateSession.View.ToString(),
                    busy = run?.Busy,
                    room = x?.RoomId,
                    hallway = x?.HallwayId,
                    segment = x?.Segment,
                    toward = x?.TowardRoomId,
                    pending = x?.Pending.ToString(),
                    status = x?.Status.ToString(),
                    light = x?.Light,
                    // the light on DD2's side: its flame now, as the fight in progress began, before and after the last fight
                    books = run?.LightBooks(),
                    progress = x?.Progress,
                    required = x?.Map.Objective?.Required,
                    complete = x?.ObjectiveComplete,
                    reachable,
                    prompt = DungeonHud.PromptText,
                    options = DungeonHud.PromptOptions,
                    picking = DungeonHud.Picking,
                    torches = run?.Torches,
                    food = run != null ? run.Bag.Count(InventoryContent.Items.Food) : (int?)null,
                    leadX = CorridorView.Instance != null ? CorridorView.Instance.LeadX : -1f,
                    tile = CorridorView.Instance != null ? CorridorView.Instance.CurrentTile : -1,
                    length = CorridorView.Instance != null ? CorridorView.Instance.Length : -1f
                };
            });
            AgentBridge.Register("estate.boss", o =>
            {
                DungeonContent.PickBoss((string)o["dungeon"], (string)o["boss"], (string)o["tier"] ?? "apprentice", out var config, out var arena);
                if (config == null) return "no fight in the data";
                EstateSession.StartBattle(config, arena);
                return config + " @ " + arena;
            });
            AgentBridge.Register("quests.list", o =>
            {
                var list = new System.Collections.Generic.List<object>();
                foreach (var q in DD2Estate.Estate.QuestBoard.Current())
                    list.Add(new { q.Id, q.Name, q.Dungeon, q.Type, q.Boss, q.Tier, q.LengthName, q.Plot, reward = DD2Estate.Estate.QuestBoard.RewardText(q) });
                return list;
            });
            // Embarks on an offer with the standard kit, paid from the purse (free=true: on the house), no provision screen.
            AgentBridge.Register("quests.embark", o => DD2Estate.Estate.QuestPanel.EmbarkOffer((int?)o["index"] ?? 0, (bool?)o["free"] ?? false));
            // The provision screen, as the Embark button opens it, and its controls.
            AgentBridge.Register("quests.provision", o => DD2Estate.Estate.QuestPanel.ProvisionOffer((int?)o["index"] ?? 0));
            AgentBridge.Register("provision.state", o => ProvisionScreen.Describe());
            AgentBridge.Register("provision.buy", o => ProvisionScreen.DevBuy((string)o["item"], (int?)o["amount"] ?? 1));
            AgentBridge.Register("provision.sell", o => ProvisionScreen.DevSell((string)o["item"], (int?)o["amount"] ?? 1));
            AgentBridge.Register("provision.kit", o => ProvisionScreen.DevPress("kit"));
            AgentBridge.Register("provision.setout", o => ProvisionScreen.DevPress("setout"));
            AgentBridge.Register("provision.back", o => ProvisionScreen.DevPress("back"));
            AgentBridge.Register("dungeon.goto", o =>
            {
                DungeonRun.Current?.TravelTo((int)o["room"]);
                return "ok";
            });
            // While the HUD waits for a stack to drop, any answer declines: the "bag is full" question comes back.
            AgentBridge.Register("dungeon.answer", o => DungeonHud.Picking ? DungeonHud.Pick(-1) : DungeonHud.Answer((int?)o["option"] ?? 0));
            AgentBridge.Register("dungeon.torch", o =>
            {
                DungeonRun.Current?.UseTorch();
                return DungeonRun.Current?.Torches;
            });
            AgentBridge.Register("dungeon.bag", o =>
            {
                var run = DungeonRun.Current;
                if (run == null) return "no expedition";
                var heroes = new System.Collections.Generic.List<object>();
                foreach (var hero in Provisioning.Party())
                    heroes.Add(new { guid = hero.ActorGuid, name = hero.ActorName, hp = hero.HpRounded, hpMax = hero.CurrentHpMax, stress = hero.Stress, eaten = run.Eaten(hero.ActorGuid) });
                return new { slots = run.Bag.SlotCount, free = run.Bag.FreeSlots, items = ProvisionScreen.BagList(run.Bag), heroes };
            });
            // item: "torch", "food", "gold", "ruby", "heirloom:crest"... amount in DD1 units
            AgentBridge.Register("dungeon.give", o =>
            {
                var run = DungeonRun.Current;
                var item = InventoryContent.Items.Find((string)o["item"]);
                if (run == null || item == null) return run == null ? "no expedition" : "no such item";
                var amount = (int?)o["amount"] ?? 1;
                return new { item = item.Key, added = amount - run.Give(item, amount), bag = ProvisionScreen.BagList(run.Bag) };
            });
            // slot=N or item=name; hero=guid for what a hero takes (default: the one who needs it most)
            AgentBridge.Register("dungeon.use", o =>
            {
                var run = DungeonRun.Current;
                if (run == null) return "no expedition";
                var slot = (int?)o["slot"] ?? -1;
                var item = InventoryContent.Items.Find((string)o["item"]);
                for (var i = 0; slot < 0 && item != null && i < run.Bag.SlotCount; i++)
                    if (run.Bag.Slot(i)?.Item == item) slot = i;
                var stack = run.Bag.Slot(slot);
                if (stack == null) return "not in the bag";
                var guid = (uint?)o["hero"] ?? 0u;
                if (guid == 0u && run.NeedsHero(slot))
                {
                    var calm = InventoryContent.Rules.UseOf(stack.Item).Kind == DD2Estate.Core.ItemUseKind.StressHeal;
                    var worst = float.MinValue;
                    foreach (var hero in Provisioning.Party())
                    {
                        var need = calm ? hero.Stress : 1f - hero.HpRounded / UnityEngine.Mathf.Max(1f, hero.CurrentHpMax);
                        if (need <= worst) continue;
                        worst = need;
                        guid = hero.ActorGuid;
                    }
                }
                var used = run.UseItem(slot, guid, out var message);
                return new { used, message, hero = guid };
            });
            // Answers the HUD's "pick a stack to drop"; outside of that it simply throws a stack away.
            AgentBridge.Register("dungeon.drop", o =>
            {
                var run = DungeonRun.Current;
                var slot = (int?)o["slot"] ?? -1;
                if (run == null || run.Bag.Slot(slot) == null) return "nothing in that slot";
                if (DungeonHud.Picking) return DungeonHud.Pick(slot) ? "picked" : "not picking";
                var dropped = run.Bag.Clear(slot);
                return "dropped " + dropped.Amount + " " + dropped.Item.Key;
            });
            // The owner's choices that are one switch each (docs/recon/inventory-unification.md 4.6); not saved, back
            // to the defaults when the game starts. Q8 {"standins":false}: the mod's stand-in effects of bandage,
            // antivenom, herbs, holy water and laudanum on a hero outside a fight are out (torch, camp and food
            // stay). Q6 {"keeptrinkets":true}: a lost party's trinkets are sent home instead of being lost with it.
            AgentBridge.Register("dungeon.choices", o =>
            {
                if (o["standins"] != null) DD2Estate.Core.InventoryRaidRules.StandInSupplyUses = (bool)o["standins"];
                if (o["keeptrinkets"] != null) DungeonRun.KeepTrinketsOfALostParty = (bool)o["keeptrinkets"];
                return new
                {
                    standInSupplyUses = DD2Estate.Core.InventoryRaidRules.StandInSupplyUses,
                    keepTrinketsOfALostParty = DungeonRun.KeepTrinketsOfALostParty,
                    trinketsOneOfEach = DD2Estate.Estate.Trinkets.OneOfEach,
                    payForStrayCombatItems = Dd2Sweep.PayForStrayCombatItems,
                    lightFollowsTheFight = DungeonRun.LightFollowsTheFight,
                    // gold is DD1's own number; DD2's gold, should any turn up, is worth this much of it
                    dd2GoldWorth = DD2Estate.Estate.EstateState.Dd2GoldWorth,
                    startingGold = DD2Estate.Estate.EstateState.StartingGold
                };
            });
            // The trinkets of the expedition: those lying in the bag (slot, DD2 id, the DD1 rarity each was found
            // under, and for every hero of the party whether they could wear it) and what each hero wears.
            AgentBridge.Register("dungeon.trinkets", o => DungeonTrinkets());
            // Puts the trinket in a slot of the bag on a hero of the party, as a click on its card does:
            // {"slot":3,"hero":5}; {"into":1} names the hero's slot, and what is worn there takes the trinket's place
            // in the bag. Without a hero: the one the screen has selected. Not in a fight (DD1's own refusal).
            AgentBridge.Register("dungeon.wear", o =>
            {
                var guid = (uint?)o["hero"] ?? DungeonHud.Selected;
                var refusal = DD2Estate.Estate.RealmInventory.WearFromBag(guid, (int?)o["slot"] ?? -1, (int?)o["into"] ?? -1);
                return refusal != null ? (object)refusal : DungeonTrinkets();
            });
            // Takes a trinket off a hero of the party into the bag, as a click on it in the hero panel does:
            // {"hero":5,"id":"<DD2 id>"} or {"hero":5,"index":0} for what the hero wears in that slot.
            AgentBridge.Register("dungeon.takeoff", o =>
            {
                var guid = (uint?)o["hero"] ?? DungeonHud.Selected;
                var id = (string)o["id"];
                if (id == null)
                {
                    var worn = DD2Estate.Estate.RealmInventory.Worn(guid);
                    var index = (int?)o["index"] ?? 0;
                    id = index >= 0 && index < worn.Count ? worn[index] : null;
                    if (id == null) return "the hero wears nothing in that slot";
                }
                var refusal = DD2Estate.Estate.RealmInventory.TakeOffToBag(guid, id);
                return refusal != null ? (object)refusal : DungeonTrinkets();
            });
            AgentBridge.Register("dungeon.leave", o =>
            {
                DungeonRun.Current?.Leave();
                return "ok";
            });
            AgentBridge.Register("dungeon.map", o => DungeonRun.Current?.Exploration.Map.ToJson().ToString(Newtonsoft.Json.Formatting.None));
            AgentBridge.Register("estate.hamlet", o =>
            {
                CorridorView.Hide();
                EstateSession.View = EstateSession.Screen.Hamlet;
                return "hamlet shown";
            });
            // The mod's own pictures in place of DD1's (Dd1Install: <plugin>/assets/<DD1 path>.png): where they are read
            // from and which have been used; {"on":false} shows DD1's own again, {"on":true} the mod's. The area on
            // screen is built anew with it.
            AgentBridge.Register("art.overrides", o =>
            {
                if (o["on"] != null)
                {
                    Dd1Install.OverridesOn = (bool)o["on"];
                    Dd1Install.ForgetPictures();
                    if (CorridorView.Instance != null) CorridorView.Instance.Rebuild();
                }
                return new { on = Dd1Install.OverridesOn, folder = Dd1Install.OverrideRoot, replaced = System.Linq.Enumerable.ToArray(Dd1Install.ReplacedSoFar) };
            });
            AgentBridge.Register("corridor.rebuild", o =>
            {
                CorridorView.Instance.Rebuild();
                return "rebuilt";
            });
            AgentBridge.Register("corridor.walk", o =>
            {
                CorridorView.Instance.DevWalk((float?)o["dir"] ?? 1f, (float?)o["seconds"] ?? 1f);
                return new { x = CorridorView.Instance.LeadX, tile = CorridorView.Instance.CurrentTile, length = CorridorView.Instance.Length };
            });
            // The party walks up to the door at a hallway's end ({"end":false}: the door it came through) and uses
            // it, as W or a click on the door does.
            AgentBridge.Register("corridor.door", o =>
            {
                var view = CorridorView.Instance;
                if (view == null) return "no corridor";
                var door = view.DoorAt((bool?)o["end"] ?? true);
                if (door == null) return "no door there";
                view.Click(door);
                return new { door = door.Tile, room = door.Room, x = view.LeadX, tile = view.CurrentTile };
            });
            AgentBridge.Register("estate.battle", o =>
            {
                EstateSession.StartBattle((string)o["config"], (string)o["arena"]);
                return "battle starting";
            });
        }
    }
}
