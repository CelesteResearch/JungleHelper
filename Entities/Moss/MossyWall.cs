using System;
using Celeste.Formats.Map.Entities.Structure;
using System.Collections.Generic;
using System.Numerics;
using Celeste.Core.Components;
using Celeste.Core.Randomness;
using Celeste.Core.Rendering;
using Celeste.Core.Scheduling;
using Celeste.Everest.Modules.Entities.Metadata;
using Celeste.Game.Content.Maps.Entities;
using Celeste.Game.Content.Maps.Levels;
using Celeste.Game.Execution.Levels;
using Celeste.Graphics.Animation;
using Celeste.Graphics.Content;
using Celeste.Mods.JungleHelper.Modules;
using Celeste.Mods.JungleHelper.Runtime.Moss;
using Celeste.Physics.Collision;
using Celeste.Physics.Collision.Shapes;
using Celeste.Scene.Entities;
using Celeste.Scene.Tracking;
using Celeste.Simulation;
using Celeste.Simulation.Gameplay.Collision.Blockers;
using Celeste.Simulation.Gameplay.Platforms.Foundation;
using Celeste.Simulation.Gameplay.Platforms.StaticMovers;
using Celeste.Game.Gameplay.Platforms.Cassette;
using Color = Celeste.Core.Display.PackedColor;

namespace Celeste.Mods.JungleHelper.Entities.Moss
{
    [CustomEntity("JungleHelper/MossyWall = Load")]
    [Celeste.Core.Reflection.Runtime.RuntimeTypeAlias("Celeste.Mod.JungleHelper.Entities.MossyWall")]
    public sealed class MossyWall : SceneEntityState
    {
        private readonly ISourceEngineContext engine;
        private readonly JungleHelperModule module;

        public static MossyWall Load(OriginalLevelSceneRoot level, OriginalLevelRoomData room, Vector2 offset,
            OriginalLevelEntityData data)
        {
            foreach (var candidate in ((Celeste.Everest.Modules.Runtime.ISourceEverestModulesEnvironment)level
                         .EngineContext).Modules)
                if (candidate is JungleHelperModule owner)
                    return new MossyWall(level.EngineContext, owner, data, offset);
            throw new InvalidOperationException("JungleHelper is not loaded.");
        }

        private const int LanternActivationRadius = 75;

        // colors used when player gets close but moss isn't dissolved yet (from closest to furthest, 1 line per pixel).
        private static Color[] GetDistanceBasedColors(string color1, string color2, string color3)
        {
            return new Color[]
            {
                Color.FromHex(color1) * 0.1f,
                Color.FromHex(color1) * 0.2f,
                Color.FromHex(color1) * 0.3f,
                Color.FromHex(color1) * 0.4f,
                Color.FromHex(color1) * 0.5f,
                Color.FromHex(color1) * 0.6f,
                Color.FromHex(color1) * 0.7f,
                Color.FromHex(color1) * 0.8f,
                Color.FromHex(color1) * 0.9f,
                Color.FromHex(color1),
                Color.Lerp(Color.FromHex(color1), Color.FromHex(color2), 0.1f),
                Color.Lerp(Color.FromHex(color1), Color.FromHex(color2), 0.2f),
                Color.Lerp(Color.FromHex(color1), Color.FromHex(color2), 0.3f),
                Color.Lerp(Color.FromHex(color1), Color.FromHex(color2), 0.4f),
                Color.Lerp(Color.FromHex(color1), Color.FromHex(color2), 0.5f),
                Color.Lerp(Color.FromHex(color1), Color.FromHex(color2), 0.6f),
                Color.Lerp(Color.FromHex(color1), Color.FromHex(color2), 0.7f),
                Color.Lerp(Color.FromHex(color1), Color.FromHex(color2), 0.8f),
                Color.Lerp(Color.FromHex(color1), Color.FromHex(color2), 0.9f),
                Color.FromHex(color2),
                Color.Lerp(Color.FromHex(color2), Color.FromHex(color3), 0.1f),
                Color.Lerp(Color.FromHex(color2), Color.FromHex(color3), 0.2f),
                Color.Lerp(Color.FromHex(color2), Color.FromHex(color3), 0.3f),
                Color.Lerp(Color.FromHex(color2), Color.FromHex(color3), 0.4f),
                Color.Lerp(Color.FromHex(color2), Color.FromHex(color3), 0.5f),
                Color.Lerp(Color.FromHex(color2), Color.FromHex(color3), 0.6f),
                Color.Lerp(Color.FromHex(color2), Color.FromHex(color3), 0.7f),
                Color.Lerp(Color.FromHex(color2), Color.FromHex(color3), 0.8f),
                Color.Lerp(Color.FromHex(color2), Color.FromHex(color3), 0.9f),
                Color.FromHex(color3)
            };
        }

        private readonly SpriteImage[] mossParts;
        private readonly ColliderGeometry[] hitboxes;
        private readonly bool[] hitboxToggles;

        private Vector2 topCenter;
        private Vector2 shake;
        private Color[] distanceColors;

        public MossyWall(ISourceEngineContext engine, JungleHelperModule module, EntityData data, Vector2 offset) :
            base(((ISourceEntitySourceDataEnvironment)engine).EntitySourceDataRegistration, data.Position + offset)
        {
            this.engine = engine;
            this.module = module;
            bool left = data.Bool("left");

            distanceColors = GetDistanceBasedColors(
                data.Attr("color1", "7A612D"),
                data.Attr("color2", "AABF3D"),
                data.Attr("color3", "33C111")
            );

            Depth = -20000; // FG tiles have depth -10000

            Add(new SourceStaticMoverState
            {
                SolidChecker = solid => CheckAttachToSolid(solid, left),
                OnMove = move =>
                {
                    Position += move;
                    topCenter += move;
                },
                OnShake = move => shake += move,
                OnDisable = () => Collidable = false,
                OnEnable = () => Collidable = true
            });
            Add(new ClimbBlockerState(edge: false));

            List<SpriteImage> mossParts = new List<SpriteImage>();
            List<ColliderGeometry> hitboxes = new List<ColliderGeometry>();
            List<bool> hitboxToggles = new List<bool>();

            var random = ((ISourceRandomProcessEnvironment)engine).RandomProcess;
            random.PushRandom();
            for (int i = 0; i < data.Height; i += 8)
            {
                string id = (i == 0)
                    ? "moss_top"
                    : (i + 16 <= data.Height ? "moss_mid" + random.Random.Next(1, 3) : "moss_bottom");
                SpriteImage sprite =
                    new SpriteImage(
                        ((ISourceGameGraphicsEnvironment)engine).GameGraphics.GameAtlas[
                            data.Attr("spriteDirectory", "JungleHelper/Moss") + "/" + id]);
                sprite.Position = new Vector2(0f, i);
                sprite.FlipX = !left;
                Add(sprite);

                mossParts.Add(sprite);

                HitboxColliderGeometry hitbox;
                if (left)
                {
                    hitbox = new HitboxColliderGeometry(2f, 8f, 8f, i);
                }
                else
                {
                    hitbox = new HitboxColliderGeometry(2f, 8f, -2f, i);
                }

                hitboxes.Add(hitbox);
                hitboxToggles.Add(true);
            }

            random.PopRandom();

            this.mossParts = mossParts.ToArray();
            this.hitboxes = hitboxes.ToArray();
            this.hitboxToggles = hitboxToggles.ToArray();

            Collider = new ColliderListGeometry(this.hitboxes);
            topCenter = TopCenter;
        }

        private bool CheckAttachToSolid(SourceSolidEntityState solid, bool left)
        {
            bool collides = SourceEntityCollision.CheckAt(this, solid, Position + (left ? -2 : 2) * Vector2.UnitX);
            if (collides && solid is CassetteBlockState cassetteBlock)
            {
                // check the color of the cassette block we're attached to, to apply it to the moss.
                Color mossColor;
                switch (cassetteBlock.Index)
                {
                    case 0:
                    default:
                        mossColor = Color.FromHex("377FB2");
                        break;
                    case 1:
                        mossColor = Color.FromHex("B2378F");
                        break;
                    case 2:
                        mossColor = Color.FromHex("B29B2A");
                        break;
                    case 3:
                        mossColor = Color.FromHex("269933");
                        break;
                }

                // the colors when the lantern gets close are a fade out from 1 to 0.1 alpha before the moss disappears (no color shifts like regular moss).
                distanceColors = new Color[10];
                for (int i = 0; i < 10; i++)
                {
                    distanceColors[i] = mossColor * ((i + 1) * 0.1f);
                }

                // make the moss update first so that it doesn't "blink" when Maddy grabs the lantern:
                // the cassette block will fix the depth when it gets updated anyway...
                Add(new MossPreUpdateHook(() =>
                {
                    Depth = -20000;
                    ((ISourceScene)Scene).SourceScene.Entities.UpdateLists();
                }));

                // ... then, when the cassette block is done setting the depth,
                // make sure the moss appears on top of it (because the cassette block moves it just behind).
                Add(new ScenePostUpdateHook(() =>
                {
                    Depth -= 2;
                    ((ISourceScene)Scene).SourceScene.Entities.UpdateLists();
                }));
            }

            return collides;
        }

        public override void Awake(object scene)
        {
            base.Awake(scene);
            UpdateLanternFade();
        }

        public override void Update()
        {
            base.Update();
            UpdateLanternFade();
        }

        private void UpdateLanternFade()
        {
            bool shouldUpdate = false;

            for (int i = 0; i < mossParts.Length; i++)
            {
                SpriteImage mossPart = mossParts[i];
                bool shouldBeCollidable = false;
                float distance =
                    module.LanternLighting.GetClosestDistanceTo(topCenter + mossPart.Position, Scene, out _);

                if (distance < LanternActivationRadius)
                {
                    // moss is dissolved, make it uncollidable.
                    mossPart.Visible = false;
                }
                else if (distance - LanternActivationRadius < distanceColors.Length)
                {
                    // moss has a particular fade of color.
                    mossPart.Visible = true;
                    mossPart.Color = distanceColors[(int)Math.Floor(distance - LanternActivationRadius)];

                    // if moss is more than 50% visible, it should be collidable.
                    if (Math.Floor(distance - LanternActivationRadius) >= 5)
                    {
                        shouldBeCollidable = true;
                    }
                }
                else
                {
                    // moss is green (player is too far or not here), make it collidable.
                    mossPart.Visible = true;
                    mossPart.Color = distanceColors[distanceColors.Length - 1];
                    shouldBeCollidable = true;
                }

                if (!Collidable)
                {
                    // moss is deactivated (when attached to a cassette block), make it darker.
                    mossPart.Color.R /= 4;
                    mossPart.Color.G /= 4;
                    mossPart.Color.B /= 4;
                }

                if (shouldBeCollidable != hitboxToggles[i])
                {
                    // that hitbox needs to be toggled!
                    hitboxToggles[i] = shouldBeCollidable;
                    shouldUpdate = true;
                }
            }

            // skip the collider rebuilding if nothing changed, this is just going to waste the GC's time
            if (!shouldUpdate) return;

            List<ColliderGeometry> enabledHitboxes = new List<ColliderGeometry>();
            for (int i = 0; i < mossParts.Length; i++)
            {
                if (hitboxToggles[i]) enabledHitboxes.Add(hitboxes[i]);
            }

            if (enabledHitboxes.Count == 0)
            {
                Collider = null;
            }
            else
            {
                Collider = new ColliderListGeometry(enabledHitboxes.ToArray());
            }
        }

        public override void Render(ISourceRenderContext context)
        {
            Vector2 position = Position;
            Position += shake;
            base.Render(context);
            Position = position;
        }
    }
}
