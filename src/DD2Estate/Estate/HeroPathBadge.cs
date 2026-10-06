using System;
using System.Collections.Generic;
using System.Text;
using Assets.Code.Actor;
using Assets.Code.Locale;
using Assets.Code.Utils;
using DD2Estate.UI;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD2's own badge of a hero's path: the path's wax seal on its strip of parchment. It is one picture
    /// (ResourceActorPath.m_SealIcon, "hero_seal_&lt;path&gt;", 256x588: the seal fills the picture's width, the
    /// strip hangs from it, short for a class's own path and to the picture's foot for the others), the one
    /// DD2's character sheet hangs beside the hero's name (TopBar/SealLabel/Sea_Animation_Anchor/SeaImage) and
    /// its hero selection at the crossroads shows. It is taken from the running game the way those screens
    /// take it; nothing of it is in the mod. What DD2 says about a path is its own text, too.
    /// </summary>
    internal static class HeroPathBadge
    {
        private static readonly Dictionary<string, Sprite> Arts = new Dictionary<string, Sprite>();
        private static readonly Dictionary<int, Rect> Boxes = new Dictionary<int, Rect>();
        private static readonly HashSet<string> Told = new HashSet<string>();

        /// <summary>DD2's picture of a path's badge; null for no path, a path without one, or while the game cannot give it.</summary>
        public static Sprite Art(string pathId)
        {
            if (string.IsNullOrEmpty(pathId)) return null;
            // the game drops art it thinks nobody uses: a picture that has gone is fetched again
            if (Arts.TryGetValue(pathId, out var known) && known != null) return known;
            Sprite art = null;
            try
            {
                var resource = Singleton<ResourceDatabaseActorPaths>.Instance.GetResource(pathId, isErrorValid: false);
                art = resource != null ? resource.m_SealIcon : null;
            }
            catch (Exception e)
            {
                if (Told.Add(pathId)) Plugin.Log.LogWarning("Roster: DD2's badge of the path " + pathId + " could not be had: " + e.Message);
            }
            if (art != null) Arts[pathId] = art;
            return art;
        }

        /// <summary>
        /// The painted part of a badge as shares of its picture (x and y from the upper left corner, 0 to 1):
        /// where the pointer is on the badge and not on the clear field under a short strip. The whole picture
        /// when it cannot be read.
        /// </summary>
        public static Rect Painted(Sprite art)
        {
            if (art == null) return new Rect(0f, 0f, 1f, 1f);
            var id = art.GetInstanceID();
            if (Boxes.TryGetValue(id, out var known)) return known;
            var size = art.rect.size;
            var share = SpritePaint.Box(art, out var box) && size.x > 0f && size.y > 0f
                ? new Rect(box.x / size.x, box.y / size.y, box.width / size.x, box.height / size.y)
                : new Rect(0f, 0f, 1f, 1f);
            return Boxes[id] = share;
        }

        /// <summary>The path's name as DD2 shows it; "" for no path.</summary>
        public static string Name(ActorInstance actor) => HeroPaths.Name(actor);

        /// <summary>
        /// What DD2 says about the hero's path, as its own tooltip of the seal has it under the name: the
        /// path's line (in the colour DD2 gives it) and what the path changes. TextMeshPro rich text; "" for no path.
        /// </summary>
        public static string Words(ActorInstance actor)
        {
            var path = actor != null ? actor.ActorDataPath : null;
            if (path == null) return "";
            try
            {
                // DD2's own strings begin and end with empty lines, which its tooltip squeezes with a line height of
                // its own; here the lines simply follow one another
                var text = new StringBuilder();
                var line = Singleton<Localization>.Instance.TryGetString("hero_path_flavour_" + path.Id);
                if (!string.IsNullOrWhiteSpace(line)) text.Append("<color=#{path_verbose}><i>").Append(line.Trim()).Append("</i></color>");
                foreach (var effect in (ActorPathDescription.GetEffectStrings(path) ?? "").Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(effect)) continue;
                    if (text.Length > 0) text.Append('\n');
                    text.Append(effect.Trim('\r'));
                }
                return Singleton<Localization>.Instance.GetSubstitutedText(text.ToString());
            }
            catch (Exception e)
            {
                if (Told.Add("words:" + path.Id)) Plugin.Log.LogWarning("Roster: DD2's words about the path " + path.Id + " could not be had: " + e.Message);
                return "";
            }
        }
    }
}
