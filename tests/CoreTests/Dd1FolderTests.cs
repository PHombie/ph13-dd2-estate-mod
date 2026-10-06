using System;
using System.Collections.Generic;
using DD2Estate.Core;

namespace DD2Estate.CoreTests
{
    /// <summary>
    /// A folder named as the Darkest Dungeon (1) install (Core/Dd1Folder.cs): a right one is taken however it
    /// was typed, a wrong one is refused with a sentence that says what is wrong.
    /// </summary>
    public static class Dd1FolderTests
    {
        private const string Install = @"D:\Games\DarkestDungeon";

        // A file system made of names: an install at Install, whole or with files taken out.
        private sealed class Disk
        {
            public readonly HashSet<string> Files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> Folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public static string Norm(string path) => path.Replace('/', '\\').TrimEnd('\\');

            public Disk With(string root, params string[] without)
            {
                var gone = new HashSet<string>(without, StringComparer.OrdinalIgnoreCase);
                var files = new List<string>(Dd1Folder.NeededFiles) { Dd1Folder.Marker, "_windows/Darkest.exe" };
                foreach (var file in files)
                {
                    if (gone.Contains(file)) continue;
                    var full = Norm(root + "\\" + file);
                    Files.Add(full);
                    for (var at = full.LastIndexOf('\\'); at > 2; at = full.LastIndexOf('\\', at - 1)) Folders.Add(full.Substring(0, at));
                }
                foreach (var dir in Dd1Folder.NeededFolders)
                    if (!gone.Contains(dir)) Folders.Add(Norm(root + "\\" + dir));
                return this;
            }

            public bool File(string path) => Files.Contains(Norm(path));
            public bool Folder(string path) => Folders.Contains(Norm(path));
        }

        private static Dd1Folder.Verdict Ask(Disk disk, string typed) => Dd1Folder.Check(typed, disk.File, disk.Folder);

        public static void TheInstallIsTaken()
        {
            var verdict = Ask(new Disk().With(Install), Install);
            Check.True(verdict.Ok, "a whole install: " + verdict.Problem);
            Check.Equal(Install, verdict.Folder, "the folder as typed");
            Check.True(verdict.Problem == null, "nothing to say");
        }

        public static void TypingIsForgiven()
        {
            var disk = new Disk().With(Install);
            foreach (var typed in new[]
                     {
                         "  " + Install + "  ", "\"" + Install + "\"", Install + "\\", Install + "/", "'" + Install + "'",
                         Install.Replace('\\', '/'), "\"" + Install + "\\\""
                     })
            {
                var verdict = Ask(disk, typed);
                Check.True(verdict.Ok, "typed as [" + typed + "]: " + verdict.Problem);
                Check.Equal(Disk.Norm(Install), Disk.Norm(verdict.Folder), "typed as [" + typed + "]");
            }
        }

        public static void TheFolderMeantIsFoundNearby()
        {
            var disk = new Disk().With(Install);
            // the folder of the program, the program itself, the folder the install lies in
            foreach (var typed in new[] { Install + @"\_windows", Install + @"\_windows\Darkest.exe", @"D:\Games" })
            {
                var verdict = Ask(disk, typed);
                Check.True(verdict.Ok, "typed as [" + typed + "]: " + verdict.Problem);
                Check.Equal(Disk.Norm(Install), Disk.Norm(verdict.Folder), "the install, not what was typed: [" + typed + "]");
            }
        }

        public static void NothingTypedIsNotAFolder()
        {
            var disk = new Disk().With(Install);
            foreach (var typed in new[] { null, "", "   ", "\"\"" })
            {
                var verdict = Ask(disk, typed);
                Check.True(!verdict.Ok && verdict.Folder == null, "nothing taken");
                Check.Equal("No folder was given.", verdict.Problem, "the words");
            }
        }

        public static void AFolderThatIsNotThereIsRefused()
        {
            var verdict = Ask(new Disk().With(Install), @"D:\Nowhere\DarkestDungeon");
            Check.True(!verdict.Ok && verdict.Folder == null, "not taken");
            Check.True(verdict.Problem.StartsWith("There is no such folder: ", StringComparison.Ordinal) && verdict.Problem.EndsWith(@"D:\Nowhere\DarkestDungeon", StringComparison.Ordinal), "the words name it: " + verdict.Problem);
        }

        public static void AnotherGamesFolderIsRefused()
        {
            var disk = new Disk().With(Install);
            disk.Folders.Add(@"D:\Games\Darkest Dungeon II");
            var verdict = Ask(disk, @"D:\Games\Darkest Dungeon II");
            Check.True(!verdict.Ok && verdict.Folder == null, "not taken");
            Check.True(verdict.Problem.Contains("not a Darkest Dungeon install") && verdict.Problem.Contains(Dd1Folder.Marker), "the words say what is looked for: " + verdict.Problem);
        }

        public static void AFileIsNotAFolder()
        {
            var disk = new Disk().With(Install);
            disk.Files.Add(@"D:\Games\notes.txt");
            var verdict = Ask(disk, @"D:\Games\notes.txt");
            Check.True(!verdict.Ok, "not taken");
            Check.True(verdict.Problem.StartsWith("That is a file, not a folder", StringComparison.Ordinal), verdict.Problem);
        }

        public static void AnInstallWithFilesMissingIsRefusedAndTheyAreNamed()
        {
            var verdict = Ask(new Disk().With(Install, "shared/rules.json", "audio"), Install);
            Check.True(!verdict.Ok, "an install the Estate could not read is not taken");
            Check.Equal(Install, verdict.Folder, "but it is known for what it is");
            Check.True(verdict.Problem.Contains("shared/rules.json") && verdict.Problem.Contains("audio/"), "the missing are named: " + verdict.Problem);
            Check.True(verdict.Problem.Contains("Verify"), "and what to do about it");

            // many missing: three named, the rest counted
            var many = Ask(new Disk().With(Install, "shared/rules.json", "fonts/fonts.darkest", "campaign/estate/estate.json", "colours/base.colours.darkest", "heroes"), Install);
            Check.True(!many.Ok && many.Problem.Contains("and 2 more"), "five missing, three named: " + many.Problem);
        }

        public static void ADiskThatThrowsIsAFolderThatIsNotThere()
        {
            var verdict = Dd1Folder.Check("D:\\a|b", path => throw new ArgumentException("illegal characters"), path => throw new ArgumentException("illegal characters"));
            Check.True(!verdict.Ok && verdict.Problem.StartsWith("There is no such folder", StringComparison.Ordinal), verdict.Problem);
        }

        public static void TheRealInstallPasses()
        {
            if (!Dd1.Available) throw new SkipException("no DD1 install");
            var verdict = Dd1Folder.Check(Dd1.Root, System.IO.File.Exists, System.IO.Directory.Exists);
            Check.True(verdict.Ok, "the install the tests read: " + verdict.Problem);
            var inside = Dd1Folder.Check(System.IO.Path.Combine(Dd1.Root, "_windows"), System.IO.File.Exists, System.IO.Directory.Exists);
            Check.True(inside.Ok, "its program folder leads to it: " + inside.Problem);
            var other = Dd1Folder.Check(System.IO.Path.Combine(Dd1.Root, "dungeons"), System.IO.File.Exists, System.IO.Directory.Exists);
            Check.True(!other.Ok, "a folder inside it is not it");
        }
    }
}
