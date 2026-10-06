using System;
using Assets.Code.Combat;
using Assets.Code.Game;
using Assets.Code.Game.Events;
using Assets.Code.Presentation;
using Assets.Code.Utils;
using DD2Estate.Dd2;
using DD2Estate.Estate;
using DD2Estate.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DD2Estate.Dev
{
    /// <summary>
    /// Keys for testing ([Dev] Hotkeys in the config; off unless asked for), inside an Estate session only:
    /// <list type="bullet">
    /// <item>Numpad 5: the fight on screen is won (the game's own cheat, CombatBhv.ForceEndCombat, which its
    /// debug pause menu calls "Win Combat");</item>
    /// <item>Numpad + and Numpad -: the game's speed a step up or down. The steps and the stepping are the game's
    /// own (GameSpeedMgr: x0.5, x1, x2, x3, x4, with its combat speed option on top); the keys only raise its
    /// events and keep it from stepping down to its pause (x0).</item>
    /// </list>
    /// A word in the screen's corner says what a key did. The speed is the game's again (its default step) when the
    /// session ends: nothing of it is left on the main menu or in another game.
    /// </summary>
    [EstateModule]
    internal static class DevHotkeys
    {
        private const float NoteSeconds = 1.6f;
        private const int NoteOrder = 32600;

        private static void Register()
        {
            if (Plugin.DevHotkeys == null || !Plugin.DevHotkeys.Value) return;
            var go = new GameObject("DD2Estate.DevHotkeys") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Watcher>();
            Plugin.Log.LogInfo("Dev hotkeys are on: Numpad 5 wins a fight, Numpad + / - step the game's speed (inside the Estate)");

            AgentBridge.Register("dev.speed", o =>
            {
                if (o["step"] != null) Step((int)o["step"]);
                return new { timeScale = Time.timeScale, index = Index(), scales = Scales(), standard = Standard() };
            });
            AgentBridge.Register("dev.win", o => Win());
        }

        // ---- the game's speed -------------------------------------------------------------------------------

        private static GameSpeedMgr Speed => SingletonMonoBehaviour<GameSpeedMgr>.HasInstance() ? SingletonMonoBehaviour<GameSpeedMgr>.Instance : null;
        private static float[] Scales() => Speed != null ? Traverse.Create(Speed).Field("m_timeScales").GetValue<float[]>() : null;
        private static int Index() => Speed != null ? Traverse.Create(Speed).Field("m_timeScaleIndex").GetValue<int>() : -1;
        private static int Standard() => Speed != null ? Traverse.Create(Speed).Field("m_defaultTimeScaleIndex").GetValue<int>() : -1;

        /// <summary>One step up (+1) or down (-1); a step down that would stop the game (the pause's x0) is not taken.</summary>
        private static string Step(int direction)
        {
            var scales = Scales();
            var index = Index();
            if (scales == null || index < 0) return "the game's speed cannot be reached";
            var next = index + (direction > 0 ? 1 : -1);
            if (next < 0 || next >= scales.Length || scales[next] <= 0.01f) return Words(scales[index]) + (direction > 0 ? " (fastest)" : " (slowest)");
            if (direction > 0) EventIncreaseGameSpeed.Trigger();
            else EventDecreaseGameSpeed.Trigger();
            index = Index();
            return Words(index >= 0 && index < scales.Length ? scales[index] : Time.timeScale);
        }

        private static string Words(float scale) => "Speed x" + scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The game's own speed again: its default step.</summary>
        private static void Normal()
        {
            var standard = Standard();
            for (var guard = 0; guard < 8 && Index() >= 0 && Index() != standard; guard++)
            {
                if (Index() > standard) EventDecreaseGameSpeed.Trigger();
                else EventIncreaseGameSpeed.Trigger();
            }
        }

        // ---- the fight --------------------------------------------------------------------------------------

        private static string Win()
        {
            if (!EstateSession.Active) return "not in the Estate";
            if (GameModeMgr.CurrentMode == null || GameModeMgr.CurrentMode.GetName() != "COMBAT") return "no fight on screen";
            if (!SingletonMonoBehaviour<CombatBhv>.HasInstance()) return "no fight on screen";
            var combat = SingletonMonoBehaviour<CombatBhv>.Instance;
            // a fight still putting its actors up cannot be ended: the key is pressed again a moment later
            if (!combat.IsBattleRunning) return "the fight is still loading";
            combat.ForceEndCombat(isForceComplete: true);
            Plugin.Log.LogInfo("Dev hotkey: the fight was ended as won");
            return "Fight won";
        }

        private class Watcher : MonoBehaviour
        {
            private Canvas _canvas;
            private TextMeshProUGUI _note;
            private float _noteUntil;
            private bool _wasActive;

            private void Update()
            {
                try { Tick(); }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Dev hotkeys failed and are off for this run: " + e);
                    enabled = false;
                }
            }

            private void Tick()
            {
                var active = EstateSession.Active;
                if (_wasActive && !active) Normal();
                _wasActive = active;
                if (_note != null && _note.gameObject.activeSelf && Time.unscaledTime > _noteUntil) _note.gameObject.SetActive(false);
                if (!active) return;
                var keys = Keyboard.current;
                if (keys == null) return;
                if (keys.numpad5Key.wasPressedThisFrame) Say(Win());
                else if (keys.numpadPlusKey.wasPressedThisFrame) Say(Step(1));
                else if (keys.numpadMinusKey.wasPressedThisFrame) Say(Step(-1));
            }

            private void Say(string words)
            {
                if (_canvas == null)
                {
                    _canvas = UiKit.Canvas("DD2Estate.DevNote", NoteOrder);
                    UnityEngine.Object.DontDestroyOnLoad(_canvas.gameObject);
                    _note = UiKit.Text("Note", _canvas.transform, "", 30f, UiKit.Gold, TextAlignmentOptions.TopRight);
                    var rect = (RectTransform)_note.transform;
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
                    rect.anchoredPosition = new Vector2(-28f, -20f);
                    rect.sizeDelta = new Vector2(700f, 44f);
                }
                _note.text = words;
                _note.gameObject.SetActive(true);
                _noteUntil = Time.unscaledTime + NoteSeconds;
            }
        }
    }
}
