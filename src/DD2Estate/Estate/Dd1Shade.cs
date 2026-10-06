using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using DD2Estate.Dd1;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// A colour DD1 gives as a darkness and a saturation instead of a tint (colours/base.colours.darkest:
    /// `colour: .id "upgrade_tree_icon_not_purchased" .darkness 0.4 .saturation 0.0`): what is not bought yet or
    /// still shut is drawn darker and with its colour taken out. A UI picture's own colour can only darken, so
    /// DD1 art is drawn from a copy shaded that way (<see cref="Art"/>), a text takes the shaded colour
    /// (<see cref="Text"/>), and a picture of DD2's is darkened and, where the shade leaves next to no colour,
    /// drawn with the material DD2 greys its own skill pictures with (<see cref="Apply"/>).
    /// </summary>
    internal struct Dd1Shade
    {
        private const string ColourTable = "colours/base.colours.darkest";
        private static readonly Regex Line = new Regex("\\.id\\s+\"?([\\w.]+)\"?\\s+(.*)");
        private static readonly Regex Part = new Regex("\\.(darkness|saturation)\\s+([\\d.]+)");

        private static Dictionary<string, Dd1Shade> _table;
        private static readonly Dictionary<string, Sprite> Copies = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static Material _grey;

        /// <summary>What the picture's brightness is multiplied by.</summary>
        public float Darkness;
        /// <summary>The share of its colour the picture keeps: 0 is grey.</summary>
        public float Saturation;

        /// <summary>The shade of a DD1 colour id; <paramref name="darkness"/> and <paramref name="saturation"/> (DD1's stock values) if the table has none of that name.</summary>
        public static Dd1Shade Of(string id, float darkness, float saturation)
        {
            if (_table == null && Dd1Install.Found) _table = Read();
            return _table != null && id != null && _table.TryGetValue(id, out var shade) ? shade : new Dd1Shade { Darkness = darkness, Saturation = saturation };
        }

        private static Dictionary<string, Dd1Shade> Read()
        {
            var table = new Dictionary<string, Dd1Shade>();
            string text = null;
            try { text = Dd1Install.ReadText(ColourTable); }
            catch (Exception e) { Plugin.Log.LogWarning("DD1 colour table could not be read: " + e.Message); }
            if (text == null) return table;
            foreach (var raw in text.Split('\n'))
            {
                var line = Line.Match(raw);
                if (!line.Success) continue;
                var parts = Part.Matches(line.Groups[2].Value);
                if (parts.Count == 0) continue;
                // A colour that names one of the two leaves the other as the art has it.
                var shade = new Dd1Shade { Darkness = 1f, Saturation = 1f };
                foreach (Match part in parts)
                {
                    if (!float.TryParse(part.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) continue;
                    if (part.Groups[1].Value == "darkness") shade.Darkness = value;
                    else shade.Saturation = value;
                }
                table[line.Groups[1].Value] = shade;
            }
            return table;
        }

        /// <summary>A colour in this shade (for a text).</summary>
        public Color Text(Color colour)
        {
            var grey = colour.r * 0.299f + colour.g * 0.587f + colour.b * 0.114f;
            return new Color((grey + (colour.r - grey) * Saturation) * Darkness, (grey + (colour.g - grey) * Saturation) * Darkness, (grey + (colour.b - grey) * Saturation) * Darkness, colour.a);
        }

        /// <summary>A copy of a DD1 picture in this shade (made once); null when the install has no such file.</summary>
        public Sprite Art(string dd1File)
        {
            if (string.IsNullOrEmpty(dd1File) || !Dd1Install.Found || !Dd1Install.Exists(dd1File)) return null;
            var key = dd1File + "|" + Darkness.ToString("0.###", CultureInfo.InvariantCulture) + "|" + Saturation.ToString("0.###", CultureInfo.InvariantCulture);
            if (Copies.TryGetValue(key, out var cached) && cached != null) return cached;
            Sprite sprite = null;
            try
            {
                var texture = Dd1Install.LoadTexture(dd1File, true, false);
                if (texture == null) return null;
                var pixels = texture.GetPixels32();
                for (var i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    var grey = p.r * 0.299f + p.g * 0.587f + p.b * 0.114f;
                    pixels[i] = new Color32(Channel(p.r, grey), Channel(p.g, grey), Channel(p.b, grey), p.a);
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                sprite.name = key;
                sprite.hideFlags = HideFlags.HideAndDontSave;
            }
            catch (Exception e) { Plugin.Log.LogWarning("DD1 picture " + dd1File + " could not be shaded: " + e.Message); }
            if (sprite != null) Copies[key] = sprite;
            return sprite;
        }

        private byte Channel(byte value, float grey)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt((grey + (value - grey) * Saturation) * Darkness), 0, 255);
        }

        /// <summary>
        /// Draws a picture that is not DD1 art in this shade: darker by its own colour, and without colour by
        /// DD2's greyscale material where the shade keeps less than half of it (and the game has the material
        /// in memory; the picture keeps its colour otherwise).
        /// </summary>
        public void Apply(Image image)
        {
            if (image == null) return;
            image.color = new Color(Darkness, Darkness, Darkness, image.color.a);
            if (Saturation >= 0.5f) return;
            var grey = Grey();
            if (grey != null) image.material = grey;
        }

        // DD2's character sheet draws a skill that is not equipped with a greyscale material of its own
        // (CharacterSheetSkillButtonBhv.SetIsGreyScale; the picture's colour still darkens it). The material is
        // a field of the button's prefab: it is taken from whichever skill button the game has loaded.
        private static Material Grey()
        {
            if (_grey != null) return _grey;
            try
            {
                var field = AccessTools.Field(typeof(Assets.Code.UI.CharacterSheetSkillButtonBhv), "m_greyscaleMaterial");
                if (field == null) return null;
                foreach (var button in Resources.FindObjectsOfTypeAll<Assets.Code.UI.CharacterSheetSkillButtonBhv>())
                {
                    _grey = field.GetValue(button) as Material;
                    if (_grey != null) break;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("DD2's greyscale material was not found: " + e.Message); }
            return _grey;
        }
    }
}
