using System.Numerics;
using Celeste.Core.Display;
using Celeste.Core.Geometry;
using Celeste.Core.Mathematics;
using Celeste.Core.Randomness;
using Celeste.Core.Reflection.Runtime;
using Celeste.Core.Rendering;
using Celeste.Core.Scheduling;
using Celeste.Everest.Modules.Entities.Metadata;
using Celeste.Formats.Map.Entities.Structure;
using Celeste.Game.Content.Maps.Entities;
using Celeste.Game.Execution.Levels;
using Celeste.Game.Gameplay.Decoration.MoonCreatures;
using Celeste.Graphics.Animation;
using Celeste.Mods.JungleHelper.Modules;
using Celeste.Physics.Collision.Shapes;
using Celeste.Scene.Entities;
using Celeste.Scene.Tracking;
using Celeste.Simulation;
using Celeste.Simulation.Gameplay.Player.Collision.Components;
using Celeste.Simulation.Presentation.Lighting;

namespace Celeste.Mods.JungleHelper.Entities.Insects
{
    [CustomEntity("JungleHelper/Firefly")]
    [RuntimeTypeAlias("Celeste.Mod.JungleHelper.Entities.Firefly")]
    public class Firefly : SceneEntityState
    {
        private readonly IOriginalMoonCreatureProcess engine;
        private static readonly MoonCreatureMotionSettings MotionSettings = MoonCreatureMotionSettings.Original;
        private Vector2 start;
        private Vector2 target;
        private float targetTimer;
        private Vector2 speed;
        private Vector2 bump;
        private RoomPlayerSession following;
        private Vector2 followingOffset;
        private float followingTime;
        private readonly Sprite sprite;
        private readonly VertexLightState light;
        private readonly int spawn;
        private readonly string reskinName;
        private IntRect originLevelBounds;

        public Firefly(ISourceEngineContext engine, Vector2 position, string reskinName)
            : base(((IOriginalMoonCreatureProcess)engine).EntitySourceDataRegistration)
        {
            this.engine = (IOriginalMoonCreatureProcess)engine;
            Tags = this.engine.Tags.TransitionUpdate.Value;
            Depth = -13010;
            Collider = new HitboxColliderGeometry(20f, 20f, -10f, -10f);
            start = position;
            targetTimer = 0f;
            MoonCreatureSimulation.GetRandomTarget(this.engine, start, ref target, 32f, 8f);
            Position = target;
            OwnedComponents.Add(new PlayerColliderComponentState(OnPlayer));
            OwnedComponents.Add(sprite = JungleHelperModule.For(engine).CreateReskinnableSprite(reskinName, "firefly"));
            OwnedComponents.Add(light = new VertexLightState(engine, Vector2.Zero, PackedColor.White, 1f, 4, 8));
            this.reskinName = reskinName;
        }

        public Firefly(ISourceEngineContext engine, EntityData data, Vector2 offset)
            : this(engine, data.Position + offset, data.Attr("sprite"))
        {
            spawn = data.Int("number", 1) - 1;
        }

        public override void Added(object scene)
        {
            base.Added(scene);
            for (int i = 0; i < spawn; i++)
                ((ISourceScene)scene).SourceScene.QueueSourceEntity(new Firefly(engine,
                    Position + new Vector2(SourceRandomMath.Range(engine.Random, -4, 4),
                        SourceRandomMath.Range(engine.Random, -4, 4)), reskinName));
            originLevelBounds = ((OriginalLevelSceneRoot)scene).Bounds;
        }

        private void OnPlayer(RoomPlayerSession player)
            => MoonCreatureSimulation.OnPlayer(engine, Position, start, player, MotionSettings,
                ref bump, ref following, ref followingTime, ref followingOffset);

        public override void Update()
        {
            base.Update();
            MoonCreatureSimulation.Update(engine, this, start, ref target, ref targetTimer, ref speed,
                ref bump, ref following, ref followingTime, ref followingOffset, MotionSettings);
            Position = new Vector2(
                ScalarMath.Clamp(Position.X, originLevelBounds.Left + 4, originLevelBounds.Right - 4),
                ScalarMath.Clamp(Position.Y, originLevelBounds.Top + 4, originLevelBounds.Bottom - 4));
            switch (sprite.Texture.AtlasPath)
            {
                case "JungleHelper/Firefly/firefly00":
                    light.Visible = true;
                    light.StartRadius = 4;
                    light.EndRadius = 8;
                    break;
                case "JungleHelper/Firefly/firefly01":
                case "JungleHelper/Firefly/firefly04":
                    light.Visible = true;
                    light.StartRadius = 2;
                    light.EndRadius = 4;
                    break;
                case "JungleHelper/Firefly/firefly02":
                case "JungleHelper/Firefly/firefly05":
                    light.Visible = true;
                    light.StartRadius = 1;
                    light.EndRadius = 2;
                    break;
                case "JungleHelper/Firefly/firefly03":
                    light.Visible = false;
                    break;
            }
        }

        public override void Render(ISourceRenderContext context)
        {
            Vector2 original = Position;
            Position = Vector2Geometry.Floor(Position);
            base.Render(context);
            Position = original;
        }
    }
}
