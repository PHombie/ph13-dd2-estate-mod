using System;
using System.Collections.Generic;
using DD2Estate.Dd1;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's own table of when the Ancestor speaks: audio/narration.json in the player's install. An entry is a
    /// moment of the game ("town_visit_start", "enter_building", "quest_start" ...) with the voice clips that
    /// may answer it; a clip has a chance, a priority, tags that say when it fits (a dungeon, a quest type, a
    /// building, a town event) and limits on how often it is heard in one expedition, one town visit and the
    /// whole campaign. The clip's path names its subtitles: "/vo/town/backstory_01" is "str_vo_town_backstory_01_0"
    /// (and "_1", "_2" ... for a clip with several) in DD1's string table. The mod uses the table and the
    /// subtitles; the clips themselves are FMOD banks DD2 cannot play.
    /// </summary>
    internal static class NarrationRules
    {
        private const string File = "audio/narration.json";

        public class Line
        {
            /// <summary>DD1's audio event; empty for an entry DD1 put in to say nothing with a certain chance.</summary>
            public string Path = "";
            /// <summary>What the line is counted under: its audio event (two entries of one clip share their count), or the entry's place for a silent one.</summary>
            public string Key;
            /// <summary>The string id of its subtitles, without the part number.</summary>
            public string TextId;
            public float Chance = 1f;
            public int Priority;
            /// <summary>Most times the line is spoken in one expedition, one town visit, the campaign; 0 for no limit.</summary>
            public int MaxRaid, MaxTownVisit, MaxCampaign;
            public string[] Tags = new string[0];
            /// <summary>True: every tag has to hold; false: one is enough. A line without tags always fits.</summary>
            public bool AllTags = true;
            /// <summary>DD1's queue_only_on_empty: the line is dropped when the Ancestor is already speaking.</summary>
            public bool OnlyWhenSilent;

            public bool Silence => Path.Length == 0;

            public bool Fits(ICollection<string> tags)
            {
                if (Tags.Length == 0) return true;
                foreach (var tag in Tags)
                {
                    var holds = tags != null && tags.Contains(tag);
                    if (holds && !AllTags) return true;
                    if (!holds && AllTags) return false;
                }
                return AllTags;
            }
        }

        public class Trigger
        {
            public string Id;
            /// <summary>The chance that the moment is answered at all.</summary>
            public float Chance = 1f;
            public readonly List<Line> Lines = new List<Line>();
        }

        private static Dictionary<string, Trigger> _triggers;

        public static IEnumerable<Trigger> All
        {
            get
            {
                Load();
                return _triggers.Values;
            }
        }

        public static Trigger Find(string id)
        {
            Load();
            return id != null && _triggers.TryGetValue(id, out var trigger) ? trigger : null;
        }

        /// <summary>"/vo/load/crypts_01" to "str_vo_load_crypts_01".</summary>
        public static string TextIdOf(string path)
        {
            return string.IsNullOrEmpty(path) ? null : "str_" + path.Trim('/').Replace('/', '_');
        }

        private static void Load()
        {
            if (_triggers != null) return;
            _triggers = new Dictionary<string, Trigger>();
            try
            {
                var json = DD2Estate.Core.Json.ParseFile(Dd1Install.ReadText(File));
                foreach (var entry in json?["entries"] as JArray ?? new JArray())
                {
                    var id = (string)entry["id"];
                    if (id == null) continue;
                    var trigger = new Trigger { Id = id, Chance = (float?)entry["chance"] ?? 1f };
                    foreach (var clip in entry["audio_events"] as JArray ?? new JArray())
                    {
                        var line = new Line
                        {
                            Path = (string)clip["audio_event"] ?? "",
                            Chance = (float?)clip["chance"] ?? 1f,
                            Priority = (int?)clip["priority"] ?? 0,
                            MaxRaid = (int?)clip["max_raid_occurrences"] ?? 0,
                            MaxTownVisit = (int?)clip["max_town_visit_occurrences"] ?? 0,
                            MaxCampaign = (int?)clip["max_campaign_occurrences"] ?? 0,
                            AllTags = (bool?)clip["check_all_tags"] ?? true,
                            OnlyWhenSilent = (bool?)clip["queue_only_on_empty"] ?? false
                        };
                        line.TextId = TextIdOf(line.Path);
                        line.Key = line.Silence ? id + "#" + trigger.Lines.Count : line.Path;
                        var tags = new List<string>();
                        foreach (var tag in clip["tags"] as JArray ?? new JArray())
                            if (tag.Type == JTokenType.String) tags.Add((string)tag);
                        line.Tags = tags.ToArray();
                        trigger.Lines.Add(line);
                    }
                    _triggers[id] = trigger;
                }
                Plugin.Log.LogInfo("Narration (DD1): " + _triggers.Count + " moments read from " + File);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("DD1 narration.json could not be read: " + e.Message);
            }
        }
    }
}
