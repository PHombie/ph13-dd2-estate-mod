using System;
using System.Collections.Generic;

namespace DD2Estate.Dd1
{
    /// <summary>
    /// What stands between "Darkest Dungeon was not found" and "it is now, without a restart".
    ///
    /// The mod reads Darkest Dungeon in well over a hundred places, and most keep what they read for the run of
    /// the game: a table of words, a set of rules, a screen built from a layout. Read while no install was
    /// found, such a place keeps "nothing" just as faithfully (an empty table, stock numbers, a screen with a
    /// message in it), and a folder named afterwards would be read by some of the mod and not by the rest.
    ///
    /// Looked at place by place (2026-10-06), the mod asks for the install NOWHERE before an Estate session
    /// begins: not while it loads, not at the main menu, not in what runs every frame, not in a patch of the
    /// game's that is live outside a session. The first to ask is the main menu's Estate entry itself. So a
    /// folder named at the main menu, in a run in which none was found, can be taken into use on the spot:
    /// nothing has been read, nothing is kept.
    ///
    /// That is a fact about the code as it stood, and it is checked each time, not trusted: Dd1Install writes
    /// down every class of the mod that asked while there was no install (<see cref="Dd1Install.AskerClasses"/>),
    /// and the folder is taken at once only if every one of them is known here, either as keeping nothing
    /// (<see cref="KeepNothing"/>) or as one that can be told to forget (<see cref="Forgetters"/>). A class
    /// that is not known, say a new feature that reads a rule of Darkest Dungeon's at start-up, makes the
    /// answer "saved, restart the game" and is named in the log. The same answer is given after an Estate
    /// session that ran without the install (the dev bridge can start one): its screens were built without it.
    /// </summary>
    internal static class Dd1Forget
    {
        /// <summary>
        /// Classes that may ask for the install outside a session and keep nothing of a "not there": they ask
        /// again each time. The main menu's entry and its question, the options' folder row (its state is made
        /// anew at every ask), the sound's driver (it has no answer of its own: it asks Dd1Audio), and what
        /// carries a dev command to them.
        /// </summary>
        private static readonly HashSet<string> KeepNothing = new HashSet<string>
        {
            "EstateMenu", "Dd1Prompt", "Dd1PromptDev", "EstateOptions", "EstateOptionsDev", "EstateOptionsTab",
            "EstateAudio", "AudioDev", "AgentBridge", "EstateHost", "Plugin", "Dd1Forget"
        };

        /// <summary>
        /// Classes that keep a "not there" and forget it when told: the services the rest reads through. Asked
        /// outside a session only through the dev bridge today (audio.state, a command that looks up a word).
        /// </summary>
        private static readonly Dictionary<string, Action> Forgetters = new Dictionary<string, Action>
        {
            // an empty table of words, kept as if it were Darkest Dungeon's
            { "Dd1Strings", DD2Estate.Estate.Dd1Strings.Forget },
            // "no DD1 install": the sound never started and would never be tried again
            { "Dd1Audio", Dd1Audio.AskAgain }
        };

        /// <summary>The classes among those that asked which are not known to be harmless: with one of them, a folder waits for a restart.</summary>
        public static List<string> Unknown(IEnumerable<string> askers)
        {
            var unknown = new List<string>();
            foreach (var asker in askers)
                if (!KeepNothing.Contains(asker) && !Forgetters.ContainsKey(asker)) unknown.Add(asker);
            unknown.Sort(StringComparer.Ordinal);
            return unknown;
        }

        /// <summary>Every "not there" that is kept and can be forgotten is forgotten (Dd1Install forgets its own pictures itself).</summary>
        public static void All()
        {
            foreach (var forgetter in Forgetters)
            {
                try { forgetter.Value(); }
                catch (Exception e) { Plugin.Log.LogWarning("Darkest Dungeon 1: " + forgetter.Key + " could not be told to ask again: " + e.Message); }
            }
        }
    }
}
