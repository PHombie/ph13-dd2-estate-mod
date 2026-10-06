using System;

namespace DD2Estate.Core
{
    /// <summary>Who is caught off guard as a fight begins.</summary>
    public enum SurpriseSide { None, Party, Monsters }

    /// <summary>
    /// What a DD1 monster says about the fight it is in (its <c>.info.darkest</c>: <c>battle_modifier</c> and
    /// <c>torchlight_modifier</c>). The ordinary monster can surprise and be surprised; DD1's bosses can
    /// neither; the Shambler cannot be surprised, always surprises, and holds the torch at nothing.
    /// </summary>
    public sealed class MonsterBattleModifier
    {
        public bool CanSurprise = true, CanBeSurprised = true, AlwaysSurprise, AlwaysBeSurprised;
        /// <summary><c>torchlight_modifier</c> .min and .max: the torchlight the fight is held to; null when the monster leaves the torch alone.</summary>
        public double[] Torchlight;

        /// <summary>Any monster of the ordinary kind.</summary>
        public static MonsterBattleModifier Ordinary => new MonsterBattleModifier();

        /// <summary>DD1's bosses, every one of them: <c>.can_surprise False .can_be_surprised False</c>.</summary>
        public static MonsterBattleModifier Boss => new MonsterBattleModifier { CanSurprise = false, CanBeSurprised = false };

        public static string FileOf(string monsterClass)
        {
            var cut = monsterClass.LastIndexOf('_');
            var family = cut > 0 ? monsterClass.Substring(0, cut) : monsterClass;
            return "monsters/" + family + "/" + monsterClass + "/" + monsterClass + ".info.darkest";
        }

        /// <summary>Reads a monster class (shambler_A); the ordinary monster when DD1 has no such file.</summary>
        public static MonsterBattleModifier Load(IDd1Files files, string monsterClass)
        {
            var text = files != null && !string.IsNullOrEmpty(monsterClass) ? files.ReadText(FileOf(monsterClass)) : null;
            return Parse(text);
        }

        public static MonsterBattleModifier Parse(string infoText)
        {
            var modifier = new MonsterBattleModifier();
            foreach (var block in DarkestFile.Parse(infoText))
            {
                if (block.Name == "battle_modifier")
                {
                    modifier.CanSurprise = Flag(block, "can_surprise", true);
                    modifier.CanBeSurprised = Flag(block, "can_be_surprised", true);
                    modifier.AlwaysSurprise = Flag(block, "always_surprise", false);
                    modifier.AlwaysBeSurprised = Flag(block, "always_be_surprised", false);
                }
                else if (block.Name == "torchlight_modifier" && block.Fields.ContainsKey("min"))
                {
                    var min = block.Number("min", 0, 0);
                    modifier.Torchlight = new[] { min, block.Number("max", 0, min) };
                }
            }
            return modifier;
        }

        private static bool Flag(DarkestBlock block, string field, bool fallback)
        {
            var text = block.Text(field);
            return text == null ? fallback : string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>The three weights of DD1's surprise roll; a side's probability is its weight over their sum.</summary>
    public struct SurpriseOdds
    {
        public double None, Party, Monsters;

        public double Sum => None + Party + Monsters;

        /// <summary>The probability that the party is caught off guard.</summary>
        public double PartyShare => Sum > 0 ? Party / Sum : 0;

        /// <summary>The probability that the monsters are.</summary>
        public double MonstersShare => Sum > 0 ? Monsters / Sum : 0;
    }

    /// <summary>
    /// DD1's surprise at the start of a fight. The numbers are the install's <c>shared/rules.json</c>; how they
    /// combine is DD1's own code (read in its executable, the roll at 0x9b3ec0 and Battle::Create at 0x953e80):
    ///
    ///   each side starts from a base chance by where the fight is (a hallway or a room) and whether the party
    ///   knew of it (the tile was scouted, or walked before); a party of one starts from nothing;
    ///   the torchlight band adds its share (<c>heroes_surprised_increase</c>, <c>monsters_surprised_increase</c>,
    ///   points of a hundred), every hero's buffs theirs (<c>party_surprise_chance</c>,
    ///   <c>monsters_surprise_chance</c>: camping skills);
    ///   each side's chance is kept between nothing and its maximum (0.65);
    ///   then ONE roll among three: "nobody" weighs what is left of 1, and never less than 0.25. So the two
    ///   sides are never both surprised, and at chances above 0.75 together they give way to each other.
    ///
    /// A known fight's base chance for the party is -1: no darkness brings it above nothing, which is DD1's
    /// "a scouted fight never surprises the party". The monsters of the fight have the last word
    /// (<see cref="MonsterBattleModifier"/>). The initial values of the fields are DD1's stock values,
    /// FALLBACKS for a missing file or key.
    /// </summary>
    public sealed class SurpriseRules
    {
        public const string RulesFile = "shared/rules.json";

        /// <summary><c>surprise_corridor_*_base_chance</c>, <c>surprise_room_*_base_chance</c>.</summary>
        public double CorridorParty = 0.1, CorridorMonsters = 0.1, RoomParty = 0.1, RoomMonsters = 0.1;
        /// <summary><c>surprise_known_corridor_*_base_chance</c>, <c>surprise_known_room_*_base_chance</c>.</summary>
        public double KnownCorridorParty = -1, KnownCorridorMonsters = 0.25, KnownRoomParty = -1, KnownRoomMonsters = 0.25;
        /// <summary><c>surprise_max_party_surprised_chance</c>, <c>surprise_max_monsters_surprised_chance</c>.</summary>
        public double MaxParty = 0.65, MaxMonsters = 0.65;

        // DD1 behaviour, not in data (a constant of the roll, exe 0x1123234): "nobody is surprised" never weighs less.
        public const double LeastNobody = 0.25;

        public static SurpriseRules Load(IDd1Files files)
        {
            var rules = new SurpriseRules();
            var raw = Json.ParseFile(files.ReadText(RulesFile));
            if (raw == null) return rules;
            rules.CorridorParty = Json.Number(raw["surprise_corridor_party_base_chance"], rules.CorridorParty);
            rules.CorridorMonsters = Json.Number(raw["surprise_corridor_monsters_base_chance"], rules.CorridorMonsters);
            rules.RoomParty = Json.Number(raw["surprise_room_party_base_chance"], rules.RoomParty);
            rules.RoomMonsters = Json.Number(raw["surprise_room_monsters_base_chance"], rules.RoomMonsters);
            rules.KnownCorridorParty = Json.Number(raw["surprise_known_corridor_party_base_chance"], rules.KnownCorridorParty);
            rules.KnownCorridorMonsters = Json.Number(raw["surprise_known_corridor_monsters_base_chance"], rules.KnownCorridorMonsters);
            rules.KnownRoomParty = Json.Number(raw["surprise_known_room_party_base_chance"], rules.KnownRoomParty);
            rules.KnownRoomMonsters = Json.Number(raw["surprise_known_room_monsters_base_chance"], rules.KnownRoomMonsters);
            rules.MaxParty = Json.Number(raw["surprise_max_party_surprised_chance"], rules.MaxParty);
            rules.MaxMonsters = Json.Number(raw["surprise_max_monsters_surprised_chance"], rules.MaxMonsters);
            return rules;
        }

        /// <summary>The file's base chance that the party is surprised, before light and buffs.</summary>
        public double PartyBase(bool room, bool known) => known ? (room ? KnownRoomParty : KnownCorridorParty) : (room ? RoomParty : CorridorParty);

        /// <summary>The file's base chance that the monsters are.</summary>
        public double MonstersBase(bool room, bool known) => known ? (room ? KnownRoomMonsters : KnownCorridorMonsters) : (room ? RoomMonsters : CorridorMonsters);

        /// <summary>
        /// The party's chance before the roll: the base (nothing for a party of one), the band's
        /// <c>heroes_surprised_increase</c> and the heroes' own <paramref name="buff"/> (a share: -0.2 for a
        /// camping skill that makes it less likely), kept between nothing and <see cref="MaxParty"/>.
        /// </summary>
        public double PartyChance(bool room, bool known, LightBand band, double buff, int partySize = 4)
        {
            var chance = (partySize == 1 ? 0 : PartyBase(room, known)) + (band != null ? band.Value("heroes_surprised_increase") / 100 : 0) + buff;
            return chance < 0 ? 0 : Math.Min(MaxParty, chance);
        }

        /// <summary>The monsters' chance before the roll: the base, the band's <c>monsters_surprised_increase</c> and the heroes' buff, up to <see cref="MaxMonsters"/>.</summary>
        public double MonstersChance(bool room, bool known, LightBand band, double buff)
        {
            var chance = MonstersBase(room, known) + (band != null ? band.Value("monsters_surprised_increase") / 100 : 0) + buff;
            return chance < 0 ? 0 : Math.Min(MaxMonsters, chance);
        }

        /// <summary>The weights of the one roll: the two chances, and "nobody" with what is left of 1 but never under <see cref="LeastNobody"/>.</summary>
        public SurpriseOdds Odds(bool room, bool known, LightBand band, double partyBuff, double monstersBuff, int partySize = 4)
        {
            var party = PartyChance(room, known, band, partyBuff, partySize);
            var monsters = MonstersChance(room, known, band, monstersBuff);
            return new SurpriseOdds { Party = party, Monsters = monsters, None = Math.Max(LeastNobody, Math.Min(1, 1 - (party + monsters))) };
        }

        /// <summary>
        /// The roll as a fight begins (one draw of the dice), before the monsters have their word:
        /// <see cref="Settle"/>.
        /// </summary>
        public SurpriseSide Roll(bool room, bool known, LightBand band, double partyBuff, double monstersBuff, int partySize, Rng rng)
        {
            var odds = Odds(room, known, band, partyBuff, monstersBuff, partySize);
            var roll = rng.NextDouble() * odds.Sum;
            if (roll < odds.None) return SurpriseSide.None;
            roll -= odds.None;
            if (odds.Party > 0 && roll < odds.Party) return SurpriseSide.Party;
            return odds.Monsters > 0 ? SurpriseSide.Monsters : odds.Party > 0 ? SurpriseSide.Party : SurpriseSide.None;
        }

        /// <summary>
        /// What the fight's monsters make of the roll (DD1's Battle::Create): one that always surprises has the
        /// party surprised whatever was rolled; else one that is always surprised is; else the party is
        /// surprised only by monsters that can surprise, and the monsters only if they can be.
        /// </summary>
        public static SurpriseSide Settle(SurpriseSide rolled, MonsterBattleModifier monsters)
        {
            if (monsters == null) return rolled;
            if (monsters.AlwaysSurprise) return SurpriseSide.Party;
            if (monsters.AlwaysBeSurprised) return SurpriseSide.Monsters;
            if (rolled == SurpriseSide.Party && !monsters.CanSurprise) return SurpriseSide.None;
            if (rolled == SurpriseSide.Monsters && !monsters.CanBeSurprised) return SurpriseSide.None;
            return rolled;
        }
    }
}
