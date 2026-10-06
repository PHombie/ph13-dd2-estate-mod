using System;
using System.Collections.Generic;
using System.Globalization;
using DD2Estate.Core;
using DD2Estate.Dd1;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// The numbers DD1 draws a hallway and a room from (docs/recon/dd1-corridor-rendering.md), read once from
    /// the player's DD1 install. Every value written here is a FALLBACK for an install that lacks the file:
    /// it is what DD1 ships. Lengths are DD1's units: one unit is one pixel of its art, x along the hallway,
    /// y up from the floor, z away from the camera, the party on z = 0. All fields are static so the dev
    /// bridge can set them (then corridor.rebuild).
    /// </summary>
    internal static class CorridorNumbers
    {
        // ---- the mod's own -------------------------------------------------------------------------------
        /// <summary>World units of the game to a DD1 unit. The mod's own: with it a DD2 Crusader (1.745 units from
        /// sole to crest) stands as tall as DD1's (292 of its units), so the models keep their own scale.</summary>
        public static float UnitScale = 0.006f;

        // ---- scripts/layout/screen.raid.darkest: area, fade_controls, door_transition ----------------------
        public static float TileWidth = 720f, ActorSpacing = 154f, RoomPosition = 600f;
        public static float FadeOutTime = 0.7f, FadeInTime = 0.7f, ForegroundInTime = 0.4f, ForegroundOutTime = 0.2f;
        public static float DoorCameraBlendTime = 3f, DoorCameraStart = 650f, DoorVisibility = 50f, DoorDarken = 200f, DoorWait = 0.01f;
        // Measured in the real DD1 (docs/recon/dd1-reference-frames.md, section 2: 30 fps recordings of both doors of
        // the Old Road's hallway). The camera flies at a point on the door, this far above the floor on the wall's
        // plane; holding "forward" at a hallway's end takes the leader this far past the last door's middle and no
        // further (a door is used, not walked through); the bars and brackets over the heroes and the quest's
        // words are gone from the moment a door is used until this long after the next area stands in full light.
        // (The height is half the door prop's own height, CorridorView.DoorMiddleHeight; this one stands in for a
        // door without art.)
        public static float DoorCameraHeight = 172f, DoorOvershoot = 48f, OverlayReturn = 0.15f;
        // Measured in the real DD1 with a party of four (the same note, 2.5): W uses a door while the leader stands
        // in the door's own tile (282 from the first door's middle, where the party arrives: used at once; 251
        // before the last door: used; 360 and more before it: nothing happens), and every hero then walks straight
        // from where it stands to the door's middle on the wall, all at once. The scene's fade proper starts a
        // quarter of a second after the walk of the farthest hero would end at 400 a second (four recordings:
        // 0.25 + the straight way / 400): DoorSettle, on top of the file's post_party_at_door_wait.
        public static float DoorReach => TileWidth * 0.5f;
        // Measured there too: the heroes stand actor_spacing (154) apart as they arrive in an area; walking forward
        // the line draws out to 166..168, walking backwards it closes to 143, and it stays as the walk left it.
        // GUESS: how fast it does so (the recordings show it done within the first steps).
        public static float SpacingForward = 167f, SpacingBackward = 143f, SpacingRate = 40f;
        public static float DoorSettle = 0.25f;

        // ---- scripts/camera.darkest ----------------------------------------------------------------------
        public static Vector3 Offset = new Vector3(80f, 270f, -1400f);
        public static Vector3 BackOffset = new Vector3(-50f, 275f, -675f);
        public static Vector3 BattleOffset = new Vector3(180f, 280f, -1240f);
        public static float Zoom = 1.25f, ZoomBack = 0.8f, TransitionTime = 0.5f;
        public static float HorizontalFov = 75f, ViewAspect = 2.666666f;
        public static Vector3 RoomCamera = new Vector3(960f, 300f, -1240f);

        // ---- scripts/world.darkest -----------------------------------------------------------------------
        public static float WallZ = 400f, ForegroundZ = -300f, ForegroundTopY = 0f, ForegroundBottomY = -10f, MidZ = 1000f, FarZ = 1500f;
        public static float CurioX = 135f, CurioRoomX = 75f, CurioZ = 75f, TrapX = 0f, TrapZ = 15f, ObstacleX = 75f;

        // ---- shared/rules.json ---------------------------------------------------------------------------
        public static float ForwardAcceleration = 1600f, MaxForwardSpeed = 400f, ReverseAcceleration = 800f, MaxReverseSpeed = 200f, Deceleration = 1600f;
        public static float InteractionWidth = 470f;
        /// <summary>
        /// How far before a prop the party's front may be to use it: DD1's executable (RaidDisplay::CanInteractWithTrap)
        /// asks that the stretch from the party's place to its leader overlap the prop's area, which is
        /// m_InteractionAreaPixelWidth wide and (read so) centred on the prop.
        /// </summary>
        public static float PropReach => InteractionWidth * 0.5f;

        // ---- colours/base.colours.darkest: lighting_none_* (torch out) and lighting_full_* ---------------
        public static Color NoneBase = Color.white, NoneHalf = Grey(200), NoneEdge = Grey(75);
        public static Color FullBase = Color.white, FullHalf = Grey(200), FullEdge = Grey(75);

        // ---- scripts/raid.lighting.darkest ---------------------------------------------------------------
        public static Vector2 LightOffset = new Vector2(0f, -60f), FalloffStart = new Vector2(450f, 140f), FalloffDistance = new Vector2(600f, 250f);
        public static Color Wash = new Color(0f, 0f, 0f, 15f / 255f);
        public static float TorchMin = 0.4f, Grain = 0.1f, FlickerVariance = 0.05f;
        public static Vector2 FlickerRange = new Vector2(1f, 1.1f);

        // ---- constants of DD1's executable (in no file; the spec gives the function each is read from) -----
        /// <summary>Hero i stands at party x + 180 - i * spacing: the leader is this far ahead of the point DD1
        /// counts tiles from and calls the party's position.</summary>
        public static float LeaderAhead = 180f;
        /// <summary>The wall art's upper 600 rows stand upright, from the floor to this height.</summary>
        public static float WallTop = 600f;
        /// <summary>Its lower 120 rows lie flat as the floor, from the wall to this depth (towards the camera).</summary>
        public static float FloorNearZ = -600f;
        /// <summary>The far background's and the midground's texture moves this share of the camera's way.</summary>
        public static float FarFollow = 0.5f, MidFollow = 0.25f;
        /// <summary>lit_sprite.glsl: the ramp ends this far from the camera's axis, in view space.</summary>
        public static float RampWidth = 960f;
        /// <summary>The walking-back blend: seconds of backing to reach it, and how much faster it goes away.</summary>
        public static float BackTime = 2f, BackRecovery = 1.5f;

        public static bool Loaded { get; private set; }

        /// <summary>Pixels of DD1's 1920-wide view to one unit at one unit's distance: half the view over tan(half the field of view).</summary>
        public static float Focal => 960f / Mathf.Tan(HorizontalFov * 0.5f * Mathf.Deg2Rad);

        /// <summary>How wide DD1 draws a room's picture: what the room camera sees at the wall's depth.</summary>
        public static float RoomWallWidth => 2f * Mathf.Tan(HorizontalFov * 0.5f * Mathf.Deg2Rad) * (WallZ - RoomCamera.z);

        /// <summary>The vertical field of view of a camera whose picture fills DD1's 1920x720 view, magnified by zoom.</summary>
        public static float VerticalFov(float zoom)
        {
            return 2f * Mathf.Atan(Mathf.Tan(HorizontalFov * 0.5f * Mathf.Deg2Rad) / ViewAspect / Mathf.Max(0.05f, zoom)) * Mathf.Rad2Deg;
        }

        private static Color Grey(int value) => new Color(value / 255f, value / 255f, value / 255f, 1f);

        public static void Load(bool again = false)
        {
            if (Loaded && !again) return;
            Loaded = true;
            try
            {
                Read();
            }
            catch (Exception e)
            {
                // the fallbacks above are DD1's own numbers: the view works without the files
                Plugin.Log.LogWarning("Corridor: DD1's numbers could not be read, its shipped values are used: " + e.Message);
            }
        }

        private static void Read()
        {
            var layout = Blocks("scripts/layout/screen.raid.darkest");
            if (layout.TryGetValue("area", out var area))
            {
                TileWidth = F(area, "tile_width", TileWidth);
                ActorSpacing = F(area, "actor_spacing", ActorSpacing);
                RoomPosition = F(area, "room_position", RoomPosition);
            }
            if (layout.TryGetValue("fade_controls", out var fade))
            {
                FadeOutTime = F(fade, "fade_out_time", FadeOutTime);
                FadeInTime = F(fade, "fade_in_time", FadeInTime);
                ForegroundInTime = F(fade, "foreground_in_time", ForegroundInTime);
                ForegroundOutTime = F(fade, "foreground_out_time", ForegroundOutTime);
            }
            if (layout.TryGetValue("door_transition", out var door))
            {
                DoorCameraBlendTime = F(door, "camera_position_blend_time", DoorCameraBlendTime);
                DoorCameraStart = F(door, "party_door_distance_camera_start", DoorCameraStart);
                DoorVisibility = F(door, "visibility_distance_to_door", DoorVisibility);
                DoorDarken = F(door, "party_darken_distance", DoorDarken);
                DoorWait = F(door, "post_party_at_door_wait", DoorWait);
            }

            var camera = Blocks("scripts/camera.darkest");
            if (camera.TryGetValue("m_CorridorCameraParameters", out var corridor))
            {
                Offset = V3(corridor, "OffsetFromPartyLeader", Offset);
                BackOffset = V3(corridor, "OffsetFromPartyLeaderWalkingBack", BackOffset);
                BattleOffset = V3(corridor, "BattleOffsetFromPartyLeader", BattleOffset);
                Zoom = F(corridor, "Zoom", Zoom);
                ZoomBack = F(corridor, "ZoomWalkingBack", ZoomBack);
                TransitionTime = F(corridor, "TransitionTime", TransitionTime);
            }
            if (camera.TryGetValue("m_CameraParameters", out var lens))
            {
                HorizontalFov = F(lens, "RegularHorizontalFOV", HorizontalFov);
                ViewAspect = F(lens, "AspectRatio", ViewAspect);
            }
            if (camera.TryGetValue("m_RoomCameraParameters", out var room)) RoomCamera = V3(room, "Position", RoomCamera);

            if (Blocks("scripts/world.darkest").TryGetValue("world_parameters", out var world))
            {
                WallZ = F(world, "distance_to_interior_background", WallZ);
                ForegroundZ = F(world, "distance_to_interior_foreground", ForegroundZ);
                ForegroundTopY = F(world, "foreground_top_y_offset", ForegroundTopY);
                ForegroundBottomY = F(world, "foreground_bottom_y_offset", ForegroundBottomY);
                MidZ = F(world, "distance_to_midground", MidZ);
                FarZ = F(world, "distance_to_farbackground", FarZ);
                CurioX = F(world, "curio_tile_centre_x_offset", CurioX);
                CurioRoomX = F(world, "curio_tile_centre_x_offset_room", CurioRoomX);
                CurioZ = F(world, "curio_z_position", CurioZ);
                TrapX = F(world, "trap_tile_centre_x_offset", TrapX);
                TrapZ = F(world, "trap_z_position", TrapZ);
                ObstacleX = F(world, "obstacle_tile_centre_x_offset", ObstacleX);
            }

            var rules = Json.ParseFile(Dd1Install.ReadText("shared/rules.json"));
            if (rules != null)
            {
                ForwardAcceleration = (float?)rules["m_ForwardAcceleration"] ?? ForwardAcceleration;
                MaxForwardSpeed = (float?)rules["m_MaxForwardSpeed"] ?? MaxForwardSpeed;
                ReverseAcceleration = (float?)rules["m_ReverseAcceleration"] ?? ReverseAcceleration;
                MaxReverseSpeed = (float?)rules["m_MaxReverseSpeed"] ?? MaxReverseSpeed;
                Deceleration = (float?)rules["m_Deceleration"] ?? Deceleration;
                InteractionWidth = (float?)rules["m_InteractionAreaPixelWidth"] ?? InteractionWidth;
            }

            foreach (var block in DD2Estate.Core.DarkestFile.Parse(Dd1Install.ReadText("colours/base.colours.darkest")))
            {
                if (block.Name != "colour") continue;
                switch (block.Text("id"))
                {
                    case "lighting_none_base": NoneBase = Rgb(block, "rgba", NoneBase); break;
                    case "lighting_none_half": NoneHalf = Rgb(block, "rgba", NoneHalf); break;
                    case "lighting_none_edge": NoneEdge = Rgb(block, "rgba", NoneEdge); break;
                    case "lighting_full_base": FullBase = Rgb(block, "rgba", FullBase); break;
                    case "lighting_full_half": FullHalf = Rgb(block, "rgba", FullHalf); break;
                    case "lighting_full_edge": FullEdge = Rgb(block, "rgba", FullEdge); break;
                }
            }

            var lighting = Blocks("scripts/raid.lighting.darkest");
            if (lighting.TryGetValue("shade_character", out var shade))
            {
                Wash = Rgb(shade, "wash", Wash);
                TorchMin = F(shade, "torch_min", TorchMin);
            }
            if (lighting.TryGetValue("grain", out var grain)) Grain = F(grain, "intensity", Grain);
            if (lighting.TryGetValue("flicker", out var flicker))
            {
                FlickerVariance = F(flicker, "variance", FlickerVariance);
                FlickerRange = V2(flicker, "range", FlickerRange);
            }
            if (lighting.TryGetValue("light_parameters", out var light))
            {
                LightOffset = V2(light, "offset_from_centre", LightOffset);
                FalloffStart = V2(light, "falloff_start", FalloffStart);
                FalloffDistance = V2(light, "falloff_distance", FalloffDistance);
            }
        }

        // The first block of each name (a .darkest file names a block once).
        private static Dictionary<string, DarkestBlock> Blocks(string file)
        {
            var blocks = new Dictionary<string, DarkestBlock>();
            foreach (var block in DD2Estate.Core.DarkestFile.Parse(Dd1Install.ReadText(file)))
                if (block.Name != null && !blocks.ContainsKey(block.Name)) blocks[block.Name] = block;
            return blocks;
        }

        private static float F(DarkestBlock block, string field, float fallback) => (float)block.Number(field, 0, fallback);

        private static Vector2 V2(DarkestBlock block, string field, Vector2 fallback)
        {
            return block.All(field).Count >= 2 ? new Vector2((float)block.Number(field, 0, fallback.x), (float)block.Number(field, 1, fallback.y)) : fallback;
        }

        private static Vector3 V3(DarkestBlock block, string field, Vector3 fallback)
        {
            return block.All(field).Count >= 3
                ? new Vector3((float)block.Number(field, 0, fallback.x), (float)block.Number(field, 1, fallback.y), (float)block.Number(field, 2, fallback.z))
                : fallback;
        }

        // "255 255 255 255", "0 0 0 15" or "#646464"
        private static Color Rgb(DarkestBlock block, string field, Color fallback)
        {
            var values = block.All(field);
            if (values.Count == 0) return fallback;
            if (values[0].StartsWith("#"))
            {
                var hex = values[0].Substring(1);
                if (hex.Length == 3) hex = "" + hex[0] + hex[0] + hex[1] + hex[1] + hex[2] + hex[2];
                if (hex.Length < 6) return fallback;
                var parts = new float[4] { 1f, 1f, 1f, 1f };
                for (var i = 0; i < 4 && i * 2 + 1 < hex.Length; i++)
                {
                    if (!int.TryParse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var part)) return fallback;
                    parts[i] = part / 255f;
                }
                return new Color(parts[0], parts[1], parts[2], parts[3]);
            }
            if (values.Count < 3) return fallback;
            return new Color((float)block.Number(field, 0, 255) / 255f, (float)block.Number(field, 1, 255) / 255f, (float)block.Number(field, 2, 255) / 255f,
                values.Count > 3 ? (float)block.Number(field, 3, 255) / 255f : 1f);
        }
    }
}
