using System;
using System.Collections.Generic;
using DD2Estate.Core;

namespace DD2Estate.CoreTests
{
    /// <summary>
    /// Where Darkest Dungeon (1) is looked for (Core/Dd1Search.cs): the order of the places, what Steam's list
    /// of libraries says, and that no place is looked at twice.
    /// </summary>
    public static class Dd1SearchTests
    {
        private const string Dd2 = @"E:\Games\steamapps\common\Darkest Dungeon II";

        // Steam's list as it writes it today: a block a library, the folder under "path", backslashes doubled.
        private const string NewList = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t\t\"label\"\t\t\"\"\n\t\t\"contentid\"\t\t\"123\"\n\t\t\"totalsize\"\t\t\"0\"\n"
                                       + "\t\t\"apps\"\n\t\t{\n\t\t\t\"228980\"\t\t\"1000\"\n\t\t\t\"220\"\t\t\"4096\"\n\t\t}\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"F:\\\\SteamLibrary\"\n\t\t\"apps\"\n\t\t{\n\t\t\t\"262060\"\t\t\"2000\"\n\t\t}\n\t}\n}\n";

        // ... and as it wrote it before: a number, then the folder.
        private const string OldList = "\"LibraryFolders\"\n{\n\t\"TimeNextStatsReport\"\t\t\"1600000000\"\n\t\"ContentStatsID\"\t\t\"-123\"\n\t\"1\"\t\t\"D:\\\\Games\\\\Steam\"\n\t\"2\"\t\t\"G:\\\\More\"\n}\n";

        private static Func<string, string> Files(params string[] pairs)
        {
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i + 1 < pairs.Length; i += 2) files[Dd1Search.Key(pairs[i])] = pairs[i + 1];
            return path => files.TryGetValue(Dd1Search.Key(path), out var text) ? text : null;
        }

        private static List<string> Folders(List<Dd1Search.Place> places)
        {
            var folders = new List<string>();
            foreach (var place in places) folders.Add(place.Folder);
            return folders;
        }

        public static void TheListOfLibrariesIsRead()
        {
            var libraries = Dd1Search.Libraries(NewList);
            Check.Equal(2, libraries.Count, "two libraries, and no app's number or size among them");
            Check.Equal(@"C:\Program Files (x86)\Steam", libraries[0], "the first, its backslashes single");
            Check.Equal(@"F:\SteamLibrary", libraries[1], "the second");
        }

        public static void TheOlderListIsRead()
        {
            var libraries = Dd1Search.Libraries(OldList);
            Check.Equal(2, libraries.Count, "two libraries (the numbers of the report are no folders)");
            Check.Equal(@"D:\Games\Steam", libraries[0], "the first");
            Check.Equal(@"G:\More", libraries[1], "the second");
        }

        public static void AListThatIsNoListGivesNothing()
        {
            Check.Equal(0, Dd1Search.Libraries(null).Count, "no file");
            Check.Equal(0, Dd1Search.Libraries("").Count, "an empty file");
            Check.Equal(0, Dd1Search.Libraries("\"libraryfolders\" { \"0\" { \"path\" \"").Count, "a file cut short");
            Check.Equal(0, Dd1Search.Libraries("not a list at all").Count, "words");
        }

        public static void TheConfigComesFirstAndIsByHand()
        {
            var places = Dd1Search.Places(@"X:\Mine\DD", Dd2, null, null, null, null);
            Check.Equal(@"X:\Mine\DD", places[0].Folder, "the config's folder first");
            Check.True(places[0].ByHand, "named by the player");
            Check.Equal(Dd1Search.ByConfig, places[0].By, "and said so");
            Check.Equal(@"E:\Games\steamapps\common\DarkestDungeon", places[1].Folder, "then the folder beside Darkest Dungeon II");
            Check.True(!places[1].ByHand, "which was found, not named");
        }

        public static void WithoutAConfigTheNeighbourComesFirst()
        {
            var places = Dd1Search.Places("", Dd2, null, null, null, null);
            Check.Equal(1, places.Count, "one place: nothing else is known");
            Check.Equal(Dd1Search.ByNeighbour, places[0].By, "beside Darkest Dungeon II");
        }

        public static void TheOrderIsNeighbourRegistryProgramFilesDrives()
        {
            var read = Files(
                @"E:\Games\steamapps\libraryfolders.vdf", "\"libraryfolders\" { \"0\" { \"path\" \"H:\\\\Lib\" } }",
                @"C:\Steam\config\libraryfolders.vdf", "\"libraryfolders\" { \"0\" { \"path\" \"c:\\\\steam\" } \"1\" { \"path\" \"F:\\\\SteamLibrary\" } }",
                @"C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf", OldList);
            var places = Dd1Search.Places("", Dd2, new[] { "c:/steam" }, new[] { @"C:\Program Files (x86)", @"C:\Program Files" }, new[] { @"C:\", @"D:\" }, read);
            var by = new List<string>();
            foreach (var place in places)
                if (by.Count == 0 || by[by.Count - 1] != place.By) by.Add(place.By);
            Check.Equal(string.Join(" > ", new[]
            {
                Dd1Search.ByNeighbour, Dd1Search.ByNeighbourLibraries, Dd1Search.ByRegistry, Dd1Search.ByRegistryLibraries,
                Dd1Search.ByProgramFiles, Dd1Search.ByProgramFilesLibraries, Dd1Search.ByProgramFiles, Dd1Search.ByDrive
            }), string.Join(" > ", by), "the order of the places");
            var folders = Folders(places);
            Check.True(folders.Contains(@"H:\Lib\steamapps\common\DarkestDungeon"), "the library the list beside Darkest Dungeon II names");
            Check.True(folders.Contains(@"c:\steam\steamapps\common\DarkestDungeon"), "the registry's Steam, its slashes turned");
            Check.True(folders.Contains(@"F:\SteamLibrary\steamapps\common\DarkestDungeon"), "a library of the registry's Steam");
            Check.True(folders.Contains(@"G:\More\steamapps\common\DarkestDungeon"), "a library of the Steam under Program Files");
            Check.True(folders.Contains(@"D:\SteamLibrary\steamapps\common\DarkestDungeon"), "a guess by drive");
            Check.True(folders.IndexOf(@"F:\SteamLibrary\steamapps\common\DarkestDungeon") < folders.IndexOf(@"D:\SteamLibrary\steamapps\common\DarkestDungeon"), "what Steam names comes before a guess");
        }

        public static void NoFoundPlaceIsLookedAtTwice()
        {
            // the registry's Steam is the library Darkest Dungeon II is in, and its list names itself
            var read = Files(@"E:\Games\steamapps\libraryfolders.vdf", "\"libraryfolders\" { \"0\" { \"path\" \"E:\\\\Games\" } \"1\" { \"path\" \"E:\\\\GAMES\\\\\" } }");
            var places = Dd1Search.Places("", Dd2, new[] { @"E:\Games", "E:/Games/" }, null, new[] { @"E:\" }, read);
            var seen = new HashSet<string>();
            foreach (var place in places) Check.True(seen.Add(Dd1Search.Key(place.Folder)), "twice: " + place.Folder);
            Check.Equal(3, places.Count, "the neighbour's (which every list names again), and the two guesses on the drive");
        }

        public static void TheConfigsFolderIsAlsoLookedAtAsSteams()
        {
            // The config names the very folder that lies beside Darkest Dungeon II (a player typed it at the main
            // menu). "Find through Steam" asks for what Steam finds whatever the config says: it must find it.
            var places = Dd1Search.Places(@"e:\games\steamapps\common\darkestdungeon\", Dd2, null, null, null, null);
            Check.Equal(2, places.Count, "the config's folder, and the same folder as the neighbour's");
            Check.True(places[0].ByHand && places[0].By == Dd1Search.ByConfig, "first as the config's");
            Check.True(!places[1].ByHand && places[1].By == Dd1Search.ByNeighbour, "then as found");
            Check.Equal(Dd1Search.Key(places[0].Folder), Dd1Search.Key(places[1].Folder), "one folder");
            var found = places.FindAll(place => !place.ByHand);
            Check.Equal(1, found.Count, "without the config's there is still a place to look at");
        }

        public static void AFileThatCannotBeReadIsNoList()
        {
            var places = Dd1Search.Places("", Dd2, new[] { @"C:\Steam" }, null, null, path => throw new UnauthorizedAccessException(path));
            Check.Equal(2, places.Count, "the neighbour and the registry's own folder; no list, no failure");
        }

        public static void NothingKnownNothingToLookAt()
        {
            Check.Equal(0, Dd1Search.Places(null, null, null, null, null, null).Count, "no place");
            Check.Equal(0, Dd1Search.Places("", "", new string[] { null, "" }, new[] { "" }, new[] { "" }, null).Count, "empty names are no places");
        }
    }
}
