using System;
using System.Collections.Generic;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The DD1 town view: sky, backdrop, ground, scenery and the ten buildings, composed from the player's DD1
    /// install the way campaign/town/town.layout.darkest describes it. tools/preview_hamlet.py is the reference
    /// for the maths here (it renders the same composition to a PNG): change one, change the other.
    ///
    /// Layout rules, worked out against DD1's own files:
    /// - `pos3d` is a world position (x right, y up, z away) seen by a camera at `camera_position` whose focal
    ///   length is its distance to the z = 0 plane, so that plane is 1:1 with the 1920x1080 screen.
    /// - Depth moves a sprite's anchor and sets the draw order (far first); it does not shrink the sprite.
    ///   Every building and scenery layer is drawn at native size, bottom-centre on its projected point.
    /// - The ground is the one real 3D surface: a quad from `ground_start` (near) to `ground_end` (far).
    /// - DD1's alert over a building (fx/estate_exclamation) has its middle at the projected `pos3d` plus
    ///   (`alert_offset.x`, `alert_offset.y` - `bbox_size.y` / 2): the middle of a box of the hover box's size
    ///   that stands on the projected point. `bbox_offset` is no part of it. Measured on a real frame of DD1's
    ///   hamlet with tools/dd1_fit_sprite.py (tavern 480, 746.5; abbey 948, 421.5; graveyard 960, 549.5;
    ///   statue 940, 774: the rule gives each to the pixel).
    ///
    /// What moves in DD1's town is in the buildings' skeletons: the animation "idle" lets lit windows breathe
    /// and smoke rise ("active", for the building under the pointer, is the same with the silhouette shown).
    /// A building's own two pictures are images here, "idle" taking the pointer by its painted pixels and
    /// "active" switched on under it; the rest of the skeleton is one sprite that plays "idle".
    /// </summary>
    internal class HamletScene : MonoBehaviour
    {
        public const float Width = 1920f;
        public const float Height = 1080f;

        private const string TownDir = "campaign/town/";
        private const string SlotActive = "active";     // hover silhouette, drawn behind idle; hidden at rest
        private const string SlotIdle = "idle";
        private const string AnimationIdle = "idle";    // a building's lights and smoke, round and round
        // DD1's mark over a building with news: one animation, "alert", a second long (the mark and its glow swell).
        private const string AlertSprite = "fx/estate_exclamation/estate_exclamation.sprite";
        private const string AlertAnimation = "alert";
        private const float AlertSeconds = 0.25f;       // how often the buildings are asked for news
        // DD1's sounds of the town screen: the pointer coming over a building, a click on one still boarded up.
        private const string HoverSound = "ui/town/button_mouse_over_town";
        private const string LockedSound = "ui/town/button_click_locked";
        // sky_anim speeds read as pixels per frame of DD1's 60 fps loop (not verified against the game).
        private const float SkyFramesPerSecond = 60f;

        /// <summary>Raised with the building id when a building is clicked.</summary>
        public static event Action<string> BuildingClicked;

        /// <summary>DD1's own building order (its quick navigation bar). circus (Arena DLC) is left out.</summary>
        public static readonly string[] BuildingIds =
        {
            "stage_coach", "blacksmith", "guild", "camping_trainer", "tavern",
            "abbey", "sanitarium", "nomad_wagon", "graveyard", "statue"
        };

        // FALLBACK: plain English for a DD1 install whose string table cannot be read (town_name_<id>, through ActivityText).
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "stage_coach", "Stage Coach" }, { "abbey", "Abbey" }, { "tavern", "Tavern" },
            { "sanitarium", "Sanitarium" }, { "blacksmith", "Blacksmith" }, { "guild", "Guild" },
            { "camping_trainer", "Survivalist" }, { "nomad_wagon", "Nomad Wagon" },
            { "graveyard", "Graveyard" }, { "statue", "Ancestor's Statue" }
        };

        private struct State
        {
            public int Level;
            public bool Locked;
        }

        private class Building
        {
            public string Id;
            public RectTransform Root;      // the skeleton origin: bottom-centre of the art
            public Vector2 Plate;           // top-left of the name plate, town pixels
            public Vector2 AlertAt;         // middle of DD1's alert, town pixels
            public GameObject Alert;
            public GameObject Active;
            public string Stem;             // skeleton on show, e.g. town_abbey_level02
            public string Atlas;
        }

        private class SkyLayer
        {
            public RawImage Image;
            public float Speed, Offset, TextureWidth;
        }

        // Kept outside the scene so game state can set buildings before the hamlet is first opened.
        private static readonly Dictionary<string, State> States = new Dictionary<string, State>();
        private static HamletScene _current;

        private readonly Dictionary<string, Building> _buildings = new Dictionary<string, Building>();
        private readonly List<SkyLayer> _sky = new List<SkyLayer>();
        private RectTransform _town;
        private DarkestFile _layout;
        private Vector3 _camera;
        private Texture2D _groundTexture;
        private RectTransform _plate;
        private CanvasGroup _plateGroup;
        private TextMeshProUGUI _plateLabel, _plateSummary;
        // The plate fades from where it is to shown or hidden: building_name_fade_anim (0.3 s, "easeInQuad").
        private float _plateFrom, _plateTarget, _plateShare = 1f;
        private float _plateFadeSeconds = 0.3f;
        private bool _plateEasesIn = true;
        private float _nextAlerts;
        private string _hovered;

        public static string DisplayName(string id)
        {
            return Names.TryGetValue(id, out var name) ? name : id;
        }

        /// <summary>
        /// Chooses a building's art: upgrade level 1..3 and whether it shows its boarded-up locked version.
        /// Can be called at any time; the scene picks it up when it exists.
        /// </summary>
        public static void SetBuilding(string id, int level, bool locked)
        {
            States[id] = new State { Level = Mathf.Clamp(level, 1, 3), Locked = locked };
            if (_current == null) return;
            try { _current.Refresh(id); }
            catch (Exception e) { Plugin.Log.LogWarning("Hamlet: building " + id + " failed: " + e); }
        }

        /// <summary>Dev bridge: the art level and lock a building was last given, and the skeleton it shows (null: no town built yet).</summary>
        public static object Describe(string id)
        {
            States.TryGetValue(id, out var state);
            Building building = null;
            if (_current != null) _current._buildings.TryGetValue(id, out building);
            return new { level = Mathf.Max(1, state.Level), locked = state.Locked, shows = building != null ? building.Stem : null };
        }

        /// <summary>
        /// Builds the town under <paramref name="parent"/> as a clipped 1920x1080 rect at its centre.
        /// Null (and nothing left behind) if it cannot be built from the DD1 install.
        /// </summary>
        public static HamletScene Build(RectTransform parent)
        {
            var layout = DarkestFile.Load(TownDir + "town.layout.darkest");
            var screen = layout != null ? layout.Find("town_screen_layout") : null;
            if (screen == null)
            {
                Plugin.Log.LogWarning("Hamlet: town.layout.darkest has no town_screen_layout");
                return null;
            }

            var centre = new Vector2(0.5f, 0.5f);
            var town = UiKit.Rect("Town", parent).Place(centre, centre, Vector2.zero, new Vector2(Width, Height));
            town.gameObject.AddComponent<RectMask2D>();     // scenery and the scrolling sky overhang the view
            var scene = town.gameObject.AddComponent<HamletScene>();
            scene._town = town;
            scene._layout = layout;
            scene._camera = screen.Vector3("camera_position");
            if (scene._camera.z >= 0f)
            {
                Plugin.Log.LogWarning("Hamlet: no usable camera_position in town.layout.darkest, using the stock one");
                scene._camera = new Vector3(960f, 322f, -1251f);
            }

            try
            {
                // Sibling order is draw order: back to front.
                var anim = DarkestFile.Load(TownDir + "town.anim.darkest");
                scene.BuildSky(anim != null ? anim.Find("sky_anim") : null);
                if (screen.Has("backdrop_position")) scene.AddAnchored("Backdrop", "town_backdrop.png", screen.Vector3("backdrop_position"));
                scene.BuildGround(screen);
                scene.BuildTown(screen);
                scene.BuildAlerts();
                scene.BuildNamePlate(anim != null ? anim.Find("building_name_fade_anim") : null);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Hamlet: town view failed: " + e);
                Destroy(town.gameObject);
                return null;
            }
            _current = scene;
            // DD1 regrades the town as a visit goes on: the pictures hanging on the town's root are its to grade
            TownTime.Attach(town);
            return scene;
        }

        /// <summary>World point to town pixels from the top-left corner, y down.</summary>
        private Vector2 Project(Vector3 world)
        {
            float k = -_camera.z / (world.z - _camera.z);
            return new Vector2(Width * 0.5f + (world.x - _camera.x) * k, Height * 0.5f - (world.y - _camera.y) * k);
        }

        private void BuildSky(DarkestFile.Block speeds)
        {
            var background = Dd1Install.Sprite(TownDir + "town_bg.png");
            if (background != null) ((RectTransform)UiKit.Image("Background", _town, background).transform).Stretch();

            var sky = _layout.Find("sky_layout");
            AddSkyLayer("Sky1", "sky/sky01.png", sky != null ? sky.Vector2("pos1") : Vector2.zero, speeds != null ? speeds.Float("speedX1") : 0f);
            AddSkyLayer("Sky2", "sky/sky02.png", sky != null ? sky.Vector2("pos2") : Vector2.zero, speeds != null ? speeds.Float("speedX2") : 0f);

            // The ruined manor on its hill: a flat 2D layer like the sky, placed by its top-left corner.
            var midground = _layout.Find("midground_layout");
            var ruins = Dd1Install.Sprite(TownDir + "sky/ruins.png");
            if (ruins != null)
            {
                var image = UiKit.Image("Midground", _town, ruins);
                ((RectTransform)image.transform).PlaceTopLeft(midground != null ? midground.Vector2("pos") : Vector2.zero, new Vector2(0f, 1f), ruins.rect.size);
            }
        }

        private void AddSkyLayer(string name, string file, Vector2 position, float speed)
        {
            var texture = Dd1Install.Texture(TownDir + file);
            if (texture == null) return;
            texture.wrapModeU = TextureWrapMode.Repeat;     // the layer is a band that scrolls sideways for ever
            var rt = UiKit.Rect(name, _town).PlaceTopLeft(new Vector2(0f, position.y), new Vector2(0f, 1f), new Vector2(Width, texture.height));
            var image = rt.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            var layer = new SkyLayer { Image = image, Speed = speed * SkyFramesPerSecond, Offset = position.x, TextureWidth = texture.width };
            image.uvRect = new Rect(-layer.Offset / layer.TextureWidth, 0f, Width / layer.TextureWidth, 1f);
            _sky.Add(layer);
        }

        /// <summary>A scenery image at native size with its bottom-centre on the projected world position.</summary>
        private void AddAnchored(string name, string file, Vector3 world)
        {
            var sprite = Dd1Install.Sprite(TownDir + file);
            if (sprite == null) return;
            var image = UiKit.Image(name, _town, sprite);
            ((RectTransform)image.transform).PlaceTopLeft(Project(world), new Vector2(0.5f, 0f), sprite.rect.size);
        }

        private void BuildGround(DarkestFile.Block screen)
        {
            if (!screen.Has("ground_start") || !screen.Has("ground_end")) return;
            var near = screen.Vector3("ground_start");
            var far = screen.Vector3("ground_end");
            // Mip-mapped: the far rows are squeezed to a fraction of the texture's size.
            _groundTexture = Dd1Install.LoadTexture(TownDir + "town_ground.png", false, true);
            if (_groundTexture == null) return;

            const int columns = 16, rows = 48;
            var points = new Vector2[(columns + 1) * (rows + 1)];
            for (int row = 0; row <= rows; row++)
            {
                float t = (float)row / rows;
                for (int column = 0; column <= columns; column++)
                {
                    float s = (float)column / columns;
                    var p = Project(new Vector3(Mathf.Lerp(near.x, far.x, s), Mathf.Lerp(near.y, far.y, t), Mathf.Lerp(near.z, far.z, t)));
                    points[row * (columns + 1) + column] = new Vector2(p.x, -p.y);
                }
            }
            var rt = UiKit.Rect("Ground", _town).PlaceTopLeft(Vector2.zero, new Vector2(0f, 1f), new Vector2(Width, Height));
            var ground = rt.gameObject.AddComponent<HamletGround>();
            ground.raycastTarget = false;
            ground.Set(_groundTexture, points, columns, rows);

            // The ground stops short of the bottom of the screen (DD1's estate bar covers the rest); its last
            // texture row is black, so black carries it down.
            float edge = Project(near).y;
            if (edge < Height)
            {
                var shade = UiKit.Image("Foreground", _town, null, Color.black);
                ((RectTransform)shade.transform).PlaceTopLeft(new Vector2(0f, edge), new Vector2(0f, 1f), new Vector2(Width, Height - edge));
            }
        }

        private struct Entry
        {
            public float Depth;         // world z
            public int Kind;            // 0 scenery, 1 building
            public float Order;         // the 2D layout's draw order (third value of `.pos`), larger = further
            public string Name;
            public string File;
            public Vector3 World;
        }

        // Scenery and buildings share one depth-sorted list: the cliff stands behind the survivalist, the tree
        // behind the blacksmith, the bridge in front of everything.
        private void BuildTown(DarkestFile.Block screen)
        {
            var entries = new List<Entry>();
            AddScenery(entries, screen, "LeftCliff", "left_cliff_position", "town_left_cliff.png");
            AddScenery(entries, screen, "Bridge", "bridge_position", "town_bridge.png");
            AddScenery(entries, screen, "RightTree", "right_tree_position", "town_right_tree.png");
            foreach (var id in BuildingIds)
            {
                var block = _layout.Find(id + "_layout");
                if (block == null || !block.Has("pos3d")) continue;
                var world = block.Vector3("pos3d");
                entries.Add(new Entry { Depth = world.z, Kind = 1, Order = block.Float("pos", 2), Name = id, World = world });
            }
            entries.Sort((a, b) =>
            {
                if (a.Depth != b.Depth) return b.Depth.CompareTo(a.Depth);      // far first
                if (a.Kind != b.Kind) return a.Kind.CompareTo(b.Kind);
                if (a.Order != b.Order) return b.Order.CompareTo(a.Order);
                return string.CompareOrdinal(a.Name, b.Name);
            });

            foreach (var entry in entries)
            {
                if (entry.Kind == 0)
                {
                    AddAnchored(entry.Name, entry.File, entry.World);
                    continue;
                }
                try
                {
                    AddBuilding(entry.Name, entry.World);
                    Refresh(entry.Name);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Hamlet: building " + entry.Name + " failed: " + e);
                }
            }
        }

        private static void AddScenery(List<Entry> entries, DarkestFile.Block screen, string name, string key, string file)
        {
            if (!screen.Has(key)) return;
            var world = screen.Vector3(key);
            entries.Add(new Entry { Depth = world.z, Kind = 0, Name = name, File = file, World = world });
        }

        private void AddBuilding(string id, Vector3 world)
        {
            var block = _layout.Find(id + "_layout");
            var origin = Project(world);
            var root = UiKit.Rect("Building." + id, _town).PlaceTopLeft(origin, new Vector2(0.5f, 0.5f), Vector2.zero);
            float scale = block.Float("scale", 0, 1f);
            root.localScale = new Vector3(scale, scale, 1f);

            // The layout's hover box sits bottom-centre on the origin, offset x right / y up. The name is offset
            // from the centre of that box, y down. DD1's alert counts from the middle of a box of that size
            // standing on the origin itself, without bbox_offset (measured: see the class's notes); whether the
            // name does the same has not been seen in DD1, so the name stays where it was.
            var boxOffset = block.Vector2("bbox_offset");
            var boxSize = block.Vector2("bbox_size");
            var boxCentre = new Vector2(origin.x + boxOffset.x, origin.y - boxOffset.y - boxSize.y * 0.5f);
            var text = _layout.Find("building_text_layout");
            var plateOffset = text != null ? text.Vector2("background_offset") : Vector2.zero;
            _buildings[id] = new Building
            {
                Id = id, Root = root, Plate = boxCentre + block.Vector2("text_offset") + plateOffset,
                AlertAt = new Vector2(origin.x, origin.y - boxSize.y * 0.5f) + block.Vector2("alert_offset")
            };
        }

        // DD1's alerts hang over the whole town, each on its building's place; BuildingAlerts says which show.
        private void BuildAlerts()
        {
            foreach (var id in BuildingIds)
            {
                if (!_buildings.TryGetValue(id, out var building)) continue;
                var alert = SpineView.Create("Alert." + id, _town, AlertSprite);
                if (alert == null) return;      // an install without the sprite: no alerts
                alert.Rect.PlaceTopLeft(building.AlertAt, new Vector2(0.5f, 0.5f), Vector2.zero);
                alert.Play(AlertAnimation);
                building.Alert = alert.gameObject;
                building.Alert.SetActive(false);
            }
        }

        /// <summary>Folder and file stem of the skeleton for a state: fx/&lt;stem&gt;/&lt;stem&gt;.sprite.{atlas,png,skel}.</summary>
        private static string Variant(string id, int level, bool locked)
        {
            var candidates = new[]
            {
                locked ? "town_" + id + "_locked" : null,       // stage_coach, statue and graveyard have none
                "town_" + id + "_level0" + Mathf.Clamp(level, 1, 3),
                "town_" + id + "_level01",
                "town_" + id                                     // the graveyard has a single skeleton
            };
            foreach (var stem in candidates)
                if (stem != null && Dd1Install.Exists("fx/" + stem + "/" + stem + ".sprite.skel")) return stem;
            return null;
        }

        /// <summary>(Re)creates a building's images for its current state.</summary>
        private void Refresh(string id)
        {
            if (!_buildings.TryGetValue(id, out var building)) return;
            States.TryGetValue(id, out var state);
            var stem = Variant(id, state.Level, state.Locked);
            if (stem == null)
            {
                Plugin.Log.LogWarning("Hamlet: no DD1 art for building " + id);
                return;
            }
            if (stem == building.Stem) return;

            var files = "fx/" + stem + "/" + stem + ".sprite";
            var skeleton = SpineSkeleton.Load(files + ".skel");
            var atlas = SpineAtlas.Load(files + ".atlas", true);
            if (skeleton == null || atlas == null) return;

            if (_hovered == id) Hover(id, false);
            for (int i = building.Root.childCount - 1; i >= 0; i--)
            {
                var old = building.Root.GetChild(i).gameObject;
                old.SetActive(false);
                Destroy(old);
            }
            building.Active = null;

            // Lights and smoke: everything of the skeleton but the building's own two pictures, as one sprite that
            // plays "idle". A skeleton whose "idle" has no length (the boarded-up ones, the statue) stands still.
            var moving = skeleton.Duration(AnimationIdle) > 0f ? SpineView.Create("Moving", building.Root, files) : null;
            if (moving != null)
            {
                moving.Leaves = slot => slot == SlotActive || slot == SlotIdle;
                moving.Play(AnimationIdle);
            }

            var half = new Vector2(0.5f, 0.5f);
            foreach (var part in skeleton.Parts)
            {
                if (moving != null && part.Slot != SlotActive && part.Slot != SlotIdle) continue;
                var region = atlas.Find(part.Region);
                if (region == null || region.Sprite == null) continue;
                // The attachment is sized for the region's original bounds; a whitespace-stripped region
                // covers only part of them (libgdx offsets are from the bottom-left).
                float sx = part.Width / region.OriginalWidth, sy = part.Height / region.OriginalHeight;
                var trim = new Vector3((region.OffsetX + (region.Width - region.OriginalWidth) * 0.5f) * sx,
                                       (region.OffsetY + (region.Height - region.OriginalHeight) * 0.5f) * sy, 0f);
                var rotation = Quaternion.Euler(0f, 0f, part.Rotation);

                var image = UiKit.Image(part.Slot, building.Root, region.Sprite, part.Color);
                var rt = (RectTransform)image.transform;
                rt.Place(half, half, new Vector2(part.X, part.Y) + (Vector2)(rotation * trim), new Vector2(region.Width * sx, region.Height * sy));
                rt.localRotation = rotation;

                if (part.Slot == SlotActive)
                {
                    building.Active = image.gameObject;
                    image.gameObject.SetActive(false);
                }
                else if (part.Slot == SlotIdle)
                {
                    image.raycastTarget = true;
                    var hit = image.gameObject.AddComponent<HamletBuildingHit>();
                    hit.Scene = this;
                    hit.Id = id;
                    hit.Mask = region.Mask;
                }
            }
            // every skeleton draws its two pictures first and what moves over them
            if (moving != null) moving.transform.SetAsLastSibling();

            var previous = building.Atlas;
            building.Stem = stem;
            building.Atlas = files + ".atlas";
            if (previous != null) SpineAtlas.Unload(previous);
        }

        private void BuildNamePlate(DarkestFile.Block fade)
        {
            if (fade != null)
            {
                _plateFadeSeconds = Mathf.Max(0.01f, fade.Float("seconds_time", 0, _plateFadeSeconds));
                // DD1's stock easing here is "easeInQuad": slow at first; any other name fades evenly
                _plateEasesIn = string.Equals(fade.String("easing_function", 0, "easeInQuad"), "easeInQuad", StringComparison.OrdinalIgnoreCase);
            }
            var blot = Dd1Install.Sprite(TownDir + "buildings/blg_name_background.png");
            var size = blot != null ? blot.rect.size : new Vector2(208f, 224f);
            _plate = UiKit.Rect("NamePlate", _town).PlaceTopLeft(Vector2.zero, new Vector2(0f, 1f), size);
            _plateGroup = _plate.gameObject.AddComponent<CanvasGroup>();
            _plateGroup.alpha = 0f;
            _plateGroup.interactable = false;
            _plateGroup.blocksRaycasts = false;
            if (blot != null) ((RectTransform)UiKit.Image("Blot", _plate, blot).transform).Stretch();

            // building_text_layout: the blot's corner lies background_offset from the text's place, the name
            // at name_offset and DD1's line about the building (str_<id>_summary) at description_offset. A
            // building's text_offset counts from the middle of its hover box, so both lines are centred on
            // that point; they run past the blot, most names being wider than it.
            var text = _layout.Find("building_text_layout");
            var origin = text != null && text.Has("background_offset") ? -text.Vector2("background_offset") : new Vector2(70f, 70f);
            var nameAt = origin + (text != null && text.Has("name_offset") ? text.Vector2("name_offset") : Vector2.zero);
            var summaryAt = origin + (text != null && text.Has("description_offset") ? text.Vector2("description_offset") : new Vector2(0f, 50f));
            _plateLabel = PlateLine("Name", "town_screen_building_name", nameAt, 64f);
            _plateSummary = PlateLine("Summary", "town_screen_building_description", summaryAt, 30f);
        }

        // One centred line of the name plate, its top at `at` (plate pixels); it is never cut.
        private TextMeshProUGUI PlateLine(string name, string style, Vector2 at, float height)
        {
            const float width = 600f;
            var label = UiKit.Text(name, _plate, "", style, null, TextAlignmentOptions.Top);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            ((RectTransform)label.transform).PlaceTopLeft(new Vector2(at.x - width * 0.5f, at.y), new Vector2(0f, 1f), new Vector2(width, height));
            return label;
        }

        /// <summary>What DD1 writes under a building's name on the town screen ("Recruit New Heroes"); empty when DD1 has no such line.</summary>
        private static string Summary(string id)
        {
            if (BuildingLocks.Locked(id)) return BuildingLocks.Summary;
            return Dd1Strings.Plain(Dd1Strings.Get("str_" + id + "_summary"));
        }

        internal void Hover(string id, bool on)
        {
            if (!_buildings.TryGetValue(id, out var building)) return;
            if (on)
            {
                if (_hovered != null && _hovered != id) Hover(_hovered, false);
                if (_hovered != id) EstateAudio.Ui(HoverSound);
                _hovered = id;
                if (building.Active != null) building.Active.SetActive(true);
                _plate.PlaceTopLeft(building.Plate, new Vector2(0f, 1f), _plate.sizeDelta);
                _plateLabel.text = ActivityText.Building(id);
                _plateSummary.text = Summary(id);
                FadePlate(1f);
            }
            else if (_hovered == id)
            {
                _hovered = null;
                if (building.Active != null) building.Active.SetActive(false);
                FadePlate(0f);
            }
        }

        private void FadePlate(float target)
        {
            if (_plateGroup == null || (Mathf.Approximately(target, _plateTarget) && _plateShare < 1f)) return;
            _plateFrom = _plateGroup.alpha;
            _plateTarget = target;
            _plateShare = Mathf.Approximately(_plateFrom, target) ? 1f : 0f;
        }

        internal void Click(string id)
        {
            // DD1: a building still boarded up answers a click with its own sound and nothing else.
            if (BuildingLocks.Locked(id))
            {
                EstateAudio.Ui(LockedSound);
                Plugin.Log.LogInfo("Hamlet: the " + id + " is not open yet");
                return;
            }
            Enter(id);
        }

        /// <summary>
        /// A building entered: by a click on it in the town, or by another way into it (the quick navigation
        /// beside a building's screen). Whoever listens for the click hears of it all the same: the building's
        /// screen opens, its door sounds, the Ancestor has his word on it.
        /// </summary>
        public static void Enter(string id)
        {
            try { BuildingClicked?.Invoke(id); }
            catch (Exception e) { Plugin.Log.LogError("Hamlet: BuildingClicked handler failed: " + e); }
        }

        private void Update()
        {
            // The canvas keeps 1080 units of height; on screens narrower than 16:9 (16:10, Steam Deck) that
            // would crop the cliff and the tree, so the town shrinks to fit the width instead.
            float fit = Mathf.Min(1f, ((RectTransform)_town.parent).rect.width / Width);
            if (fit > 0f && !Mathf.Approximately(fit, _town.localScale.x)) _town.localScale = new Vector3(fit, fit, 1f);

            float dt = Time.unscaledDeltaTime;
            foreach (var layer in _sky)
            {
                if (layer.Speed == 0f) continue;
                layer.Offset = Mathf.Repeat(layer.Offset + layer.Speed * dt, layer.TextureWidth);
                layer.Image.uvRect = new Rect(-layer.Offset / layer.TextureWidth, 0f, Width / layer.TextureWidth, 1f);
            }
            if (_plateGroup != null && _plateShare < 1f)
            {
                _plateShare = Mathf.Min(1f, _plateShare + dt / _plateFadeSeconds);
                _plateGroup.alpha = Mathf.Lerp(_plateFrom, _plateTarget, _plateEasesIn ? _plateShare * _plateShare : _plateShare);
            }

            if (Time.unscaledTime >= _nextAlerts)
            {
                _nextAlerts = Time.unscaledTime + AlertSeconds;
                try
                {
                    foreach (var building in _buildings.Values)
                    {
                        if (building.Alert == null) continue;
                        var on = BuildingAlerts.On(building.Id);
                        if (building.Alert.activeSelf != on) building.Alert.SetActive(on);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Hamlet: the buildings' alerts failed: " + e);
                    foreach (var building in _buildings.Values)
                    {
                        if (building.Alert != null) Destroy(building.Alert);
                        building.Alert = null;
                    }
                }
            }
        }

        // The screen is hidden by deactivating it; no pointer-exit arrives for the building under the cursor.
        private void OnDisable()
        {
            if (_hovered != null) Hover(_hovered, false);
            if (_plateGroup != null) _plateGroup.alpha = 0f;
            _plateTarget = 0f;
            _plateShare = 1f;
        }

        private void OnDestroy()
        {
            if (_current == this) _current = null;
            if (_groundTexture != null) Destroy(_groundTexture);
        }
    }
}
