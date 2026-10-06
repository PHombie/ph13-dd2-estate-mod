using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Assets.Code.Actor;
using DD2Estate.Dd1;
using DD2Estate.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DD2Estate.Estate
{
    /// <summary>
    /// What the Guild, the Blacksmith and the Survivalist share besides their frame (<see cref="HeroActionFrame"/>):
    /// where DD1 keeps a hero class's own art and words, DD1's words for what a purchase asks beyond its price,
    /// and the order of the roster column for the slot's brackets.
    /// </summary>
    internal static class HeroActionUi
    {
        // DD1 keeps a class in heroes/<class>; the classes it got later lie in a folder of their own that is
        // laid out like the game's root (the folders Core/CampingRules reads those classes' camping skills
        // from). A class the install does not have is not there.
        private static readonly string[] ClassRoots =
        {
            "",
            "dlc/445700_musketeer/",
            "dlc/580100_crimson_court/features/flagellant/",
            "dlc/702540_shieldbreaker/",
            "dlc/4964110_fires_edge/features/duelist/",
            "dlc/4964110_fires_edge/features/runaway/"
        };

        // The string tables that speak of the classes: the game's own, and those of the classes it got later.
        private static readonly string[] ClassTables =
        {
            "localization/heroes.string_table.xml",
            "dlc/580100_crimson_court/localization/CC.string_table.xml",
            "dlc/702540_shieldbreaker/localization/shieldbreaker.string_table.xml",
            "dlc/4964110_fires_edge/features/duelist/localization/duelist.string_table.xml",
            "dlc/4964110_fires_edge/features/runaway/localization/runaway.string_table.xml"
        };

        // hero_class_name_<class>, action_verbose_body_<building>_<class>, <class>_weapon_<n>, <class>_armour_<n>
        private static readonly Regex ClassWord = new Regex("^(?:hero_class_name_|action_verbose_).+|.+_(?:weapon|armour)_\\d+$");

        private static readonly Dictionary<string, string> Folders = new Dictionary<string, string>();
        private static Dictionary<string, string> _words;

        // ---- a hero's class in DD1 ---------------------------------------------------------------------

        /// <summary>The DD1 class a DD2 hero stands for: the class id, which the two games share for every hero both have.</summary>
        public static string ClassOf(ActorInstance actor)
        {
            if (actor == null) return "";
            var byClass = actor.ActorDataClass != null ? actor.ActorDataClass.Id : null;
            // ActorDataId can name a form of the class; the class definition's id is tried first.
            if (!string.IsNullOrEmpty(byClass) && (ClassFolder(byClass) != null || ClassFolder(actor.ActorDataId) == null)) return byClass;
            return actor.ActorDataId ?? "";
        }

        /// <summary>The class's folder in the DD1 install ("heroes/crusader", "dlc/.../heroes/duelist"); null when DD1 has no such class.</summary>
        public static string ClassFolder(string cls)
        {
            if (string.IsNullOrEmpty(cls) || !Dd1Install.Found) return null;
            if (Folders.TryGetValue(cls, out var folder)) return folder;
            try
            {
                foreach (var root in ClassRoots)
                {
                    var path = Dd1Install.PathOf(root + "heroes/" + cls);
                    if (path == null || !Directory.Exists(path)) continue;
                    folder = root + "heroes/" + cls;
                    break;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("DD1's folder of the class " + cls + " was not found: " + e.Message); }
            Folders[cls] = folder;
            return folder;
        }

        /// <summary>A file of the hero's class in the DD1 install ("crusader_guild_header.png", "icons_equip/eqp_weapon_0.png"); null when there is none.</summary>
        public static string ClassFile(ActorInstance actor, string file)
        {
            var folder = ClassFolder(ClassOf(actor));
            return folder != null && Dd1Install.Exists(folder + "/" + file) ? folder + "/" + file : null;
        }

        /// <summary>
        /// DD1's English words about a class (hero_class_name_&lt;class&gt;, action_verbose_body_&lt;building&gt;_&lt;class&gt;,
        /// &lt;class&gt;_weapon_&lt;n&gt;, &lt;class&gt;_armour_&lt;n&gt;), without colour marks and line breaks; null when DD1 has none.
        /// </summary>
        public static string Words(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var text = WindowText.Plain(id);
            if (text != null) return text;
            if (_words == null) _words = ReadWords();
            return _words.TryGetValue(id, out text) ? Dd1Strings.Format(text) : null;
        }

        private static Dictionary<string, string> ReadWords()
        {
            var words = new Dictionary<string, string>();
            foreach (var table in ClassTables)
            {
                var path = Dd1Install.PathOf(table);
                if (path == null || !File.Exists(path)) continue;
                try
                {
                    var all = new Dictionary<string, string>();
                    Dd1Strings.ReadEnglish(File.ReadLines(path), all, null);
                    foreach (var pair in all)
                        if (!words.ContainsKey(pair.Key) && ClassWord.IsMatch(pair.Key)) words[pair.Key] = pair.Value;
                }
                catch (Exception e) { Plugin.Log.LogWarning("DD1 string table " + table + " could not be read: " + e.Message); }
            }
            return words;
        }

        // ---- what a purchase asks ------------------------------------------------------------------------

        /// <summary>
        /// What a purchase of DD1's hero upgrade files asks beyond its price, in DD1's words: one line per
        /// building step ("Weaponsmithing Level 2": upgrade_prerequisite_requirement_tooltip_body_format) and one
        /// for the hero's resolve ("Hero Resolve Level: 3": upgrade_prerequisite_resolve_level_tooltip_body_format).
        /// <paramref name="unmetOnly"/> leaves out what the estate and the hero already have.
        /// </summary>
        public static List<string> Asks(UpgradeHeroRules.Purchase purchase, uint guid, bool unmetOnly, bool colours = false)
        {
            var lines = new List<string>();
            if (purchase == null) return lines;
            foreach (var need in purchase.Needs)
            {
                var met = UpgradeRules.Has(need);
                if (met && unmetOnly) continue;
                var tree = UpgradeRules.Find(need.Tree);
                var level = tree != null ? tree.IndexOf(need.Code) + 1 : 0;
                var line = level > 0
                    ? WindowText.Format("upgrade_prerequisite_requirement_tooltip_body_format", "%s Level %d", UpgradeText.TreeName(need.Tree), level)
                    : UpgradeText.NeedName(need);
                lines.Add(colours && !met ? Unmet(line) : line);
            }
            if (purchase.ResolveLevel > 0)
            {
                var met = Resolve.Level(guid) >= purchase.ResolveLevel;
                if (!met || !unmetOnly)
                {
                    var line = WindowText.Format("upgrade_prerequisite_resolve_level_tooltip_body_format", "Hero Resolve Level: %d", purchase.ResolveLevel);
                    lines.Add(colours && !met ? Unmet(line) : line);
                }
            }
            return lines;
        }

        /// <summary>
        /// DD1's block of a tooltip for what a purchase asks: "Prerequisites:" (upgrade_prerequisite_tooltip_title)
        /// and a line each, what is not met in DD1's colour for it (upgrade_tree_prerequisite_not_purchased).
        /// Null when the purchase asks for nothing.
        /// </summary>
        public static string Prerequisites(UpgradeHeroRules.Purchase purchase, uint guid)
        {
            var lines = Asks(purchase, guid, false, true);
            if (lines.Count == 0) return null;
            return (WindowText.Plain("upgrade_prerequisite_tooltip_title") ?? "Prerequisites:") + "\n" + string.Join("\n", lines);
        }

        /// <summary>
        /// What a purchase still waits for, on one line and in the same words ("Prerequisites: Weaponsmithing
        /// Level 2, Hero Resolve Level: 3"); null when only its price is left.
        /// </summary>
        public static string Missing(UpgradeHeroRules.Purchase purchase, uint guid)
        {
            var lines = Asks(purchase, guid, true);
            return lines.Count == 0 ? null : (WindowText.Plain("upgrade_prerequisite_tooltip_title") ?? "Prerequisites:") + " " + string.Join(", ", lines);
        }

        private static string Unmet(string line)
        {
            return "<color=#" + ColorUtility.ToHtmlStringRGB(Dd1Fonts.Colour("upgrade_tree_prerequisite_not_purchased", UiKit.Harmful)) + ">" + line + "</color>";
        }

        // ---- the roster's order --------------------------------------------------------------------------

        /// <summary>The living heroes in the order the roster column shows them now.</summary>
        public static List<uint> RosterOrder()
        {
            var order = new List<uint>();
            var list = RosterPanel.CanvasRoot != null ? RosterPanel.CanvasRoot.Find("Roster/List") : null;
            if (list != null)
            {
                for (var i = 0; i < list.childCount; i++)
                {
                    var row = list.GetChild(i).GetComponent<RosterRowClick>();
                    if (row != null && row.gameObject.activeSelf && UpgradeUi.Hero(row.Guid) != null && !order.Contains(row.Guid)) order.Add(row.Guid);
                }
            }
            if (order.Count == 0)
                foreach (var guid in RosterLifecycle.LivingGuids()) order.Add(guid);
            return order;
        }
    }

    /// <summary>
    /// The two brackets of a hero slot (hero_slot.positive_prev / _next, the steps to the hero before and after
    /// in the roster): there only while a hero is in the slot and the pointer rests on the slot or on a bracket.
    /// DD1's own Guild with a hero in shows none at rest. GUESS: that the pointer is what brings them (no
    /// frame has it on the slot); a place 20 px either side of the slot counts, where the brackets stand.
    /// </summary>
    internal class HeroSlotBrackets : MonoBehaviour
    {
        private const float Reach = 20f;

        public GameObject Prev, Next;
        /// <summary>A hero is in the slot.</summary>
        public bool Has;

        private void Update()
        {
            var show = Has && Near();
            if (Prev != null && Prev.activeSelf != show) Prev.SetActive(show);
            if (Next != null && Next.activeSelf != show) Next.SetActive(show);
        }

        private void OnDisable()
        {
            if (Prev != null) Prev.SetActive(false);
            if (Next != null) Next.SetActive(false);
        }

        private bool Near()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return false;
            var rect = (RectTransform)transform;
            // the hamlet's canvas is drawn over the screen: no camera stands between
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, mouse.position.ReadValue(), null, out var local)) return false;
            var area = rect.rect;
            return local.x >= area.xMin - Reach && local.x <= area.xMax + Reach && local.y >= area.yMin && local.y <= area.yMax;
        }
    }

    /// <summary>Tells a hero slot when a hero dragged from the roster hangs over it, and when that ends.</summary>
    internal class HeroSlotHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IDropHandler
    {
        public Action<bool> Changed;

        public bool Over { get; private set; }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (eventData.dragging && eventData.pointerDrag != null && eventData.pointerDrag.GetComponent<HeroDrag>() != null) Set(true);
        }

        public void OnPointerExit(PointerEventData eventData) => Set(false);

        // Let go over the slot: the roster's row hears of it next (HeroDrag.OnEndDrag) and hands the hero over.
        public void OnDrop(PointerEventData eventData) => Set(false);

        private void OnDisable() => Set(false);

        private void Set(bool over)
        {
            if (Over == over) return;
            Over = over;
            Changed?.Invoke(over);
        }
    }
}
