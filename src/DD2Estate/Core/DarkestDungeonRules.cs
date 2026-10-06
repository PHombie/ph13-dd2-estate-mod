using System;
using System.Collections.Generic;
using System.Linq;

namespace DD2Estate.Core
{
    /// <summary>
    /// One of DD1's two answers to a hero who has finished a Darkest Dungeon quest and is asked to go again
    /// (<c>campaign/roster/roster.settings.json</c> <c>never_again_settings</c>; the option "Never Again").
    /// </summary>
    public sealed class NeverAgainSetting
    {
        public string Id;
        /// <summary><c>darkest_dungeon_can_enter</c>.</summary>
        public bool CanEnter;
        /// <summary><c>darkest_dungeon_buff_ids</c>: buffs of base.buffs.json such a hero carries through the descent.</summary>
        public List<string> BuffIds = new List<string>();
        /// <summary><c>darkest_duungeon_minimum_stress</c> (DD1's own spelling): the stress, of 200, the hero sets out with at least.</summary>
        public double MinimumStress;
    }

    /// <summary>
    /// DD1's rules of the Darkest Dungeon's heroes, read from the install: Never Again
    /// (<c>campaign/roster/roster.settings.json</c>), and the two resolve experience buffs of
    /// <c>shared/buffs/base.buffs.json</c>: <c>completed_darkest_dungeon_quest_party_resolve_xp</c> (a veteran
    /// of the place in the party) and <c>darkest_dungeon_failure_roster_resolve_xp</c> (the estate after a
    /// descent that failed). The initial values of the fields are DD1's stock values, FALLBACKS for a missing
    /// file or key.
    ///
    /// How DD1 uses them (read in its executable): a hero is marked when a quest in the Darkest Dungeon ends
    /// with every goal met, never by a retreat (0x938064). Strict: such a hero is refused for another quest
    /// there (0x92da65). Permissive: they go, their stress is RAISED to the setting's minimum if it is below
    /// it, after the stress of setting out under-levelled (0x97b486), and they carry the setting's buffs
    /// through the descent (0x9ad520).
    /// </summary>
    public sealed class DarkestDungeonRules
    {
        public const string SettingsFile = "campaign/roster/roster.settings.json";
        public const string BuffsFile = "shared/buffs/base.buffs.json";
        public const string VeteranBuff = "completed_darkest_dungeon_quest_party_resolve_xp";
        public const string FailureBuff = "darkest_dungeon_failure_roster_resolve_xp";
        public const string NeverAgainBuff = "never_again_affliction";

        public const string Strict = "strict", Permissive = "permissive";
        // DD1 behaviour, not in the base mode's data: the option "Never Again" stands at Strict unless a mode
        // says otherwise (modes/radiant/shared/options/options.campaign_default_values.json sets "permissive"
        // for Radiant; the base mode, Darkest, has no such file).
        public const string DefaultSetting = Strict;

        public List<NeverAgainSetting> NeverAgain = new List<NeverAgainSetting>
        {
            new NeverAgainSetting { Id = Strict },
            new NeverAgainSetting { Id = Permissive, CanEnter = true, BuffIds = new List<string> { NeverAgainBuff }, MinimumStress = 80 }
        };

        /// <summary><c>resolve_xp_bonus_percent</c> of the veteran's buff: the share more experience (0.5: half as much again).</summary>
        public double VeteranXpBonus = 0.5;
        /// <summary><c>resolve_xp_bonus_percent</c> of the failed descent's buff (1: twice the experience).</summary>
        public double FailureXpBonus = 1;
        /// <summary><c>resolve_check_percent</c> of <c>never_again_affliction</c>: what a returning hero's chance to hold fast under stress changes by.</summary>
        public double NeverAgainResolveCheck = -0.25;

        public static DarkestDungeonRules Load(IDd1Files files)
        {
            var rules = new DarkestDungeonRules();
            var settings = Json.ParseFile(files.ReadText(SettingsFile));
            var read = new List<NeverAgainSetting>();
            foreach (var entry in Json.Array(settings?["never_again_settings"]))
            {
                var id = (string)entry["id"];
                if (id == null) continue;
                var setting = new NeverAgainSetting
                {
                    Id = id,
                    CanEnter = Json.Bool(entry["darkest_dungeon_can_enter"], false),
                    MinimumStress = Json.Number(entry["darkest_duungeon_minimum_stress"], 0)
                };
                foreach (var buff in Json.Array(entry["darkest_dungeon_buff_ids"]))
                    if ((string)buff != null) setting.BuffIds.Add((string)buff);
                read.Add(setting);
            }
            if (read.Count > 0) rules.NeverAgain = read;

            var buffs = Json.ParseFile(files.ReadText(BuffsFile));
            foreach (var buff in Json.Array(buffs?["buffs"]))
            {
                var stat = (string)buff["stat_type"];
                switch ((string)buff["id"])
                {
                    case VeteranBuff:
                        if (stat == "resolve_xp_bonus_percent") rules.VeteranXpBonus = Json.Number(buff["amount"], rules.VeteranXpBonus);
                        break;
                    case FailureBuff:
                        if (stat == "resolve_xp_bonus_percent") rules.FailureXpBonus = Json.Number(buff["amount"], rules.FailureXpBonus);
                        break;
                    case NeverAgainBuff:
                        if (stat == "resolve_check_percent") rules.NeverAgainResolveCheck = Json.Number(buff["amount"], rules.NeverAgainResolveCheck);
                        break;
                }
            }
            return rules;
        }

        /// <summary>The setting by its id; DD1's strict one for an id the file does not know.</summary>
        public NeverAgainSetting Setting(string id)
        {
            return NeverAgain.FirstOrDefault(s => s.Id == id) ?? NeverAgain.FirstOrDefault(s => s.Id == DefaultSetting) ?? new NeverAgainSetting { Id = Strict };
        }

        // ---- how DD1 uses them (read in its executable) --------------------------------------------------

        /// <summary>
        /// Who earns the veteran's share on a quest (exe 0x9ad4f0): when anybody of the party has finished a
        /// quest in the Darkest Dungeon, every member who has NOT gets one copy of the buff, in any dungeon,
        /// however many veterans walk with them. The veterans themselves get nothing from it.
        /// </summary>
        public static List<T> Taught<T>(IEnumerable<T> party, Func<T, bool> isVeteran)
        {
            var all = party.ToList();
            return all.Any(isVeteran) ? all.Where(member => !isVeteran(member)).ToList() : new List<T>();
        }

        /// <summary>
        /// Whether a failed plot quest's roster buffs are given (exe 0x938ba1): every hero of the party is at
        /// the quest's <c>roster_buff_on_failure_minimum_party_resolve_level</c> or above. (An empty party
        /// teaches nobody anything.)
        /// </summary>
        public static bool PartyQualifies(IEnumerable<int> partyLevels, int minimumLevel)
        {
            var any = false;
            foreach (var level in partyLevels ?? new int[0])
            {
                any = true;
                if (level < minimumLevel) return false;
            }
            return any;
        }

        /// <summary>
        /// A quest's resolve experience with its bonuses (exe 0x938dd0): the buffs' shares
        /// (<c>resolve_xp_bonus_percent</c>) are added up, and what they come to of the quest's own pay is
        /// rounded UP and added: 16 at +50% is 24, 2 at +33% is 3.
        /// </summary>
        public static int WithBonus(int experience, double bonusShare)
        {
            if (experience <= 0 || bonusShare <= 0) return Math.Max(0, experience);
            // (a hair off, so that 16 x 0.5 = 8.000000001 in floating point does not become 9)
            return experience + (int)Math.Ceiling(experience * bonusShare - 1e-9);
        }
    }
}
