using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DD2Estate.Dd1;
using DD2Estate.Dungeon;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The Ancestor's Memoirs, kept at his statue as in DD1: what he has told so far, to be read again. DD1's
    /// statue lists the prologue films, a spoken memoir for every boss quest, the Darkest Dungeon's, and the
    /// epilogue (campaign/town/buildings/statue/statue_media_info.json: the categories with their title bars,
    /// backdrops, order and the quest ids each takes). The estate keeps that shape and fills it with its own
    /// story: a plot quest of Data/plot_quests.json becomes a memoir once it is finished (its confession and
    /// what he says after the victory), filed under the category DD1 gives the quest it stands for
    /// (<c>dd1_quest_id</c>). As in DD1 the memoirs of one boss share a slab, a line for each part, and a part
    /// not yet earned is listed with a lock. The prologue and the epilogue are DD1's own words, the subtitles
    /// of its films, read from the player's install: the mod's story keeps that frame (docs/STORY.md, premise
    /// and section 3). Text only: DD1 plays these, and its voice banks cannot be played by DD2.
    /// </summary>
    [EstateModule]
    internal static class Memoirs
    {
        public const string BuildingId = "statue";
        public const string ArtDir = "campaign/town/buildings/statue/";
        private const string MediaFile = ArtDir + "statue_media_info.json";

        public class Category
        {
            public string Id;
            /// <summary>DD1's name of the category ("The Ancestor's Path").</summary>
            public string Label;
            /// <summary>DD1 art: the bar over the category's entries, and an entry's slab.</summary>
            public string TitleBar, Slab;
            public int Order;
            internal Regex Takes;
        }

        public class Entry
        {
            public string Id;
            public Category Category;
            public string Title;
            /// <summary>"The Librarian (1 / 3), Ruins"; null for DD1's own entries.</summary>
            public string Subtitle;
            /// <summary>DD1 art for the entry's picture (85 x 85).</summary>
            public string Icon;
            /// <summary>DD2 actor whose own portrait is preferred over <see cref="Icon"/>: the boss the memoir is about.</summary>
            public string Actor;
            /// <summary>Which part of its slab's line of memoirs it is, from 1 (DD1: "Part 1"); 0 for one that stands alone.</summary>
            public int Part;
            /// <summary>False for an entry that is listed but cannot be read yet: DD1 shows it with a lock.</summary>
            public bool Unlocked = true;
            /// <summary>What a locked entry says instead of its text; null when DD1 has no words for it.</summary>
            public string LockHint;
            internal Func<string> Read;

            /// <summary>The memoir in full.</summary>
            public string Text => Unlocked ? Read?.Invoke() ?? "" : LockHint ?? "";
        }

        /// <summary>
        /// One slab of the shelves, as DD1 builds them: a picture and a line per part, each with its own
        /// button. A film has one line; the memoirs of a boss have one for every quest against it.
        /// </summary>
        public class Slab
        {
            public Category Category;
            public readonly List<Entry> Parts = new List<Entry>();
            /// <summary>A film of DD1's (video_image_margins) rather than the memoirs of a quest (plot_quest_image_margins).</summary>
            public bool Film;
            /// <summary>Whom the memoirs are about ("The Librarian, Ruins"); null for DD1's own entries.</summary>
            public string About;

            public string Icon => Parts[0].Icon;
            public string Actor => Parts[0].Actor;
            /// <summary>False while every part is still shut.</summary>
            public bool Unlocked => Parts.Any(part => part.Unlocked);
        }

        /// <summary>A slab is 118 px high and a line with its button 32: three parts to a slab, as DD1 has for every boss.</summary>
        public const int PartsPerSlab = 3;

        // FALLBACK: DD1's five categories as its file has them, used when the file cannot be read.
        private static readonly string[][] StockCategories =
        {
            new[] { "prologue", "str_media_video", "video_title_bar.png", "media_entry_video_backdrop.png", "0", "house_of_ruin|old_road" },
            new[] { "boss_entries", "str_media_boss_quests", "boss_quest_title_bar.png", "media_entry_plot_quest_backdrop.png", "5000", "plot_kill_.*" },
            new[] { "dd_entries", "str_darkest_dungeon_audio", "darkest_dungeon_title_bar.png", "media_entry_darkest_dungeon_backdrop.png", "7500", "plot_darkest_dungeon_.*" },
            new[] { "epilog", "str_media_epilog_title", "epilog_title_bar.png", "media_entry_epilog_backdrop.png", "10000", "epilog" },
            new[] { "backerjournal", "str_media_backer_journals", "backer_journal_title_bar.png", "media_entry_backer_journal_backdrop.png", "12000", "NONE" }
        };

        /// <summary>The category that takes the plot quests DD1's file has no place for (the opening expedition): the Ancestor's Path.</summary>
        private const string StoryCategory = "boss_entries";
        private const string DarkestCategory = "dd_entries";

        private static List<Category> _categories;
        private static JArray _videos;
        private static JArray _plot;

        private static void Register()
        {
            Buildings.Register(BuildingId, MemoirsPanel.Open);
        }

        // ---- DD1's shelves -------------------------------------------------------------------------------

        public static IReadOnlyList<Category> Categories
        {
            get
            {
                if (_categories != null) return _categories;
                var categories = new List<Category>();
                try
                {
                    var json = DD2Estate.Core.Json.ParseFile(Dd1Install.ReadText(MediaFile));
                    foreach (var entry in json?["categories"] as JArray ?? new JArray())
                    {
                        var id = (string)entry["name"];
                        if (id == null) continue;
                        categories.Add(New(id, (string)entry["label"], (string)entry["label_backdrop"], (string)entry["entry_backdrop"],
                            (int?)entry["sort_priority"] ?? 0, (string)entry["regex_filter"]));
                    }
                    _videos = json?["videos"] as JArray;
                }
                catch (Exception e) { Plugin.Log.LogWarning("DD1 statue_media_info.json could not be read: " + e.Message); }
                if (categories.Count == 0)
                    foreach (var stock in StockCategories)
                        categories.Add(New(stock[0], stock[1], ArtDir + stock[2], ArtDir + stock[3], int.Parse(stock[4]), stock[5]));
                categories.Sort((a, b) => a.Order.CompareTo(b.Order));
                _categories = categories;
                return _categories;
            }
        }

        private static Category New(string id, string label, string bar, string slab, int order, string takes)
        {
            Regex regex = null;
            try { regex = new Regex("^(?:" + (takes ?? "") + ")$"); }
            catch (ArgumentException) { }
            return new Category
            {
                Id = id,
                Label = Dd1Strings.Plain(Dd1Strings.Get(label ?? "")) is string text && text.Length > 0 ? text : TownEventText.Words(id),
                TitleBar = bar,
                Slab = slab,
                Order = order,
                Takes = regex
            };
        }

        private static Category CategoryOf(string dd1Id, string fallback)
        {
            Category named = null;
            foreach (var category in Categories)
            {
                if (dd1Id != null && category.Takes != null && category.Takes.IsMatch(dd1Id)) return category;
                if (category.Id == fallback) named = category;
            }
            return named ?? Categories[0];
        }

        // ---- the estate's story --------------------------------------------------------------------------

        private static JArray Plot
        {
            get
            {
                if (_plot != null) return _plot;
                using (var stream = typeof(Memoirs).Assembly.GetManifestResourceStream("DD2Estate.Data.plot_quests.json"))
                using (var reader = new StreamReader(stream))
                    _plot = JObject.Parse(reader.ReadToEnd())["quests"] as JArray ?? new JArray();
                return _plot;
            }
        }

        /// <summary>The story quest with this id or this name, as plot_quests.json has it; null for none.</summary>
        public static JToken PlotQuest(string idOrName)
        {
            if (string.IsNullOrEmpty(idOrName)) return null;
            foreach (var quest in Plot)
                if ((string)quest["id"] == idOrName || (string)quest["name"] == idOrName) return quest;
            return null;
        }

        public static int PlotCount => Plot.Count;

        public static int PlotDone
        {
            get
            {
                var done = EstateState.Current.CompletedQuests;
                return Plot.Count(quest => done.Contains((string)quest["id"]));
            }
        }

        /// <summary>Every entry of the shelves, in their order: what can be read, and what is still shut.</summary>
        public static List<Entry> All()
        {
            var entries = new List<Entry>();
            foreach (var slab in Shelves()) entries.AddRange(slab.Parts);
            return entries;
        }

        /// <summary>
        /// The shelves as they stand, in DD1's order of categories and the story's order inside them: DD1's
        /// films, and a slab for every boss of the story with a part for each quest against it (the Darkest
        /// Dungeon's quests share theirs, three to a slab). A part is shut until its quest is finished.
        /// </summary>
        public static List<Slab> Shelves()
        {
            var slabs = new List<Slab>();
            var done = EstateState.Current.CompletedQuests;
            var videos = Categories.Count > 0 ? _videos : null;

            // DD1's films, as text. Without the file: the three DD1 ships.
            var films = new List<JToken>();
            if (videos != null) films.AddRange(videos);
            if (films.Count == 0)
            {
                films.Add(new JObject { ["name"] = "house_of_ruin", ["category"] = "prologue" });
                films.Add(new JObject { ["name"] = "old_road", ["category"] = "prologue" });
                films.Add(new JObject { ["name"] = "epilog", ["category"] = "epilog", ["access_if_plot_finished"] = "plot_darkest_dungeon_4" });
            }
            foreach (var film in films)
            {
                var name = (string)film["name"];
                if (name == null || !Dd1Strings.Has("str_vo_" + name + "_0")) continue;
                var needs = (string)film["access_if_plot_finished"];
                var entry = new Entry
                {
                    Id = name,
                    Category = CategoryOf(name, (string)film["category"]),
                    Title = Dd1Strings.Plain(Dd1Strings.Get("str_media_" + name)) is string title && title.Length > 0 ? title : TownEventText.Words(name),
                    Icon = ArtDir + name + ".png",
                    // DD1 names one of its own quests; the estate's quest that stands for it has to be finished.
                    Unlocked = needs == null || Plot.Any(quest => (string)quest["dd1_quest_id"] == needs && done.Contains((string)quest["id"])),
                    LockHint = Hint("str_media_" + name + "_locked"),
                    Read = () => Verse("str_vo_" + name)
                };
                var slab = new Slab { Category = entry.Category, Film = true };
                slab.Parts.Add(entry);
                slabs.Add(slab);
            }

            // The story's quests: one line of memoirs per boss, the Darkest Dungeon's quests in one line.
            var lines = new Dictionary<string, List<Entry>>();
            var open = new Dictionary<string, Slab>();
            var hinted = new HashSet<string>();
            foreach (var quest in Plot)
            {
                var id = (string)quest["id"];
                if (id == null) continue;
                var dd1 = (string)quest["dd1_quest_id"];
                var darkest = (string)quest["dungeon"] == QuestBoard.DarkestDungeon;
                var intro = (string)quest["intro_narration"];
                var victory = (string)quest["victory_narration"];
                var entry = new Entry
                {
                    Id = id,
                    Category = CategoryOf(dd1, darkest ? DarkestCategory : StoryCategory),
                    Title = (string)quest["name"] ?? id,
                    Subtitle = Subtitle(quest),
                    Icon = ArtDir + (darkest ? "portrait_darkest_dungeon.png" : Dd1Portrait(dd1)),
                    Actor = darkest ? null : BossActor(quest),
                    Unlocked = done.Contains(id),
                    // DD1 says what opens a memoir of the Darkest Dungeon (str_media_plot_darkest_dungeon_<n>); two of the story's quests can stand for one of DD1's.
                    LockHint = dd1 != null && hinted.Add(dd1) ? Hint("str_media_" + dd1) : null,
                    Read = () => string.Join("\n\n", new[] { intro, victory }.Where(part => !string.IsNullOrEmpty(part)))
                };
                var key = darkest ? QuestBoard.DarkestDungeon : (string)quest["boss"] ?? id;
                if (!lines.TryGetValue(key, out var line)) lines[key] = line = new List<Entry>();
                line.Add(entry);
                // a slab holds three parts: a longer line goes on in another one
                if (!open.TryGetValue(key, out var shelf) || shelf.Parts.Count >= PartsPerSlab)
                {
                    open[key] = shelf = new Slab { Category = entry.Category, About = darkest ? DungeonContent.DisplayName((string)quest["dungeon"]) : About(quest) };
                    slabs.Add(shelf);
                }
                shelf.Parts.Add(entry);
            }
            // "Part 1", "Part 2" ... where a line has more than one.
            foreach (var line in lines.Values)
                for (var i = 0; line.Count > 1 && i < line.Count; i++) line[i].Part = i + 1;

            // Stable: the story's order inside a category.
            var order = new Dictionary<Slab, int>();
            for (var i = 0; i < slabs.Count; i++) order[slabs[i]] = i;
            slabs.Sort((a, b) => a.Category.Order != b.Category.Order ? a.Category.Order.CompareTo(b.Category.Order) : order[a].CompareTo(order[b]));
            return slabs;
        }

        private static string Hint(string id)
        {
            var text = Dd1Strings.Plain(Dd1Strings.Get(id));
            return string.IsNullOrEmpty(text) ? null : text;
        }

        // "The Librarian, Ruins": whom a slab's memoirs are about.
        private static string About(JToken quest)
        {
            var dungeon = DungeonContent.DisplayName((string)quest["dungeon"]);
            var bossId = (string)quest["boss"];
            if (bossId == null) return dungeon;
            return ((string)Boss(quest)?["name"] ?? TownEventText.Words(bossId)) + ", " + dungeon;
        }

        public static Entry Find(string id)
        {
            foreach (var entry in All())
                if (entry.Id == id) return entry;
            return null;
        }

        // "The Librarian (2 / 3), Ruins": whom the memoir is about and where it stands in his line.
        private static string Subtitle(JToken quest)
        {
            var dungeon = DungeonContent.DisplayName((string)quest["dungeon"]);
            var bossId = (string)quest["boss"];
            if ((string)quest["dungeon"] == QuestBoard.DarkestDungeon)
            {
                var chain = Plot.Where(q => (string)q["dungeon"] == QuestBoard.DarkestDungeon).ToList();
                return dungeon + " (" + (chain.IndexOf(quest) + 1) + " / " + chain.Count + ")";
            }
            if (bossId == null) return dungeon;
            var line = Plot.Where(q => (string)q["boss"] == bossId).ToList();
            var name = (string)Boss(quest)?["name"] ?? TownEventText.Words(bossId);
            return name + " (" + (line.IndexOf(quest) + 1) + " / " + line.Count + "), " + dungeon;
        }

        // The quest's boss as Data/dungeons.json has it.
        private static JToken Boss(JToken quest)
        {
            var bossId = (string)quest["boss"];
            if (bossId == null) return null;
            foreach (var dungeon in DungeonContent.Dungeons())
            {
                if ((string)dungeon["id"] != (string)quest["dungeon"]) continue;
                foreach (var boss in dungeon["bosses"] as JArray ?? new JArray())
                    if ((string)boss["id"] == bossId) return boss;
            }
            return null;
        }

        private static string BossActor(JToken quest)
        {
            return (string)(Boss(quest)?["actor_ids"] as JArray)?.FirstOrDefault();
        }

        // "plot_kill_necromancer_2" to DD1's "portrait_necromancer.png"; a quest DD1 has no portrait for (the
        // opening expedition) gets the journal page.
        private static string Dd1Portrait(string dd1QuestId)
        {
            var match = Regex.Match(dd1QuestId ?? "", "^plot_kill_(.+?)(?:_\\d+)?$");
            var file = match.Success ? "portrait_" + match.Groups[1].Value + ".png" : null;
            return file != null && Dd1Install.Exists(ArtDir + file) ? file : "backer_journal_icon.png";
        }

        // DD1's film subtitles are a sentence in pieces; a line of the memoir ends where a sentence does.
        private static string Verse(string idWithoutPart)
        {
            var text = new StringBuilder();
            for (var part = 0; part < 200; part++)
            {
                var piece = Dd1Strings.Get(idWithoutPart + "_" + part);
                if (piece == null) break;
                piece = Dd1Strings.Plain(piece);
                if (piece.Length == 0) continue;
                text.Append(piece);
                var last = piece[piece.Length - 1];
                text.Append(last == '.' || last == '!' || last == '?' ? '\n' : ' ');
            }
            return text.ToString().TrimEnd();
        }
    }
}
