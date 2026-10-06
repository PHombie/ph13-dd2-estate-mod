using System;
using System.Collections.Generic;
using Assets.Code.UI;
using DD2Estate.Dev;
using DD2Estate.Estate;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Playables;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// DD2's own fight screen as a source of looks for the dungeon's screen (the owner's wish of 2026-10-06:
    /// the torch and the bars under the heroes as DD2's fights draw them). The fight's screen lives in the
    /// `combat` scene, which is not loaded in a corridor; but the scene's HUD is an instance of a prefab the
    /// game keeps an address for (Assets/Prefabs/UI/Combat/CombatUI.prefab), with the scene's own sizes and
    /// sprites in it. That prefab is loaded here with a handle of the mod's own, never shown, and parts of it
    /// are copied by <see cref="Dd2Torch"/> (the torch) and <see cref="Dd2Bars"/> (the health bar, the stress
    /// pips, the row of marks and the selection mark under an actor): a copy is stripped of the game's
    /// behaviours and driven by the mod. The prefabs stay loaded for the session: the copies' sprites and
    /// materials live in their bundles.
    ///
    /// What is here is what the two share: the config entries ([Look]), the loading of prefabs and sprites by
    /// address, and the stripping of a copy.
    /// </summary>
    [EstateModule]
    internal static class Dd2Hud
    {
        /// <summary>The fight's whole HUD, and its parts on their own (asked for when the whole is not to be had).</summary>
        public const string CombatUiKey = "Assets/Prefabs/UI/Combat/CombatUI.prefab";
        public const string BattleInfoKey = "Assets/Prefabs/UI/Combat/BattleInfo/BattleInfo.prefab";
        public const string IndicatorsKey = "Assets/Prefabs/UI/Combat/Actor Indicators.prefab";
        public const string ActorInfoKey = "Assets/Prefabs/UI/actor_info_panel.prefab";

        /// <summary>The first prefab asked for the fight's HUD (the dev bridge can name another, to compare).</summary>
        public static string Source = CombatUiKey;

        /// <summary>[Look] HeroBars: "dd2" the fight's bars under the heroes, "dd1" DD1's.</summary>
        public static bool BarsDd2 = true;
        /// <summary>
        /// [Look] HeroBarsRise: how far above the fight's own line the bars stand in a corridor, in pixels of a
        /// 1080 px high screen. DD2's fight has a black floor under its bars; DD1's panels begin at 720, where
        /// the fight has its stress pips. 0 puts the bars exactly where a fight has them.
        /// </summary>
        public static float BarsRise = 14f;

        private static void Register()
        {
            Dd2Torch.Enabled = !string.Equals(Plugin.Settings.Bind("Look", "Torch", "dd2",
                "The torch of the dungeon's screen: dd2 = the torch of DD2's fights (also outside a fight), dd1 = DD1's own torch gauge outside a fight.").Value.Trim(), "dd1", StringComparison.OrdinalIgnoreCase);
            BarsDd2 = !string.Equals(Plugin.Settings.Bind("Look", "HeroBars", "dd2",
                "The bars under the heroes in a corridor and at a camp: dd2 = as DD2's fights draw them (health bar, stress pips, marks, selection mark), dd1 = DD1's.").Value.Trim(), "dd1", StringComparison.OrdinalIgnoreCase);
            BarsRise = Plugin.Settings.Bind("Look", "HeroBarsRise", 14f,
                "How far above the line of DD2's fights the heroes' bars stand outside a fight, in pixels of a 1080 px high screen (DD1's panels begin where a fight has its stress pips). 0 = exactly where a fight has them.").Value;
        }

        // ---- the game's assets by address ------------------------------------------------------------------

        private class Asked<T> where T : UnityEngine.Object
        {
            public AsyncOperationHandle<T> Handle;
            public T Result;
            public bool Done;
            public string Status = "loading";
        }

        private static readonly Dictionary<string, Asked<GameObject>> Prefabs = new Dictionary<string, Asked<GameObject>>();
        private static readonly Dictionary<string, Asked<Sprite>> Sprites = new Dictionary<string, Asked<Sprite>>();

        private static Asked<T> Ask<T>(Dictionary<string, Asked<T>> table, string key) where T : UnityEngine.Object
        {
            if (table.TryGetValue(key, out var asked)) return asked;
            asked = new Asked<T>();
            table[key] = asked;
            try
            {
                // a handle of the mod's own, kept for the session: what is copied out of the asset lives in its bundles
                asked.Handle = Addressables.LoadAssetAsync<T>(key);
                var mine = asked;
                asked.Handle.Completed += loaded =>
                {
                    mine.Done = true;
                    if (loaded.Status == AsyncOperationStatus.Succeeded && loaded.Result != null)
                    {
                        mine.Result = loaded.Result;
                        mine.Status = "loaded";
                    }
                    else
                    {
                        mine.Status = "not to be had: " + (loaded.OperationException != null ? loaded.OperationException.Message : "nothing came");
                        Plugin.Log.LogWarning("DD2 HUD: " + key + " is " + mine.Status);
                    }
                };
            }
            catch (Exception e)
            {
                asked.Done = true;
                asked.Status = "not to be had: " + e.Message;
                Plugin.Log.LogWarning("DD2 HUD: " + key + " is " + asked.Status);
            }
            return asked;
        }

        /// <summary>A prefab of the game's by its address: asked for at the first call, null until it is there (or for good, when it is not to be had).</summary>
        public static GameObject Prefab(string key) => Ask(Prefabs, key).Result;

        /// <summary>True once the game has answered: the prefab is there, or will not come.</summary>
        public static bool Answered(string key) => Ask(Prefabs, key).Done;

        /// <summary>A sprite of the game's by its address, null until it is there.</summary>
        public static Sprite Sprite(string key) => string.IsNullOrEmpty(key) ? null : Ask(Sprites, key).Result;

        /// <summary>True once the game has answered for the sprite.</summary>
        public static bool SpriteAnswered(string key) => string.IsNullOrEmpty(key) || Ask(Sprites, key).Done;

        private static readonly Dictionary<string, Sprite> InMemory = new Dictionary<string, Sprite>();
        private static float _nextLook;

        /// <summary>
        /// A sprite the game has in memory, by its name: the pictures of DD2's icon atlas have no address of
        /// their own, and are there while the atlas is (the fight's HUD keeps it). Null when it is not there
        /// (looked for again every few seconds).
        /// </summary>
        public static Sprite InMemorySprite(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (InMemory.TryGetValue(name, out var known) && known != null) return known;
            if (Time.unscaledTime < _nextLook) return null;
            _nextLook = Time.unscaledTime + 3f;
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sprite == null || sprite.name != name) continue;
                InMemory[name] = sprite;
                return sprite;
            }
            return null;
        }

        /// <summary>
        /// A picture of DD2's by what names it: "sprite:NAME" one the game has in memory, else an address.
        /// <paramref name="pending"/>: it is not there yet and may still come.
        /// </summary>
        public static Sprite Picture(string art, out bool pending)
        {
            pending = false;
            if (string.IsNullOrEmpty(art)) return null;
            if (art.StartsWith("sprite:", StringComparison.Ordinal)) return InMemorySprite(art.Substring(7));
            var sprite = Sprite(art);
            pending = sprite == null && !SpriteAnswered(art);
            return sprite;
        }

        /// <summary>
        /// The prefab of the fight's HUD: the first of <see cref="Source"/>, the whole HUD and the battle's
        /// part of it that the game hands over. Null while it is on its way (<paramref name="waiting"/>) and
        /// when none of them is to be had.
        /// </summary>
        public static GameObject FightHud(out bool waiting)
        {
            waiting = false;
            foreach (var key in new[] { Source, CombatUiKey, BattleInfoKey })
            {
                if (string.IsNullOrEmpty(key)) continue;
                var prefab = Prefab(key);
                if (prefab != null) return prefab;
                if (Answered(key)) continue;
                waiting = true;
                return null;
            }
            return null;
        }

        /// <summary>Dev bridge: what was asked of the game and what came.</summary>
        public static object Describe()
        {
            var asked = new Dictionary<string, string>();
            foreach (var pair in Prefabs) asked[pair.Key] = pair.Value.Status;
            foreach (var pair in Sprites) asked[pair.Key] = pair.Value.Status;
            return new { source = Source, torch = Dd2Torch.Enabled ? "dd2" : "dd1", bars = BarsDd2 ? "dd2" : "dd1", rise = BarsRise, scale = Scale, asked };
        }

        // ---- copies ------------------------------------------------------------------------------------

        private static GameObject _holder;

        /// <summary>Where the copies wait: under a switched-off parent nothing of a copy wakes up before the game's behaviours are out of it.</summary>
        public static Transform Holder
        {
            get
            {
                if (_holder != null) return _holder.transform;
                _holder = new GameObject("DD2Estate.Dd2HudTemplates");
                _holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(_holder);
                return _holder.transform;
            }
        }

        /// <summary>
        /// DD2's fights scale their HUD to fit a 1920x1080 screen whole (CanvasScaler: Expand); the dungeon's
        /// screen is always 1080 high. On a display of 16:9 or wider the two are the same size; on a narrower
        /// one the fight's HUD is smaller by this factor, and so are the copies.
        /// </summary>
        public static float Scale
        {
            get
            {
                if (Screen.height <= 0) return 1f;
                var byWidth = Screen.width / 1920f;
                var byHeight = Screen.height / 1080f;
                return byWidth < byHeight ? byWidth / byHeight : 1f;
            }
        }

        // Unity's own UI parts, text, layouts and the particle renderer stay; the game's code and anything that acts on a click go.
        private static bool Keeps(MonoBehaviour behaviour)
        {
            var type = behaviour.GetType();
            if (behaviour is Selectable && !(behaviour is Slider)) return false;
            var space = type.Namespace ?? "";
            if (space.StartsWith("UnityEngine.Timeline", StringComparison.Ordinal)) return false;       // SignalReceiver: calls into the widget
            if (space.StartsWith("FMOD", StringComparison.Ordinal)) return false;
            // a layout of the game's own is only a layout (FlexibleGridLayout: the row of marks)
            if (behaviour is LayoutGroup || behaviour is ILayoutIgnorer) return true;
            if (space.StartsWith("Assets.Code", StringComparison.Ordinal)) return false;
            return type.Assembly != typeof(CombatTorchUiBhv).Assembly || space.StartsWith("Coffee", StringComparison.Ordinal);
        }

        /// <summary>
        /// Takes the game's behaviours out of a copy that has not woken up yet (it hangs under <see cref="Holder"/>):
        /// they read the fight, the run, the coach. What the game's debug switch would hide is hidden first.
        /// Nothing of the copy takes the pointer afterwards. Returns the names of what was taken out.
        /// </summary>
        public static List<string> Strip(GameObject clone, bool animators)
        {
            foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null && behaviour.GetType().Name == "DeactivateEditorPrefsBhv") behaviour.gameObject.SetActive(false);
            var removed = new List<string>();
            // More than once: one behaviour may be required by another. Unity's own buttons go last: the game's
            // behaviours on them require them (and Unity writes an error for each that is asked to go too early).
            for (var pass = 0; pass < 4; pass++)
            {
                foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null || Keeps(behaviour)) continue;
                    if (pass == 0 && behaviour is Selectable) continue;
                    var name = behaviour.GetType().Name;
                    UnityEngine.Object.DestroyImmediate(behaviour);
                    if (behaviour == null && !removed.Contains(name)) removed.Add(name);
                }
            }
            foreach (var director in clone.GetComponentsInChildren<PlayableDirector>(true))
            {
                if (animators) director.playOnAwake = false;
                else UnityEngine.Object.DestroyImmediate(director);
            }
            if (!animators)
                foreach (var animator in clone.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(animator);
            foreach (var graphic in clone.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            foreach (var group in clone.GetComponentsInChildren<CanvasGroup>(true))
            {
                group.blocksRaycasts = true;        // a part the mod gives the pointer to must not be shut off by its group
                group.interactable = false;
            }
            return removed;
        }

        /// <summary>Where a part is in a copy, as the numbers of the children on the way down: a copy of the copy has it in the same place.</summary>
        public static int[] PathTo(Transform root, Transform part)
        {
            if (root == null || part == null) return null;
            var path = new List<int>();
            for (var t = part; t != root; t = t.parent)
            {
                if (t == null) return null;     // not under the root
                path.Insert(0, t.GetSiblingIndex());
            }
            return path.ToArray();
        }

        public static Transform At(Transform root, int[] path)
        {
            if (root == null || path == null) return null;
            var t = root;
            foreach (var index in path)
            {
                if (index < 0 || index >= t.childCount) return null;
                t = t.GetChild(index);
            }
            return t;
        }

        public static T At<T>(Transform root, int[] path) where T : Component
        {
            var t = At(root, path);
            return t != null ? t.GetComponent<T>() : null;
        }
    }
}
