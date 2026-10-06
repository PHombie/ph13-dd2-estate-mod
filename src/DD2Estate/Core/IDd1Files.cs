namespace DD2Estate.Core
{
    /// <summary>
    /// Read access to the player's own Darkest Dungeon (1) install. Paths are relative to the install root and use
    /// forward slashes. The mod ships no DD1 data: every DD1 rule the core uses comes through here.
    /// </summary>
    public interface IDd1Files
    {
        bool Exists(string relativePath);

        /// <summary>Null when the file is missing.</summary>
        string ReadText(string relativePath);

        /// <summary>File names (no directory) matching a * wildcard pattern; empty when the directory is missing.</summary>
        string[] List(string relativeDir, string pattern);
    }
}
