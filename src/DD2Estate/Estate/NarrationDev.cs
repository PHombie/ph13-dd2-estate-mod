using System;
using System.Collections.Generic;
using System.Linq;
using DD2Estate.Dev;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Test commands of the Ancestor's narration for the dev bridge ({"cmd":"run","name":"narration.trigger","id":"enter_building","tags":"tavern"}):
    ///   narration.state                         what is on screen, what waits, what has been said
    ///   narration.triggers                      DD1's moments, with how many of their lines have words
    ///   narration.lines id=.. [tags=a,b]        the lines of a moment: chance, priority, limits, tags, words, whether they fit the tags
    ///   narration.trigger id=.. [tags=a,b] [scope=hamlet|dungeon] [force=true]
    ///                                           a moment happens (DD1's dice and limits; force skips both)
    ///   narration.say text=.. [scope=..]        the Ancestor says a text of your own
    ///   narration.story id=plot_librarian_1 [part=intro|victory]   a story quest's narration
    ///   narration.hush                          silence: the line on screen and those waiting
    ///   narration.forget [what=raid|visit|campaign]   forgets what was said (all three without "what")
    /// </summary>
    [EstateModule]
    internal static class NarrationDev
    {
        private static void Register()
        {
            AgentBridge.Register("narration.state", o => Narration.Describe());
            AgentBridge.Register("narration.triggers", o =>
            {
                var list = new List<object>();
                foreach (var trigger in NarrationRules.All)
                    list.Add(new { id = trigger.Id, chance = trigger.Chance, lines = trigger.Lines.Count, withWords = trigger.Lines.Count(l => !l.Silence && Dd1Strings.Has(l.TextId + "_0")) });
                return list;
            });
            AgentBridge.Register("narration.lines", o =>
            {
                var trigger = NarrationRules.Find((string)o["id"]);
                if (trigger == null) return "DD1 has no such moment (narration.triggers lists them)";
                var tags = Tags(o);
                var list = new List<object>();
                foreach (var line in trigger.Lines)
                {
                    list.Add(new
                    {
                        path = line.Path,
                        chance = line.Chance,
                        priority = line.Priority,
                        maxRaid = line.MaxRaid,
                        maxTownVisit = line.MaxTownVisit,
                        maxCampaign = line.MaxCampaign,
                        tags = line.Tags,
                        allTags = line.AllTags,
                        onlyWhenSilent = line.OnlyWhenSilent,
                        fits = line.Fits(tags),
                        words = line.Silence ? null : Dd1Strings.Parts(line.TextId)
                    });
                }
                return new { id = trigger.Id, chance = trigger.Chance, tags, lines = list };
            });
            AgentBridge.Register("narration.trigger", o =>
            {
                var id = (string)o["id"];
                if (NarrationRules.Find(id) == null) return "DD1 has no such moment (narration.triggers lists them)";
                var said = Narration.Answer(id, ScopeOf(o), Tags(o), (bool?)o["force"] ?? false);
                return new { said, state = Narration.Describe() };
            });
            AgentBridge.Register("narration.say", o =>
            {
                Narration.Say((string)o["text"], ScopeOf(o), "dev");
                return Narration.Describe();
            });
            AgentBridge.Register("narration.story", o =>
            {
                var quest = Memoirs.PlotQuest((string)o["id"]);
                if (quest == null) return "no such story quest";
                var text = (string)quest[(string)o["part"] == "victory" ? "victory_narration" : "intro_narration"];
                Narration.Say(text, ScopeOf(o), "dev story");
                return new { said = text, state = Narration.Describe() };
            });
            AgentBridge.Register("narration.hush", o =>
            {
                Narration.Hush();
                return "silent";
            });
            AgentBridge.Register("narration.forget", o =>
            {
                Narration.Forget((string)o["what"]);
                return Narration.Describe();
            });
        }

        private static List<string> Tags(Newtonsoft.Json.Linq.JObject o)
        {
            var text = (string)o["tags"] ?? "";
            return text.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private static Narration.Scope ScopeOf(Newtonsoft.Json.Linq.JObject o)
        {
            return Enum.TryParse((string)o["scope"], true, out Narration.Scope scope) ? scope : Narration.Scope.Anywhere;
        }
    }
}
