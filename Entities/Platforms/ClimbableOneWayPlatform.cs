using System;
using System.Collections.Generic;
using System.Numerics;
using Celeste.Core.Components;
using Celeste.Core.Randomness;
using Celeste.Core.Scheduling;
using Celeste.Everest.Modules.Entities.Metadata;
using Celeste.Game.Content.Maps.Entities;
using Celeste.Game.Content.Maps.Levels;
using Celeste.Game.Execution.Levels;
using Celeste.Graphics.Animation;
using Celeste.Graphics.Content;
using Celeste.Mods.JungleHelper.Configuration;
using Celeste.Physics.Collision;
using Celeste.Physics.Collision.Shapes;
using Celeste.Scene.Entities;
using Celeste.Scene.Tracking;
using Celeste.Scene.Tracking.Attributes;
using Celeste.Simulation;

namespace Celeste.Mods.JungleHelper.Entities.Platforms
{
    [CustomEntity("JungleHelper/ClimbableOneWayPlatform = Load")]
    [SceneTracked]
    [global::Celeste.Core.Reflection.Runtime.RuntimeTypeAlias(
        "Celeste.Mod.JungleHelper.Entities.ClimbableOneWayPlatform")]
    public class ClimbableOneWayPlatform : SceneEntityState
    {
        internal readonly ISourceEngineContext Engine;
        private readonly int lines;
        private readonly string overrideTexture;
        private readonly float animationDelay;
        internal readonly int SurfaceIndex;
        public bool AllowLeftToRight;
        internal readonly bool AllowWallJumping;
        private readonly bool sameDirBoost;
        public readonly StaminaBehavior StaminaBehavior;
        private readonly bool hasMomentumCarrying;
        public readonly float MomentumJumpDecayTime;
        public readonly float MomentumJumpDecayCurvature;
        internal float ClimbJumpGrabCooldown = -1f;
        private float initialStamina = -1f;
        public bool StamStored;
        private float initialSpeed;
        private float grabTimer;

        public static ClimbableOneWayPlatform Load(OriginalLevelSceneRoot level, OriginalLevelRoomData room,
            Vector2 offset, OriginalLevelEntityData data)
            => new ClimbableOneWayPlatform(level.EngineContext, data.Position + offset, data.Height, !data.Bool("left"),
                data.Attr("texture", "default"), data.Float("animationDelay"), data.Int("surfaceIndex", -1),
                data.Enum<StaminaBehavior>("staminaBehavior"), data.Bool("sameDirectionJumpBoost"),
                data.Float("momentumJumpDecayTime"), data.Float("momentumJumpDecayCurvature", 1f),
                data.Bool("allowWallJumping", true));

        public ClimbableOneWayPlatform(ISourceEngineContext engine, Vector2 position, int height, bool allowLeftToRight,
            string overrideTexture, float animationDelay, int surfaceIndex, StaminaBehavior staminaBehavior,
            bool sameDirBoost, float momentumJumpDecayTime, float momentumJumpDecayCurvature, bool allowWallJumping)
            : base(((ISourceEntitySourceDataEnvironment)engine).EntitySourceDataRegistration, position)
        {
            Engine = engine;
            lines = height / 8;
            AllowLeftToRight = allowLeftToRight;
            Depth = -60;
            this.overrideTexture = overrideTexture;
            this.animationDelay = animationDelay;
            SurfaceIndex = surfaceIndex;
            AllowWallJumping = allowWallJumping;
            Collider = new HitboxColliderGeometry(5, height, allowLeftToRight ? 3f : 0f, 0f);
            MomentumJumpDecayTime = Celeste.Core.Mathematics.ScalarMath.Clamp(momentumJumpDecayTime, 0f, 2147483600f);
            MomentumJumpDecayCurvature = Celeste.Core.Mathematics.ScalarMath.Clamp(momentumJumpDecayCurvature, 0f, 1f);
            hasMomentumCarrying = MomentumJumpDecayTime != 0f && MomentumJumpDecayCurvature != 0f;
            StaminaBehavior = staminaBehavior;
            this.sameDirBoost = sameDirBoost;
            initialStamina = staminaBehavior == StaminaBehavior.Conserve ? 0f : -1f;
        }

        public override void Awake(object scene)
        {
            var atlas = ((ISourceGameGraphicsEnvironment)Engine).GameGraphics.GameAtlas;
            if (animationDelay > 0f)
            {
                for (int i = 0; i < lines; i++)
                {
                    var sprite = new Sprite(Engine, atlas, "objects/jumpthru/" + overrideTexture);
                    sprite.AddLoop("idle", "", animationDelay);
                    Place(sprite, i);
                    sprite.Play("idle", false, false);
                    Add(sprite);
                }
            }
            else
            {
                string jumpthru = ((OriginalLevelSceneRoot)scene).AreaData.Jumpthru;
                if (!string.IsNullOrEmpty(overrideTexture) && !overrideTexture.Equals("default"))
                    jumpthru = overrideTexture;
                var texture = atlas["objects/jumpthru/" + jumpthru];
                int count = texture.FrameWidth / 8;
                var random = ((ISourceRandomEnvironment)Engine).Random;
                for (int i = 0; i < lines; i++)
                {
                    int x, y;
                    if (i == 0)
                    {
                        x = 0;
                        y = IsSideOpen(-Vector2.UnitY) ? 0 : 1;
                    }
                    else if (i == lines - 1)
                    {
                        x = count - 1;
                        y = IsSideOpen(Vector2.UnitY) ? 0 : 1;
                    }
                    else
                    {
                        x = 1 + random.Next(count - 2);
                        y = RandomSelection.Choose(random, 0, 1);
                    }

                    var image = new SpriteImage(texture.GetSubtexture(x * 8, y * 8, 8, 8));
                    Place(image, i);
                    Add(image);
                }
            }

            foreach (var other in GetPlatforms())
                if (SourceEntityCollision.Check(this, other))
                {
                    ClimbJumpGrabCooldown = other.ClimbJumpGrabCooldown;
                    break;
                }
        }

        private void Place(SourceGraphicsComponentState graphic, int line)
        {
            graphic.Y = line * 8;
            graphic.Rotation = (float)Math.PI / 2f;
            if (AllowLeftToRight) graphic.X = 8f;
            else graphic.Scale.Y = -1f;
        }

        private bool IsSideOpen(Vector2 move)
        {
            foreach (var solid in ((ISourceScene)Scene).SourceScene.Entities.GetTrackedEntities("Celeste.Solid"))
                if (solid is ISourceEntityCollider collider && !SourceEntityCollision.Check(this, collider) &&
                    SourceEntityCollision.CheckAt(this, collider, Position + move))
                    return true;
            return false;
        }

        internal IEnumerable<ClimbableOneWayPlatform> GetPlatforms()
        {
            foreach (var actual in ((ISourceScene)Scene).SourceScene.Entities.GetTrackedEntities(
                         typeof(ClimbableOneWayPlatform)))
                yield return (ClimbableOneWayPlatform)actual;
        }

        public override void Update()
        {
            base.Update();
            if (ClimbJumpGrabCooldown >= 0f) ClimbJumpGrabCooldown -= Engine.DeltaTime;
            if (grabTimer > 0f) grabTimer += Engine.DeltaTime;
        }

        internal void BeforeClimbBegin(RoomPlayerSession player)
        {
            if (hasMomentumCarrying)
            {
                initialSpeed = player.Speed.X;
                grabTimer = Engine.DeltaTime;
            }

            if (StaminaBehavior == StaminaBehavior.Conserve)
            {
                StamStored = true;
                initialStamina = player.Data.Stamina;
            }
        }

        internal void BeforeClimbUpdate(RoomPlayerSession player)
        {
            var aim = player.Input.Aim.Value.X;
            if ((player.Input.Jump.Pressed || player.Input.Jump.BufferTime < 0.08f) && (int)aim != 0)
            {
                if (hasMomentumCarrying && grabTimer < MomentumJumpDecayTime)
                {
                    var speed = player.Speed;
                    speed.X += BonusSpeed(initialSpeed, grabTimer);
                    player.Speed = speed;
                    grabTimer = 0f;
                }
                else if (sameDirBoost && Math.Abs(player.Speed.X) < 240f && aim != 0f &&
                         aim / Math.Abs(aim) == player.Facing)
                {
                    var speed = player.Speed;
                    speed.X += 140f * (1f - Math.Abs(speed.X) / 240f) * player.Facing;
                    player.Speed = speed;
                }

                StamStored = false;
            }

            if (StaminaBehavior == StaminaBehavior.Regain) player.RefillStamina();
            else if (StamStored) player.Data.Stamina = Math.Max(initialStamina, 21f);
        }

        internal void AfterClimbJump(RoomPlayerSession player)
        {
            ClimbJumpGrabCooldown = 0.35f;
            if (StaminaBehavior == StaminaBehavior.Regain) player.RefillStamina();
            else if (StaminaBehavior == StaminaBehavior.Conserve) player.Data.Stamina = Math.Max(initialStamina, 21f);
        }

        internal float BonusSpeed(float speedX, float time)
        {
            if (time > MomentumJumpDecayTime) return 0f;
            if (time <= 4f * Engine.DeltaTime) return speedX * 1.2f;
            return (float)(speedX * 1.2 * Math.Pow(MomentumJumpDecayTime, -2.0) * (MomentumJumpDecayTime + time) *
                           (MomentumJumpDecayTime - time) * Math.Pow(MomentumJumpDecayCurvature, time));
        }
    }
}
