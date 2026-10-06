using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// DD1's light (docs/recon/dd1-corridor-rendering.md, section 5), term by term, each with a switch for
    /// A/B screenshots (dev bridge: corridor.light):
    /// <list type="bullet">
    /// <item>walls, floor, far layers and props (shaders/lit_sprite.glsl): a colour ramp EdgeLight -> HalfLight ->
    /// BaseLight over the distance from the camera's axis, in vertex colours (<see cref="Ramp"/>);</item>
    /// <item>heroes (shaders/character.glsl): a box of full light around the camera's axis that falls off outside it,
    /// one value per hero, given to the DD2 model as its _Brightness (<see cref="Hero"/>);</item>
    /// <item>the torch: blends the three colours between colours/base.colours.darkest's lighting_none_* and
    /// lighting_full_* (the same colours in the shipped file) and picks the colour grade (<see cref="CorridorPost"/>);</item>
    /// <item>flicker: DD1's random walk on the torch value (nothing to see with the shipped colours);</item>
    /// <item>the scene fade: DD1's light multiplier for door transitions, here a black curtain over the view.</item>
    /// </list>
    /// </summary>
    internal static class CorridorLight
    {
        // ---- switches and tunables (static: set through the bridge) ----------------------------------------
        /// <summary>The ramp on walls, floor, far layers and props. Off: the art as it is.</summary>
        public static bool Enabled = true;
        /// <summary>The box light on the heroes. Off: every hero at <see cref="HeroBrightness"/>.</summary>
        public static bool HeroLight = true;
        /// <summary>Dev bridge: a torch level 0..100 instead of the expedition's; below 0: the expedition's.</summary>
        public static float TorchOverride = -1f;
        /// <summary>DD1's scene fade (1 = lit, 0 = black): the door transition runs it down and up again.</summary>
        public static float Fade = 1f;
        /// <summary>
        /// The mod's own: DD1 multiplies its light into the art's stored (gamma) values; the game renders in linear
        /// space, where the vertex colour multiplies the decoded texture. The exact equivalent depends on how dark
        /// the art is (sRGB is not a pure power near black); DD1's dungeon art is dark, and measured on screen
        /// (lit against unlit frame) a multiplier m^2.2 came out as m^1.52. m^1.45 gives m.
        /// </summary>
        public static float Gamma = 1.45f;
        /// <summary>The same for a hero's _Brightness (their textures are brighter: nearer the plain 2.2).</summary>
        public static float HeroGamma = 2.0f;
        /// <summary>The mod's own: a fully lit hero's _Brightness (the game's scenes use 0.8..1 with lights of their
        /// own; DD1's heroes are painted bright against the wall). 2.0 was chosen in game against 1.35, 1.8 and 2.3
        /// (_lab/shots/c3_hero_sheet.png): below it the dark-clothed heroes are hard to read on the lit wall.</summary>
        public static float HeroBrightness = 2.0f;
        /// <summary>The height above the floor at which a hero's light is taken (DD1 lights each pixel; the box is
        /// centred at 210): DD1 units.</summary>
        public static float HeroLightHeight = 170f;
        /// <summary>The mod's own: 0 is DD1 (its flicker changes nothing on screen); above 0 the scene dims by up to this
        /// share with the walk of the flicker value.</summary>
        public static float FlickerVisible = 0f;
        /// <summary>DD1's layers.B_intensity: the heroes' light while a timescript runs (1.4 while one investigates).</summary>
        public static float HeroBoost = 1f;
        public static bool FlickerOn = true;

        public static Color Base { get; private set; } = Color.white;
        public static Color Half { get; private set; } = Color.white;
        public static Color Edge { get; private set; } = Color.white;
        /// <summary>DD1's flicker value: a random walk inside flicker.range, a step of up to flicker.variance a frame.</summary>
        public static float Flicker { get; private set; } = 1.05f;

        private static float _flickerClock;

        /// <summary>The torch, 0..100.</summary>
        public static float Torch
        {
            get
            {
                if (TorchOverride >= 0f) return Mathf.Clamp(TorchOverride, 0f, 100f);
                var run = DungeonRun.Current;
                // (the expedition's light, with what a fight just over did to it: DungeonRun.LightInView)
                return run != null ? Mathf.Clamp((float)run.LightInView, 0f, 100f) : 100f;
            }
        }

        /// <summary>What is left of the light after the scene fade and the mod's visible flicker: 0..1, as DD1 would
        /// multiply it into what is on screen.</summary>
        public static float Curtain
        {
            get
            {
                var range = CorridorNumbers.FlickerRange;
                var dip = FlickerVisible > 0f && range.y > range.x ? FlickerVisible * (range.y - Flicker) / (range.y - range.x) : 0f;
                return Mathf.Clamp01(Fade) * (1f - Mathf.Clamp01(dip));
            }
        }

        /// <summary>Once a frame, before anything asks for the light.</summary>
        public static void Update(float deltaTime)
        {
            // DD1 steps its flicker once a frame; its frames are sixtieths of a second
            var range = CorridorNumbers.FlickerRange;
            if (!FlickerOn) Flicker = (range.x + range.y) * 0.5f;
            else
            {
                _flickerClock += deltaTime;
                while (_flickerClock >= 1f / 60f)
                {
                    _flickerClock -= 1f / 60f;
                    Flicker = Mathf.Clamp(Flicker + Random.Range(-CorridorNumbers.FlickerVariance, CorridorNumbers.FlickerVariance), range.x, range.y);
                }
            }
            // DD1: torch01 = torch / 100 * flicker blends "none" to "full" (unclamped, as DD1 leaves it)
            var t = Torch / 100f * Flicker;
            Base = Color.LerpUnclamped(CorridorNumbers.NoneBase, CorridorNumbers.FullBase, t);
            Half = Color.LerpUnclamped(CorridorNumbers.NoneHalf, CorridorNumbers.FullHalf, t);
            Edge = Color.LerpUnclamped(CorridorNumbers.NoneEdge, CorridorNumbers.FullEdge, t);
        }

        /// <summary>The ramp as DD1 computes it (a multiplier on the art's own colour as stored, 0..1 per channel).</summary>
        public static Color RampGamma(float viewX)
        {
            if (!Enabled) return Color.white;
            var d = Mathf.Clamp01(1f - Mathf.Abs(viewX) / CorridorNumbers.RampWidth);
            var c = d < 0.5f ? Color.Lerp(Edge, Half, d * 2f) : Color.Lerp(Half, Base, (d - 0.5f) * 2f);
            return new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
        }

        /// <summary>The ramp as a vertex colour: DD1's multiplier raised to <see cref="Gamma"/>.</summary>
        public static Color Ramp(float viewX)
        {
            var c = RampGamma(viewX);
            if (QualitySettings.activeColorSpace != ColorSpace.Linear) return c;
            return new Color(Mathf.Pow(c.r, Gamma), Mathf.Pow(c.g, Gamma), Mathf.Pow(c.b, Gamma), 1f);
        }

        /// <summary>
        /// character.glsl's light on a hero standing at x (DD1 units), as DD1 multiplies it into the sprite: full
        /// inside a box of falloff_start around the light's centre (the camera's axis moved by offset_from_centre),
        /// down to nothing falloff_distance further out; times the hero's own LightScalar (the walk into a door)
        /// and the wash (shade_character.wash: black at 15/255).
        /// </summary>
        public static float HeroGamma01(float heroX, Vector3 camera, float lightScalar)
        {
            if (!HeroLight) return 1f;
            var start = CorridorNumbers.FalloffStart;
            var reach = CorridorNumbers.FalloffDistance;
            var x = Mathf.Clamp(Mathf.Abs(heroX - (camera.x + CorridorNumbers.LightOffset.x)) - start.x, 0f, reach.x) / Mathf.Max(1f, reach.x);
            var y = Mathf.Clamp(Mathf.Abs(HeroLightHeight - (camera.y + CorridorNumbers.LightOffset.y)) - start.y, 0f, reach.y) / Mathf.Max(1f, reach.y);
            var light = Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y));
            var wash = 1f - CorridorNumbers.Wash.a * (1f - CorridorNumbers.Wash.grayscale);
            return Mathf.Clamp01(light * Mathf.Clamp01(lightScalar) * wash * Base.grayscale);
        }

        /// <summary>The _Brightness of a hero's model for that light.</summary>
        public static float Hero(float heroX, Vector3 camera, float lightScalar)
        {
            return HeroBrightness * Mathf.Pow(HeroGamma01(heroX, camera, lightScalar) * Mathf.Max(0f, HeroBoost), HeroGamma);
        }
    }
}
