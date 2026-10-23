using Entity = Celeste.Scene.Entities.SceneEntityState;
using System.Numerics;
using Celeste.Core.Components;
using Celeste.Core.Scheduling;
using Celeste.Core.Audio;
using Celeste.Core.Geometry;
using Celeste.Core.Rendering;
using Celeste.Core.Mathematics.Easing;
using Celeste.Core.Reflection.Runtime;
using Celeste.Graphics.Content;
using Celeste.Graphics.Animation;
using Celeste.Formats.Map.Entities.Structure;
using Celeste.Game.Content.Maps.Entities;
using Celeste.Game.Content.Maps.Levels;
using Celeste.Game.Execution.Levels;
using Celeste.Physics.Collision;
using Celeste.Physics.Collision.Shapes;
using Celeste.Scene.Entities;
using Celeste.Scene.Tracking;
using Celeste.Scene.Tracking.Attributes;
using Celeste.Simulation;
using Celeste.Simulation.Gameplay.Platforms.StaticMovers;
using Celeste.Simulation.Gameplay.Player.Collision.Components;
using Level = Celeste.Game.Execution.Levels.OriginalLevelSceneRoot;
using LevelData = Celeste.Game.Content.Maps.Levels.OriginalLevelRoomData;
using Player = Celeste.Simulation.RoomPlayerSession;
using Hitbox = Celeste.Physics.Collision.Shapes.HitboxColliderGeometry;
using ColliderList = Celeste.Physics.Collision.Shapes.ColliderListGeometry;
using StaticMover = Celeste.Simulation.Gameplay.Platforms.StaticMovers.SourceStaticMoverState;
using PlayerCollider = Celeste.Simulation.Gameplay.Player.Collision.Components.PlayerColliderComponentState;
using Image = Celeste.Graphics.Animation.SpriteImage;
using Celeste.Everest.Modules.Entities.Metadata;
using System;

namespace Celeste.Mods.JungleHelper.Entities.Interaction.Mushrooms
{
    [SceneTracked]
    [RuntimeTypeAlias("Celeste.Mod.JungleHelper.Entities.BouncyShroom")]
    [CustomEntity(
        "JungleHelper/BouncyShroomUp = BounceUp",
        "JungleHelper/BouncyShroomLeft = BounceLeft",
        "JungleHelper/BouncyShroomRight = BounceRight")]
    public class BouncyShroom : Entity
    {
        public static Entity BounceUp(Level level, LevelData levelData, Vector2 position,
            OriginalLevelEntityData entityData)
            => new BouncyShroom(level.EngineContext, entityData, position, Directions.Up);

        public static Entity BounceLeft(Level level, LevelData levelData, Vector2 position,
            OriginalLevelEntityData entityData)
            => new BouncyShroom(level.EngineContext, entityData, position, Directions.Left);

        public static Entity BounceRight(Level level, LevelData levelData, Vector2 position,
            OriginalLevelEntityData entityData)
            => new BouncyShroom(level.EngineContext, entityData, position, Directions.Right);

        public enum Directions
        {
            Up,
            Left,
            Right
        }

        public Directions Direction;

        private readonly ISourceEngineContext engine;
        private Vector2 imageOffset;

        private bool dashedIntoIt;
        private bool collidedWithIt;

        private Vector2 particlePosAdjust;

        private float yeetSpeedCalcX;
        private float yeetSpeedCalcY;
        private Sprite bouncyShroomSprite;
        private StaticMover staticMover;
        private bool triggerBlocks;

        private int? dashCount;

        public BouncyShroom(ISourceEngineContext engine, Vector2 position, Directions direction, int yeetx, int yeety,
            string spriteDirectory, int? dashCount, bool triggerBlocks)
            : base(((ISourceEntitySourceDataEnvironment)engine).EntitySourceDataRegistration, position)
        {
            this.engine = engine;
            Depth = -1;
            Direction = direction;
            this.dashCount = dashCount;
            this.triggerBlocks = triggerBlocks;

            // making the bounce particles
            particlePosAdjust = new Vector2(0, 1);

            bouncyShroomSprite = new Sprite(engine, ((ISourceGameGraphicsEnvironment)engine).GameGraphics.GameAtlas,
                spriteDirectory + "/");

            int[] frames = { 2, 3, 4, 5, 6, 7, 8 };

            switch (Direction)
            {
                case Directions.Up:
                    bouncyShroomSprite.AddLoop("Idle", "mushroom", 0.15f, 0);
                    bouncyShroomSprite.Add("Bounce", "mushroom", 0.05f, frames);

                    Collider = new ColliderList(
                        new Hitbox(8f, 8f, -16f, -15f),
                        new Hitbox(8f, 8f, -8f, -16f),
                        new Hitbox(8f, 8f, 0f, -15f));

                    yeetSpeedCalcY = yeety;

                    Add(staticMover = new StaticMover()
                    {
                        OnShake = OnShake,
                        SolidChecker = s =>
                            SourceEntityCollision.CheckRect(s,
                                new IntRect((int)Position.X - 10, (int)Position.Y + 8, 12, 1)),
                        JumpThruChecker = jt => SourceEntityCollision.CheckRect((ISourceEntityCollider)jt,
                            new IntRect((int)(Position.X - 10), (int)Position.Y + 8, 12, 1))
                    });
                    break;
                case Directions.Right:
                    bouncyShroomSprite.AddLoop("Idle", "mushroom_rd_", 0.15f, 0);
                    bouncyShroomSprite.Add("Bounce", "mushroom_rd_", 0.05f, frames);

                    Collider = new ColliderList(
                        new Hitbox(6f, 6f, -14f, -14f),
                        new Hitbox(8f, 8f, -8f, -15f),
                        new Hitbox(4f, 8f, 0f, -13f),
                        new Hitbox(4f, 8f, 4f, -9f));

                    yeetSpeedCalcY = yeety;
                    yeetSpeedCalcX = yeetx;

                    Add(staticMover = new StaticMover()
                    {
                        OnShake = OnShake,
                        SolidChecker = s =>
                            SourceEntityCollision.CheckRect(s,
                                new IntRect((int)Position.X - 16, (int)Position.Y + 8, 16, 1)),
                        JumpThruChecker = jt => SourceEntityCollision.CheckRect((ISourceEntityCollider)jt,
                            new IntRect((int)(Position.X - 16), (int)Position.Y + 8, 16, 1))
                    });
                    break;
                case Directions.Left:
                    Collider = new ColliderList(
                        new Hitbox(6f, 6f, 0f, -14f),
                        new Hitbox(8f, 8f, -8f, -15f),
                        new Hitbox(4f, 8f, -12f, -13f),
                        new Hitbox(4f, 8f, -16f, -9f));

                    bouncyShroomSprite.AddLoop("Idle", "mushroom_ld_", 0.15f, 0);
                    bouncyShroomSprite.Add("Bounce", "mushroom_ld_", 0.05f, frames);

                    yeetSpeedCalcY = yeety;
                    yeetSpeedCalcX = -yeetx;
                    Add(staticMover = new StaticMover()
                    {
                        OnShake = OnShake,
                        SolidChecker = s =>
                            SourceEntityCollision.CheckRect(s,
                                new IntRect((int)Position.X - 8, (int)Position.Y + 8, 16, 1)),
                        JumpThruChecker = jt => SourceEntityCollision.CheckRect((ISourceEntityCollider)jt,
                            new IntRect((int)(Position.X - 8), (int)Position.Y + 8, 16, 1))
                    });
                    break;
            }

            Add(new PlayerCollider(OnCollide));
        }

        public BouncyShroom(ISourceEngineContext engine, EntityData data, Vector2 offset, Directions dir)
            : this(engine, data.Position + offset, dir, data.Int("yeetx", 200), data.Int("yeety", -290),
                data.Attr("spriteDirectory", "JungleHelper/BouncyShroom"),
                !string.IsNullOrEmpty(data.Attr("dashCount")) ? int.Parse(data.Attr("dashCount")) : null,
                data.Bool("triggerBlocks", false))
        {
        }

        public override void Added(object scene)
        {
            base.Added(scene);

            bouncyShroomSprite.Position = -20 * Vector2.UnitX - 20 * Vector2.UnitY;

            Add(bouncyShroomSprite);
            bouncyShroomSprite.Play("Idle");
        }

        private void OnShake(Vector2 amount)
        {
            imageOffset += amount;
        }

        public override void Render(ISourceRenderContext context)
        {
            Vector2 position = Position;
            Position += imageOffset;
            base.Render(context);
            Position = position;
        }

        public void SetOrigins(Vector2 origin)
        {
            foreach (SourceComponentState component in OwnedComponents)
            {
                if (component is Image image)
                {
                    Vector2 vector = origin - Position;
                    image.Origin = image.Origin + vector - image.Position;
                    image.Position = vector;
                }
            }
        }

        private void OnCollide(Player player)
        {
            if (player.Speed.Y >= 0f && player.Bottom <= Bottom)
            {
                if (Direction == Directions.Right)
                {
                    if (player.Speed.X < yeetSpeedCalcX)
                    {
                        player.Data.Motion.Normal.Speed.X = yeetSpeedCalcX;
                    }
                }
                else if (Direction == Directions.Left)
                {
                    if (player.Speed.X > yeetSpeedCalcX)
                    {
                        player.Data.Motion.Normal.Speed.X = yeetSpeedCalcX;
                    }
                }

                player.Data.Motion.Normal.Speed.Y = yeetSpeedCalcY;

                ((ISourceAudioEnvironment)engine).SourceAudio.Play("event:/junglehelper/sfx/Mushroom_boost");

                if (player.Data.DashAttacking(player.Machine.State))
                {
                    dashedIntoIt = true;
                    ((Level)Scene).Displacement.AddBurst(player.Position + particlePosAdjust, 0.4f, 8f, 64f, 0.5f,
                        ((ISourceEasingEnvironment)engine).EasingArithmetic.QuadOut,
                        ((ISourceEasingEnvironment)engine).EasingArithmetic.QuadOut);
                }

                bouncyShroomSprite.Play("Bounce");
                collidedWithIt = true;
            }
        }

        private bool CollidesWithPlayer()
        {
            var players = ((Level)Scene).SourceScene.Entities.GetTrackedEntities(typeof(Player));
            for (int i = 0; i < players.Count; i++)
                if (SourceEntityCollision.Check(this, (Player)players[i]))
                    return true;
            return false;
        }

        public override void Update()
        {
            base.Update();

            if (collidedWithIt == true && !CollidesWithPlayer())
            {
                var players = ((Level)Scene).SourceScene.Entities.GetTrackedEntities(typeof(Player));
                Player player = players.Count == 0 ? null : (Player)players[0];

                // stop right there if the player is dead
                if (player == null) return;

                // remove the player's dash state
                if (dashedIntoIt == true)
                {
                    player.Machine.State = 0;
                    dashedIntoIt = false;
                }

                // refill dashes, the usual way or using the "Dash Count" option
                if (dashCount.HasValue)
                {
                    player.Data.Dashes = Math.Max(player.Data.Dashes, dashCount.Value);
                }
                else if (!player.Inventory.NoRefills)
                {
                    player.RefillDash();
                }

                // refill stamina
                player.RefillStamina();

                if (triggerBlocks)
                {
                    staticMover.TriggerPlatform();
                }

                collidedWithIt = false;
            }
        }
    }
}
