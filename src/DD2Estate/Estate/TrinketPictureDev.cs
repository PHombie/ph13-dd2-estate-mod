using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Assets.Code.Item;
using Assets.Code.Library;
using Assets.Code.Utils;
using DD2Estate.Dev;
using Newtonsoft.Json;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// For choosing the size of the trinket pictures by looking (<see cref="TrinketPicture"/>): every trinket
    /// picture of the running game written out as a PNG of its own whole square, with a list of what each is
    /// (name, DD1 grade), so that all of them can be laid on DD1's cards at several factors side by side,
    /// outside the game. The files are for the test's scratch folder: nothing of the game's goes into the repository.
    ///
    ///   trinkets.export {"dir":"C:/.../scratch/trinkets"}   starts it (the game loads its pictures on demand,
    ///                                                       so it runs over some seconds)
    ///   trinkets.export                                      how far it has come
    /// </summary>
    [EstateModule]
    internal static class TrinketPictureDev
    {
        private const int PerFrame = 6;
        private const float Patience = 30f;         // seconds a picture may take to load before it is given up

        private static string _dir;
        private static bool _running;
        private static int _total, _written, _withoutPicture;
        private static string _failure;

        private static void Register()
        {
            AgentBridge.Register("trinkets.export", o =>
            {
                var dir = (string)o["dir"];
                if (!string.IsNullOrEmpty(dir) && !_running)
                {
                    if (!SingletonMonoBehaviour<Library<string, ItemDefinition>>.HasInstance()) return "no item library yet";
                    _dir = dir;
                    _running = true;
                    _total = _written = _withoutPicture = 0;
                    _failure = null;
                    Plugin.Host.StartCoroutine(Export());
                }
                return new { running = _running, dir = _dir, trinkets = _total, written = _written, withoutPicture = _withoutPicture, failure = _failure };
            });
        }

        private static IEnumerator Export()
        {
            var ids = new List<string>();
            var items = SingletonMonoBehaviour<Library<string, ItemDefinition>>.Instance;
            for (var i = 0; i < items.GetNumberOfLibraryElements(); i++)
            {
                var item = items.GetLibraryElementAtIndex(i);
                if (item != null && item.m_type == ItemType.TRINKET) ids.Add(item.m_id);
            }
            _total = ids.Count;
            try { Directory.CreateDirectory(_dir); }
            catch (Exception e)
            {
                _failure = e.Message;
                _running = false;
                yield break;
            }

            // DD2 hands a picture over some frames after it is first asked for
            var sprites = new Dictionary<string, Sprite>();
            var until = Time.unscaledTime + Patience;
            while (true)
            {
                var waiting = 0;
                foreach (var id in ids)
                {
                    if (sprites.ContainsKey(id)) continue;
                    var sprite = Trinkets.Icon(id);
                    if (sprite != null) sprites[id] = sprite;
                    else if (!Trinkets.HasNoIcon(id)) waiting++;
                }
                if (waiting == 0 || Time.unscaledTime > until) break;
                yield return null;
            }
            _withoutPicture = ids.Count - sprites.Count;

            var rows = new List<object>();
            var inFrame = 0;
            foreach (var group in sprites.GroupBy(pair => pair.Value.texture))
            {
                var texture = group.Key;
                if (texture == null) continue;
                var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(texture, target);
                foreach (var pair in group)
                {
                    object row = null;
                    try { row = Write(pair.Key, pair.Value, target); }
                    catch (Exception e) { Plugin.Log.LogWarning("Trinket pictures: " + pair.Key + " could not be written: " + e.Message); }
                    if (row != null)
                    {
                        rows.Add(row);
                        _written++;
                    }
                    if (++inFrame < PerFrame) continue;
                    inFrame = 0;
                    yield return null;
                }
                RenderTexture.ReleaseTemporary(target);
            }
            try { File.WriteAllText(Path.Combine(_dir, "index.json"), JsonConvert.SerializeObject(rows, Formatting.Indented)); }
            catch (Exception e) { _failure = e.Message; }
            _running = false;
        }

        // The sprite's whole square (its clear margin with it: a packed picture keeps only the painted part in
        // its texture, at an offset), as a UI picture draws it.
        private static object Write(string id, Sprite sprite, RenderTexture drawn)
        {
            var size = sprite.rect.size;
            var region = sprite.textureRect;
            var offset = sprite.textureRectOffset;
            int width = Mathf.RoundToInt(size.x), height = Mathf.RoundToInt(size.y);
            int w = Mathf.RoundToInt(region.width), h = Mathf.RoundToInt(region.height);
            var before = RenderTexture.active;
            Texture2D part = null, whole = null;
            try
            {
                RenderTexture.active = drawn;
                part = new Texture2D(w, h, TextureFormat.RGBA32, false);
                part.ReadPixels(new Rect(region.x, region.y, w, h), 0, 0);
                part.Apply();
                whole = new Texture2D(width, height, TextureFormat.RGBA32, false);
                whole.SetPixels32(new Color32[width * height]);
                whole.SetPixels32(Mathf.RoundToInt(offset.x), Mathf.RoundToInt(offset.y), w, h, part.GetPixels32());
                whole.Apply();
                var file = id + ".png";
                File.WriteAllBytes(Path.Combine(_dir, file), whole.EncodeToPNG());
                return new
                {
                    id, name = Trinkets.Name(id), grade = Trinkets.Grade(id), dd2 = Trinkets.SubTypeOf(id), forClass = RealmInventory.ClassOf(id), file,
                    w = width, h = height, sprite = sprite.name, packed = sprite.packed
                };
            }
            finally
            {
                RenderTexture.active = before;
                if (part != null) UnityEngine.Object.Destroy(part);
                if (whole != null) UnityEngine.Object.Destroy(whole);
            }
        }
    }
}
