using System;
using System.Numerics;
using Celeste.Core.Mathematics;
using Celeste.Core.Reflection.Runtime;
using Celeste.Core.Scheduling;
using Celeste.Everest.Modules.Entities.Metadata;
using Celeste.Formats.Map.Entities.Structure;
using Celeste.Graphics.Animation;
using Celeste.Mods.JungleHelper.Modules;
using Celeste.Mods.JungleHelper.Runtime.Lanterns;
using Celeste.Physics.Collision;
using Celeste.Physics.Collision.Shapes;
using Celeste.Scene.Tracking;
using Celeste.Simulation;
using Celeste.Simulation.Gameplay.Actors.Foundation;
using Celeste.Simulation.Gameplay.Platforms.Foundation;
using Celeste.Simulation.Gameplay.Player.Collision.Components;

namespace Celeste.Mods.JungleHelper.Entities.Hazards
{
    [CustomEntity("JungleHelper/Snake")]
    [RuntimeTypeAlias("Celeste.Mod.JungleHelper.Entities.Snake")]
    public sealed class Snake : SourceActorEntityState
    {
        private readonly ISourceEngineContext engine;
        private readonly Vector2 startingPosition;
        private readonly Vector2 scaredPosition;
        private readonly SimulationStateMachine stateMachine;
        private readonly Sprite sprite;
        private readonly JungleLanternLighting lanternLighting;
        private float currentSpeed;

        public Snake(ISourceEngineContext engine, EntityData data, Vector2 offset)
            : base(new PixelActor(null, data.Position + offset), engine,
                ((ISourcePlatformTargetMotionEnvironment)engine).TargetMotionPolicy)
        {
            this.engine = engine;
            startingPosition = Position;
            scaredPosition = data.Nodes[0] + offset;
            stateMachine = new SimulationStateMachine(engine, 10);
            stateMachine.SetCallbacks(0, WaitingForPlayerUpdate, begin: WaitingForPlayerBegin);
            stateMachine.SetCallbacks(1, AttackingPlayerUpdate, begin: AttackingPlayerBegin);
            stateMachine.SetCallbacks(2, ReturningToStartPointUpdate, begin: ReturningToStartPointBegin);
            stateMachine.SetCallbacks(3, RunningAwayUpdate, begin: RunningAwayBegin);
            stateMachine.SetCallbacks(4, HiddenUpdate, begin: HiddenBegin);
            Add(stateMachine);
            var snakeRandom = new Random();
            var module = JungleHelperModule.For(engine);
            var spriteHissOverlay = module.CreateReskinnableSprite(data.Attr("sprite", ""), "snek");
            spriteHissOverlay.Visible = false;
            spriteHissOverlay.OnFinish = _ => spriteHissOverlay.Visible = false;
            sprite = module.CreateReskinnableSprite(data.Attr("sprite", ""), "snek");
            sprite.FlipX = data.Bool("left", false);
            sprite.OnLoop = animation =>
            {
                if (animation == "idle_aggro" && snakeRandom.Next(10) == 1)
                {
                    spriteHissOverlay.Play("idle_hiss");
                    spriteHissOverlay.Visible = true;
                    spriteHissOverlay.FlipX = sprite.FlipX;
                }
            };
            sprite.OnChange = (from, to) =>
            {
                if (from == "idle_aggro" && to != "idle_aggro") spriteHissOverlay.Visible = false;
            };
            Add(sprite);
            Add(spriteHissOverlay);
            Collider = new HitboxColliderGeometry(58f, 6f, 3f, 10f);
            Add(new PlayerColliderComponentState(OnPlayer));
            lanternLighting = module.LanternLighting;
        }

        private void OnPlayer(RoomPlayerSession player)
        {
            if (player.Center != Center) player.Die(Vector2Math.Normalize(player.Center - Center));
            else player.Die(Vector2.Zero);
        }

        private RoomPlayerSession Player =>
            ((ISourceScene)Scene).SourceScene.Entities.GetTrackedEntity<RoomPlayerSession>();

        private void WaitingForPlayerBegin() => sprite.Play("idle_aggro");

        private int WaitingForPlayerUpdate()
        {
            var player = Player;
            if (player != null)
            {
                var delta = Center - player.Center;
                if (Vector2Math.LengthSquared(delta) < 5625f && Math.Abs(player.Bottom - Bottom) < 5f) return 1;
            }

            return 0;
        }

        private void AttackingPlayerBegin() => currentSpeed = 0f;

        private int AttackingPlayerUpdate()
        {
            var player = Player;
            if (player != null)
            {
                var delta = Center - player.Center;
                if (!(Vector2Math.LengthSquared(delta) > 40000f))
                {
                    currentSpeed = ScalarMath.Approach(currentSpeed, 200f, 300f * engine.DeltaTime);
                    float exactX = ExactPosition.X;
                    float positionX = Position.X;
                    MoveTowardsPlayer(player);
                    if (positionX != Position.X)
                    {
                        if (!sprite.CurrentAnimationID.StartsWith("moving_")) sprite.Play("moving_aggro");
                        sprite.FlipX = positionX > Position.X;
                    }
                    else if (exactX == ExactPosition.X)
                    {
                        if (!sprite.CurrentAnimationID.StartsWith("idle_")) sprite.Play("idle_aggro");
                        currentSpeed = 0f;
                    }

                    return 1;
                }
            }

            return 2;
        }

        private void MoveTowardsPlayer(RoomPlayerSession player)
        {
            float target =
                ScalarMath.Approach(ExactPosition.X, player.Position.X - 32f, currentSpeed * engine.DeltaTime);
            Collider.Width = 1f;
            if (target > ExactPosition.X) Collider.Position.X = 60f;
            float position = ExactPosition.X;
            while (position != target)
            {
                position = ScalarMath.Approach(position, target, 1f);
                if (!OnGround(new Vector2(position, ExactPosition.Y))) break;
                MoveToX(position);
            }

            Collider.Width = 58f;
            Collider.Position.X = 3f;
        }

        private void ReturningToStartPointBegin()
        {
            sprite.Play("moving_aggro");
            sprite.FlipX = startingPosition.X < Position.X;
        }

        private int ReturningToStartPointUpdate()
        {
            float target = ScalarMath.Approach(Position.X, startingPosition.X, 100f * engine.DeltaTime);
            NaiveMove(new Vector2(target - Position.X, 0f));
            return Position.X == startingPosition.X ? 0 : 2;
        }

        private void RunningAwayBegin() => sprite.Play("shock");

        private int RunningAwayUpdate()
        {
            if (sprite.CurrentAnimationID == "shock") return 3;
            sprite.FlipX = scaredPosition.X < Position.X;
            float target = ScalarMath.Approach(Position.X, scaredPosition.X, 200f * engine.DeltaTime);
            NaiveMove(new Vector2(target - Position.X, 0f));
            return Position.X == scaredPosition.X ? 4 : 3;
        }

        private void HiddenBegin() => sprite.Play("idle_scared");

        private int HiddenUpdate()
            => lanternLighting.GetClosestDistanceTo(startingPosition + (Center - Position), Scene, out _) < 75f ? 4 : 2;

        public override void Update()
        {
            base.Update();
            if (sprite.CurrentAnimationID.Contains("_aggro") &&
                lanternLighting.GetClosestDistanceTo(startingPosition + (Center - Position), Scene, out _) < 75f)
                stateMachine.State = 3;
        }
    }
}
