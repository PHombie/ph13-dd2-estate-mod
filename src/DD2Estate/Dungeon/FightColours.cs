using System;
using System.Collections.Generic;
using Assets.Code.Game;
using Assets.Code.Presentation;
using Assets.Code.Utils;
using BepInEx.Configuration;
using DD2Estate.Dd2;
using DD2Estate.Dev;
using DD2Estate.Estate;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// DD1's red for harm in a fight of the Estate (the owner's word, 2026-10-06: "red instead of blue in the
    /// tooltips of the fight itself: do it"). DD2 writes what is bad in its blue; on the hero's sheet the Estate turns
    /// that into DD1's red already (<see cref="SheetColours"/>). In a fight the same is done to:
    /// <list type="bullet">
    /// <item>the words of DD2's own texts (the retreat button's "Flame -15", a skill's "Target:" line and debuff
    /// numbers, a token's name): DD2 asks its string table for the colours "harmful", "debuff", "stat_debuff"
    /// and the like, and the table answers with DD1's red for as long as an Estate session lasts
    /// (<see cref="SheetWritesHarmInRed"/> asks <see cref="WordsOn"/>). The whole session and not the fight
    /// alone: a fight's HUD writes its lines in the frame the fight comes up, before anything could notice
    /// that a fight is on (seen: "Target:" and a token's name stayed blue while the retreat button's tooltip,
    /// written when the pointer comes, was red);</item>
    /// <item>what a tooltip's box draws in one of DD2's exact blues of harm (<see cref="Tooltip"/>);</item>
    /// <item>the pips of a skill's targets over the skill bar (DD2 paints them anew for every skill pointed at);</item>
    /// <item>the line under the skill bar that says why a skill cannot be used.</item>
    /// </list>
    /// The pictures of the negative tokens stay DD2's blue art, and so does everything that is blue without
    /// meaning harm (the turn order's side marker, a hovered portrait's frame): nothing but tooltips and that
    /// one line is looked at. Whatever is turned is remembered and gets its own colour back when the fight is
    /// over: DD2's tooltips are pooled and the fight's HUD stays loaded, and the regular game shares both.
    /// </summary>
    [EstateModule]
    internal static class FightColours
    {
        private const string CannotUseLine = "SkillValidLabel";     // Arena/CombatUI/CombatInterfaceBar/skill_selection_panel
        private const string TargetPips = "SkillPositionGroups";    // .../NameAndGroupsCanvas/SkillPositionGroups
        private const int LateFrames = 4;                           // DD2 fills some of a tooltip a frame or two late
        private const float LineEvery = 0.5f;

        private static ConfigEntry<bool> _enabled;
        private static bool _held;      // dev: switched off by the bridge, to look at a fight as DD2 has it
        private static readonly Dictionary<Graphic, Color> Was = new Dictionary<Graphic, Color>();
        private static readonly List<KeyValuePair<GameObject, int>> Fresh = new List<KeyValuePair<GameObject, int>>();
        private static readonly List<Graphic> Scratch = new List<Graphic>();
        private static TextMeshProUGUI _line;
        private static GameObject _pips;
        private static float _nextLine;

        /// <summary>A fight of the Estate is on screen and harm is red in it.</summary>
        public static bool On { get; private set; }

        /// <summary>An Estate session is on: DD2's string table gives DD1's red for its colours of harm.</summary>
        public static bool WordsOn => _enabled != null && _enabled.Value && !_held && EstateSession.Active;

        private static void Register()
        {
            _enabled = Plugin.Settings.Bind("Fight", "RedForHarm", true,
                "In a fight of the Estate, what DD2's own tooltips and the line under the skill bar mark as bad in DD2's blue is DD1's red. Off: DD2's own colours.");
            var go = new GameObject("DD2Estate.FightColours") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Watcher>();

            // The state; {"on":false} shows the fight as DD2 has it (until {"on":true}): a dev switch, not saved.
            AgentBridge.Register("fight.colours", o =>
            {
                if (o["on"] != null) _held = !(bool)o["on"];
                return new
                {
                    on = On, words = WordsOn, enabled = _enabled.Value && !_held, wanted = Wanted(), pips = _pips != null,
                    harm = "#" + ColorUtility.ToHtmlStringRGB(SheetColours.Harm),
                    turned = Was.Count, line = _line != null ? "#" + ColorUtility.ToHtmlStringRGB(_line.color) : null
                };
            });
        }

        private static bool Wanted()
        {
            if (_enabled == null || !_enabled.Value || _held || !EstateSession.Active) return false;
            var mode = GameModeMgr.CurrentMode;
            return mode != null && mode.GetName() == "COMBAT";
        }

        /// <summary>A tooltip's box came up: it is looked at now and for the next few frames.</summary>
        public static void Tooltip(GameObject box)
        {
            if (!On || box == null) return;
            Turn(box);
            Fresh.Add(new KeyValuePair<GameObject, int>(box, LateFrames));
        }

        // Only what is shown is turned: a pooled part's template lies inactive beside its copies, and a template
        // turned red would hand its red to every copy made of it, also after the fight.
        private static void Turn(GameObject root)
        {
            root.GetComponentsInChildren(false, Scratch);
            foreach (var graphic in Scratch)
                if (graphic != null && graphic.gameObject.activeInHierarchy) Turn(graphic);
            Scratch.Clear();
        }

        private static void Turn(Graphic graphic)
        {
            if (!SheetColours.Red(graphic.color, out var red)) return;
            if (!Was.ContainsKey(graphic)) Was[graphic] = graphic.color;
            graphic.color = red;
        }

        private static void Line()
        {
            if (_line == null)
                foreach (var label in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
                    if (label != null && label.name == CannotUseLine && label.gameObject.scene.IsValid())
                    {
                        _line = label;
                        break;
                    }
            if (_pips == null && _line != null)
            {
                // the pips' holder lies in the same HUD as the line: looked for from the HUD's root down
                var root = _line.transform.root;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == TargetPips)
                    {
                        _pips = t.gameObject;
                        break;
                    }
            }
            // (DD2 may write the line's colour again when it shows it: it is looked at now and then)
            if (_line != null) Turn(_line);
        }

        private static void Begin()
        {
            On = true;
            _line = null;
            _nextLine = 0f;
        }

        private static void End()
        {
            On = false;
            foreach (var entry in Was)
                if (entry.Key != null) entry.Key.color = entry.Value;
            Was.Clear();
            Fresh.Clear();
            _line = null;
            _pips = null;
        }

        private class Watcher : MonoBehaviour
        {
            private void Update()
            {
                try { Tick(); }
                catch (Exception e)
                {
                    Plugin.Log.LogError("The fight's colours failed and are DD2's own for this run: " + e);
                    try { End(); } catch (Exception) { }
                    enabled = false;
                }
            }

            private static void Tick()
            {
                var wanted = Wanted();
                if (wanted != On)
                {
                    if (wanted) Begin();
                    else End();
                }
                if (!On) return;
                for (var i = Fresh.Count - 1; i >= 0; i--)
                {
                    var box = Fresh[i].Key;
                    var left = Fresh[i].Value;
                    if (box == null || left <= 0)
                    {
                        Fresh.RemoveAt(i);
                        continue;
                    }
                    Turn(box);
                    Fresh[i] = new KeyValuePair<GameObject, int>(box, left - 1);
                }
                if (_pips != null) Turn(_pips);
                if (Time.unscaledTime >= _nextLine)
                {
                    _nextLine = Time.unscaledTime + LineEvery;
                    Line();
                }
            }
        }
    }
}
