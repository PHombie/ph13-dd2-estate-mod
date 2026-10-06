using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Code.Actor;
using Assets.Code.Dot;
using Assets.Code.Game;
using Assets.Code.Inputs;
using Assets.Code.Library;
using Assets.Code.Source;
using Assets.Code.Token;
using Assets.Code.UI;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dd2;
using DD2Estate.Dev;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// Dev bridge commands for the bag in a fight (<see cref="FightBag"/>).
    ///
    ///   python tools/bridge.py run fightbag.state
    ///   python tools/bridge.py run fightbag.show bag=true          (bag=false: the skills; no argument: the other one)
    ///   python tools/bridge.py run fightbag.use item=bandage       (or slot=3): the acting hero uses it, as a click does
    ///   python tools/bridge.py run fightbag.click slot=3 right=true     the click itself, with its notice
    ///   python tools/bridge.py run fightbag.hover slot=3           the card's tooltip (slot=-1: none; button=true: the button's)
    ///
    /// fightbag.state: whether a fight of an expedition is on, whether DD2's bar is up and what the bar's place
    /// shows, the acting hero (health, stress, what an item could act on), every stack of the bag with what a
    /// use would do now or why it is refused, the buffs items have put on heroes, DD2's flame, where everything
    /// stands on the 1920x1080 screen.
    ///
    /// For a test: fightbag.fight starts a hallway fight where the party stands (ambush=true: as a camp's night
    /// ambush); fightbag.afflict hero=&lt;guid&gt; dot=bleed|blight|&lt;dot id&gt; token=&lt;token id&gt; damage=5 stress=2
    /// hurts the hero (the acting one by default) so that an item has something to act on; fightbag.dots and
    /// fightbag.tokens list what DD2 has (filter=text).
    ///
    /// Looking at DD2's own screen: fightbag.hud (the bar's objects, where they stand, their pictures),
    /// fightbag.canvases (every canvas with its order), fightbag.sprites filter=text (loaded pictures by name),
    /// fightbag.keys key=tab (DD2's input actions bound to a key). fightbag.place x= y= scale= bx= by= bsize=
    /// order= icon= puts numbers in the place of the computed ones (reset=true: computed again).
    /// </summary>
    internal static class FightBagDev
    {
        internal static void Register()
        {
            AgentBridge.Register("fightbag.state", o => State());
            AgentBridge.Register("fightbag.show", o =>
            {
                var bag = (bool?)o["bag"] ?? !FightBag.Open;
                return new { shown = FightBag.Show(bag), open = FightBag.Open, bar = FightBag.BarShown };
            });
            AgentBridge.Register("fightbag.use", o =>
            {
                var run = DungeonRun.Current;
                if (run == null) return "no expedition";
                var slot = Slot(run, o);
                if (run.Bag.Slot(slot) == null) return "not in the bag";
                var hero = DungeonRun.ActingHero();
                var before = Hero(run, hero);
                var used = run.UseItemInFight(slot, out var message);
                return new { used, message, before, after = Hero(run, DungeonRun.ActingHero()), flame = DungeonRun.Torch(), buffs = FightBagBuffs.Describe() };
            });
            AgentBridge.Register("fightbag.click", o =>
            {
                var run = DungeonRun.Current;
                if (run == null || FightBag.View == null) return "no bag on screen";
                FightBag.DevClick(Slot(run, o), (bool?)o["right"] ?? true);
                return new { notice = FightBag.View.NoticeText, bag = ProvisionScreen.BagList(run.Bag) };
            });
            AgentBridge.Register("fightbag.hover", o =>
            {
                var view = FightBag.View;
                if (view == null) return "no bag on screen";
                if (o["button"] != null) view.DevHoverButton((bool)o["button"]);
                else view.DevHover(DungeonRun.Current != null && (o["slot"] != null || o["item"] != null) ? Slot(DungeonRun.Current, o) : -1);
                return new { hover = view.Hovered };
            });
            AgentBridge.Register("fightbag.place", o =>
            {
                if ((bool?)o["reset"] == true)
                {
                    FightBag.PlaceX = FightBag.PlaceY = FightBag.PlaceScale = FightBag.PlaceButtonX = FightBag.PlaceButtonY = FightBag.PlaceButtonSize = float.NaN;
                    FightBag.PlaceIcon = null;
                    FightBag.Rebuild();
                }
                if (o["x"] != null) FightBag.PlaceX = (float)o["x"];
                if (o["y"] != null) FightBag.PlaceY = (float)o["y"];
                if (o["scale"] != null) FightBag.PlaceScale = (float)o["scale"];
                if (o["bx"] != null) FightBag.PlaceButtonX = (float)o["bx"];
                if (o["by"] != null) FightBag.PlaceButtonY = (float)o["by"];
                if (o["bsize"] != null) FightBag.PlaceButtonSize = (float)o["bsize"];
                if (o["order"] != null)
                {
                    FightBag.Order = (int)o["order"];
                    if (FightBag.View != null) FightBag.View.Order = FightBag.Order;
                }
                if (o["icon"] != null) FightBag.SetIcon((string)o["icon"]);
                return Layout();
            });
            AgentBridge.Register("fightbag.fight", o => DungeonRun.Current != null ? DungeonRun.Current.DevFight((bool?)o["ambush"] ?? false) : "no expedition");
            AgentBridge.Register("fightbag.afflict", Afflict);
            AgentBridge.Register("fightbag.dots", o => Dots((string)o["filter"]));
            AgentBridge.Register("fightbag.tokens", o => Tokens((string)o["filter"]));
            AgentBridge.Register("fightbag.hud", o => Hud());
            AgentBridge.Register("fightbag.canvases", o => Canvases());
            AgentBridge.Register("fightbag.sprites", o => Sprites((string)o["filter"] ?? "invent"));
            AgentBridge.Register("fightbag.keys", o => Keys((string)o["key"] ?? "tab"));
        }

        private static int Slot(DungeonRun run, JObject o)
        {
            var slot = (int?)o["slot"] ?? -1;
            var item = InventoryContent.Items.Find((string)o["item"]);
            for (var i = 0; slot < 0 && item != null && i < run.Bag.SlotCount; i++)
                if (run.Bag.Slot(i)?.Item == item) slot = i;
            return slot;
        }

        private static object Hero(DungeonRun run, ActorInstance hero)
        {
            if (hero == null) return null;
            var state = run.FightStateOf(hero);
            return new
            {
                guid = hero.ActorGuid, name = hero.ActorName, cls = hero.ActorDataId, hp = hero.HpRounded, hpMax = hero.CurrentHpMax, stress = hero.Stress,
                state.Hurt, state.Bleeding, state.Blighted, state.Debuffed, state.Horrified, state.Stressed, state.Eaten,
                dots = hero.DotContainer.GetInstances().Select(d => d.Definition.Id + ":" + d.Definition.m_Type).ToList(),
                tokens = hero.TokenContainer.GetInstances().Select(t => t.Definition.Id + (t.Definition.IsNegative ? "(-)" : t.Definition.IsPositive ? "(+)" : "")).ToList(),
                resist = new { bleed = Resistance(hero, "bleed"), blight = Resistance(hero, "blight"), disease = Resistance(hero, "disease"), debuff = Resistance(hero, "debuff") }
            };
        }

        private static float? Resistance(ActorInstance hero, string key)
        {
            try { return hero.GetStatValue(ActorStatType.RESISTANCE, key, null, 0f, false, false); }
            catch (Exception) { return null; }
        }

        private static object Layout()
        {
            var view = FightBag.View;
            var bar = FightBag.BarRect;
            if (view == null) return new { bar = new[] { bar.xMin, bar.yMin, bar.xMax, bar.yMax } };
            var l = view.Layout;
            var last = view.CardRect(InventoryContent.NewBag().SlotCount - 1);
            var first = view.CardRect(0);
            return new
            {
                bar = new[] { bar.xMin, bar.yMin, bar.xMax, bar.yMax },
                grid = new[] { l.Grid.x, l.Grid.y }, scale = l.Scale,
                firstCard = new[] { first.xMin, first.yMin, first.xMax, first.yMax }, lastCard = new[] { last.xMin, last.yMin, last.xMax, last.yMax },
                button = new[] { l.Button.x, l.Button.y, l.ButtonSize }, cover = new[] { l.Cover.xMin, l.Cover.yMin, l.Cover.xMax, l.Cover.yMax },
                order = view.Order, sign = view.Skin.Sign != null ? view.Skin.Sign.name : null, plate = view.Skin.Plate != null ? view.Skin.Plate.name : null,
                mark = view.Skin.Mark != null ? view.Skin.Mark.name : null, screen = new[] { Screen.width, Screen.height }
            };
        }

        private static object State()
        {
            var run = DungeonRun.Current;
            var hero = DungeonRun.ActingHero();
            var items = new List<object>();
            if (run != null)
            {
                var state = run.FightStateOf(hero);
                var scene = run.FightSceneNow();
                for (var i = 0; i < run.Bag.SlotCount; i++)
                {
                    var stack = run.Bag.Slot(i);
                    if (stack == null) continue;
                    var use = run.FightItems.Plan(stack.Item, state, scene);
                    items.Add(new
                    {
                        slot = i, item = stack.Item.Key, amount = stack.Amount, kind = use.Kind.ToString(), usable = hero != null && use.Possible,
                        dark = FightItemRules.IsUsedItem(stack.Item) && !use.Possible,
                        refusal = use.Possible ? null : use.Refusal.ToString(), reason = hero != null ? FightBag.Reason(run, i) : "nobody's turn",
                        does = use.Possible ? Does(use) : null
                    });
                }
            }
            var presentation = FightBag.Dd2Presentation;
            return new
            {
                enabled = FightBag.Enabled.Value, onTab = FightBag.OnTab.Value, standIns = FightItemRules.StandInsInAFight,
                mode = GameModeMgr.CurrentMode?.GetName(), expedition = run != null, fighting = run?.Fighting, fight = FightBag.InFight,
                bar = FightBag.BarShown, alpha = FightBag.BarAlpha, open = FightBag.Open, shown = FightBag.View != null && FightBag.View.Shown, showsBag = FightBag.View != null && FightBag.View.ShowsBag,
                presentation = presentation != null ? presentation.CurrentPresentationState.ToString() : null, turnOpen = FightBag.TurnIsOpen,
                input = FightBag.Dd2Skills != null ? FightBag.Dd2Skills.CurrentInputState.ToString() : null,
                hero = run != null ? Hero(run, hero) : null,
                flame = DungeonRun.Torch(), light = run?.Exploration.Light,
                items, buffs = FightBagBuffs.Describe(), notice = FightBag.View?.NoticeText, hover = FightBag.View?.Hovered, layout = Layout()
            };
        }

        private static string Does(FightItemUse use)
        {
            var parts = new List<string>();
            if (use.CureBleed) parts.Add("bleed off");
            if (use.CureBlight) parts.Add("blight off");
            if (use.ClearDebuffs) parts.Add("debuffs off");
            if (use.ClearHorror) parts.Add("horror off");
            if (use.Buffs.Count > 0) parts.Add(string.Join(" ", use.Buffs.Select(b => b.Buff.SubType + "+" + b.Amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))) + " for " + use.Rounds + " rounds");
            if (use.Heal > 0) parts.Add("health +" + Mathf.RoundToInt((float)(use.Heal * 100)) + "%");
            if (use.StressHeal > 0) parts.Add("stress -" + use.StressHeal);
            if (use.Light > 0) parts.Add("flame +" + use.Light);
            return string.Join(", ", parts);
        }

        // ---- something for an item to act on ---------------------------------------------------------------

        private static object Afflict(JObject o)
        {
            var run = DungeonRun.Current;
            if (!EstateSession.Active) return "the Estate is not open";
            var hero = o["hero"] != null ? SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement((uint)o["hero"]) : DungeonRun.ActingHero();
            if (hero == null) return "no such hero (and nobody's turn)";
            var notes = new List<string>();
            if (o["damage"] != null)
            {
                // never the last point
                var damage = Mathf.Min(hero.HpRounded - 1f, (float)o["damage"]);
                if (damage >= 1f) hero.ApplyHealthDamage(damage, false, false, hero, DeathType.EFFECT, SourceType.DEBUG, "fightbag", false);
                notes.Add("health -" + damage);
            }
            if (o["stress"] != null)
            {
                hero.ApplyStressDamage((float)o["stress"], false, SourceType.DEBUG, "fightbag", 0u);
                notes.Add("stress +" + (float)o["stress"]);
            }
            var dot = (string)o["dot"];
            if (dot != null)
            {
                var library = SingletonMonoBehaviour<Library<string, DotDefinition>>.Instance;
                var definition = library.GetHasLibraryKey(dot) ? library.GetLibraryElement(dot) : library.GetFirstLibraryElement(d => d.m_Type == dot);
                if (definition == null) notes.Add("no dot " + dot);
                else
                {
                    hero.DotContainer.Add(definition, false, false, SourceType.DEBUG, "fightbag", hero);
                    notes.Add("dot " + definition.Id + " (" + definition.m_Type + ")");
                }
            }
            var token = (string)o["token"];
            if (token != null)
            {
                var library = SingletonMonoBehaviour<Library<string, TokenDefinition>>.Instance;
                var definition = library.GetHasLibraryKey(token) ? library.GetLibraryElement(token) : null;
                if (definition == null) notes.Add("no token " + token);
                else
                {
                    hero.TokenContainer.Add(definition, false, false, SourceType.DEBUG, "fightbag", hero.ActorGuid, (int?)o["amount"] ?? 1);
                    notes.Add("token " + definition.Id);
                }
            }
            return new { notes, hero = run != null ? Hero(run, hero) : (object)hero.ActorName };
        }

        private static object Dots(string filter)
        {
            var list = new List<object>();
            foreach (var d in SingletonMonoBehaviour<Library<string, DotDefinition>>.Instance.GetLibraryElements())
            {
                if (filter != null && d.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && (d.m_Type ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                list.Add(new
                {
                    id = d.Id, type = d.m_Type, tags = string.Join(",", d.Tags),
                    stress = d.m_Effects != null && d.m_Effects.Exists(e => e.m_StressDamage > 0f || e.m_StressDamageUpTo > 0f), heals = d.IsHoT
                });
            }
            return new { count = list.Count, types = SingletonMonoBehaviour<Library<string, DotDefinition>>.Instance.GetLibraryElements().Select(d => d.m_Type).Distinct().ToList(), dots = list.Take(80).ToList() };
        }

        private static object Tokens(string filter)
        {
            var list = new List<object>();
            foreach (var t in SingletonMonoBehaviour<Library<string, TokenDefinition>>.Instance.GetLibraryElements())
            {
                if (filter != null && t.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && !t.Tags.Any(tag => tag.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                list.Add(new { id = t.Id, tags = string.Join(",", t.Tags), negative = t.IsNegative, hidden = t.IsHidden });
            }
            return new { count = list.Count, tokens = list.Take(120).ToList() };
        }

        // ---- DD2's own screen ------------------------------------------------------------------------------

        private static string Path(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        private static float[] Box(RectTransform rt)
        {
            var r = FightBag.ScreenRect(rt);
            return new[] { Mathf.Round(r.xMin), Mathf.Round(r.yMin), Mathf.Round(r.xMax), Mathf.Round(r.yMax) };
        }

        private static object Describe(Transform t, int depth)
        {
            var rt = t as RectTransform;
            var image = t.GetComponent<Image>();
            var text = t.GetComponent<TMPro.TMP_Text>();
            var group = t.GetComponent<CanvasGroup>();
            var kids = new List<object>();
            if (depth > 0)
                foreach (Transform child in t)
                    kids.Add(Describe(child, depth - 1));
            return new
            {
                name = t.name, on = t.gameObject.activeInHierarchy, box = rt != null ? Box(rt) : null,
                sprite = image != null && image.sprite != null ? image.sprite.name + " " + image.sprite.rect.width + "x" + image.sprite.rect.height : null,
                colour = image != null ? "#" + ColorUtility.ToHtmlStringRGBA(image.color) : null,
                material = image != null && image.material != null && image.material != image.defaultMaterial ? image.material.name : null,
                text = text != null ? text.text : null, font = text != null && text.font != null ? text.font.name + " " + text.fontSize : null,
                alpha = group != null ? (float?)group.alpha : null,
                comps = string.Join(",", t.GetComponents<Component>().Where(c => c != null && !(c is Transform) && !(c is CanvasRenderer)).Select(c => c.GetType().Name)),
                kids = kids.Count > 0 ? kids : null
            };
        }

        private static object Hud()
        {
            FightBag.TurnIsOpen.ToString();     // finds the HUD if nobody has yet
            var skills = FightBag.Dd2Skills;
            var bar = FightBag.Dd2Bar;
            if (skills == null || bar == null) return "DD2's fight HUD is not there";
            var canvas = skills.GetComponentInParent<Canvas>();
            var root = canvas != null ? canvas.rootCanvas : null;
            var scaler = root != null ? root.GetComponent<CanvasScaler>() : null;
            var buttons = new List<object>();
            for (var i = 0; i < skills.SkillButtonCount; i++)
            {
                var button = skills.GetSkillButton(i);
                if (button != null && button.gameObject.activeInHierarchy) buttons.Add(Describe(button.transform, 4));
            }
            var chain = new List<object>();
            for (var t = skills.transform; t != null; t = t.parent)
            {
                var group = t.GetComponent<CanvasGroup>();
                var c = t.GetComponent<Canvas>();
                chain.Add(new
                {
                    name = t.name, box = t is RectTransform rt ? Box(rt) : null, alpha = group != null ? (float?)group.alpha : null,
                    canvas = c != null ? c.renderMode + " order " + c.sortingOrder + " layer " + c.sortingLayerName + (c.overrideSorting ? " override" : "") : null,
                    layout = t.GetComponent<LayoutGroup>() != null ? t.GetComponent<LayoutGroup>().GetType().Name : null
                });
            }
            return new
            {
                barPath = Path(bar.transform), skillsPath = Path(skills.transform),
                rootCanvas = root != null ? new
                {
                    name = root.name, mode = root.renderMode.ToString(), order = root.sortingOrder, layer = root.sortingLayerName, scale = root.scaleFactor,
                    camera = root.worldCamera != null ? root.worldCamera.name : null, plane = root.planeDistance,
                    scaler = scaler != null ? scaler.uiScaleMode + " " + scaler.referenceResolution + " match " + scaler.matchWidthOrHeight + " " + scaler.screenMatchMode : null
                } : null,
                chain,
                barTree = Describe(bar.transform, 2),
                skillsTree = Describe(skills.transform, 1),
                buttons,
                move = Traverse.Create(skills).Field("m_moveButtonBhv").GetValue<SkillButtonBhv>() is SkillButtonBhv move ? Describe(move.transform, 4) : null,
                pass = Traverse.Create(skills).Field("m_passButtonBhv").GetValue<SkillButtonBhv>() is SkillButtonBhv pass ? Describe(pass.transform, 4) : null,
                hudInventory = SingletonMonoBehaviour<GameUIBhv>.HasInstance() && Traverse.Create(SingletonMonoBehaviour<GameUIBhv>.Instance).Field("m_inventoryBtn").GetValue<Button>() is Button inv ? Describe(inv.transform, 3) : null,
                screen = new[] { Screen.width, Screen.height }
            };
        }

        private static object Canvases()
        {
            var list = new List<object>();
            foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (canvas == null || !canvas.isRootCanvas && !canvas.overrideSorting) continue;
                if (!canvas.gameObject.activeInHierarchy) continue;
                list.Add(new
                {
                    path = Path(canvas.transform), root = canvas.isRootCanvas, mode = canvas.renderMode.ToString(), order = canvas.sortingOrder, layer = canvas.sortingLayerName,
                    camera = canvas.worldCamera != null ? canvas.worldCamera.name + " depth " + canvas.worldCamera.depth : null, plane = canvas.planeDistance, enabled = canvas.enabled
                });
            }
            return list;
        }

        private static object Sprites(string filter)
        {
            var list = new List<object>();
            var seen = new HashSet<string>();
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sprite == null || sprite.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var key = sprite.name + "|" + (sprite.texture != null ? sprite.texture.name : "");
                if (!seen.Add(key)) continue;
                list.Add(new { name = sprite.name, texture = sprite.texture != null ? sprite.texture.name : null, size = sprite.rect.width + "x" + sprite.rect.height });
            }
            // who wears them: the pictures on screen objects (hidden ones too) that show a sprite of that name
            var users = new List<string>();
            foreach (var image in Resources.FindObjectsOfTypeAll<Image>())
                if (image != null && image.sprite != null && image.sprite.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 && users.Count < 40)
                    users.Add(image.sprite.name + " <- " + Path(image.transform));
            return new { count = list.Count, sprites = list.Take(150).ToList(), users };
        }

        private static object Keys(string key)
        {
            var list = new List<object>();
            if (!SingletonMonoBehaviour<InputSystemBhv>.HasInstance()) return "no input system";
            var input = Traverse.Create(SingletonMonoBehaviour<InputSystemBhv>.Instance).Field("m_unityInput").GetValue<UnityEngine.InputSystem.PlayerInput>();
            if (input == null || input.actions == null) return "no player input";
            foreach (var map in input.actions.actionMaps)
                foreach (var action in map.actions)
                    foreach (var binding in action.bindings)
                    {
                        var path = binding.effectivePath ?? "";
                        if (path.IndexOf("/" + key, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        list.Add(new { map = map.name, mapOn = map.enabled, action = action.name, on = action.enabled, path, groups = binding.groups });
                    }
            return new { key, current = input.currentActionMap != null ? input.currentActionMap.name : null, bindings = list };
        }
    }
}
