using System;
using System.Collections.Generic;
using System.Reflection;
using System.Numerics;
using Celeste.Core.Scheduling;
using Celeste.Core.Mathematics;
using Celeste.Graphics.Display.Colors;
using Celeste.Graphics.Animation;
using Celeste.Graphics.Content;
using Celeste.Everest.Modules.Entities.Metadata;
using Celeste.Core.Reflection.Runtime;
using Celeste.Scene.Tracking.Attributes;
using Celeste.Scene.Tracking;
using Celeste.Scene.Entities;
using Celeste.Scene.Scheduling.ExecutionTags;
using Celeste.Physics.Collision;
using Celeste.Physics.Collision.Shapes;
using Celeste.Simulation.Gameplay.Actors.Foundation;
using Celeste.Simulation.Gameplay.Platforms.Foundation;
using Celeste.Simulation.Gameplay.Player.Collision.Components;
using Celeste.Simulation.Gameplay.Player.Presentation.Sprites;
using Celeste.Game.Execution.Processes;
using Celeste.Mods.JungleHelper.Modules;
using Celeste.Mods.JungleHelper.Components;
using Celeste.Mods.JungleHelper.Runtime.Lanterns;
using Celeste.Mods.JungleHelper.Triggers;
using Celeste.Mods.MaxHelpingHand.Entities.Switches;
using Player = Celeste.Simulation.RoomPlayerSession;
using Level = Celeste.Game.Execution.Levels.OriginalLevelSceneRoot;
using Color = Celeste.Core.Display.PackedColor;
using Image = Celeste.Graphics.Animation.SpriteImage;
using PlayerCollider = Celeste.Simulation.Gameplay.Player.Collision.Components.PlayerColliderComponentState;
using TransitionListener = Celeste.Simulation.RoomTransitionListener;
using ParticleType = Celeste.Simulation.Presentation.Particles.Definitions.ParticleTypePresentation;
using TouchSwitch = Celeste.Game.Gameplay.Mechanisms.Switches.OriginalTouchSwitchState;
using Water = Celeste.Game.Gameplay.Environment.Water.OriginalWaterState;
using SwapBlock = Celeste.Game.Entities.Platforms.SwapBlocks.OriginalSwapBlockState;
using DashSwitch = Celeste.Game.Gameplay.Mechanisms.DashSwitches.OriginalDashSwitchState;
using EntityData = Celeste.Formats.Map.Entities.Structure.EntityData;
using Hitbox = Celeste.Physics.Collision.Shapes.HitboxColliderGeometry;

namespace Celeste.Mods.JungleHelper.Entities.Lanterns
{
    [CustomEntity("JungleHelper/Lantern = Load")]
    [SceneTracked(true)]
    [RuntimeTypeAlias("Celeste.Mod.JungleHelper.Entities.Lantern")]
    public sealed class Lantern : SourceActorEntityState
    {
        public static Lantern Load(Level level, Celeste.Game.Content.Maps.Levels.OriginalLevelRoomData room,
            Vector2 offset, Celeste.Game.Content.Maps.Entities.OriginalLevelEntityData data)
        {
            foreach (var module in ((Celeste.Everest.Modules.Runtime.ISourceEverestModulesEnvironment)level
                         .EngineContext).Modules)
                if (module is JungleHelperModule owner)
                    return new Lantern(level.EngineContext, owner, data, offset);
            throw new InvalidOperationException("JungleHelper module has not loaded.");
        }

        private static readonly FieldInfo NextSpriteMode =
            typeof(Player).GetField("nextSpriteMode", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly ParticleType P_Regen;
        private readonly ISourceEngineContext engine;
        private readonly JungleHelperModule module;
        public bool IsLit => sprite.CurrentAnimationID != "unlit";

        private float regrabDelay = 0f;
        private float speedY = 0f;
        private float respawnDelay = 0f;

        private Vector2 startingPosition;
        private bool doRespawn = true;
        private Sprite sprite;
        private float fadeOutAlpha = 1f;
        private ComponentWithDepth<Image> lanternOverlay;

        private readonly string reskinName;
        private readonly string lightPath;
        private readonly bool onlyIfMaddyNotHolding;

        // registers the player's position when the lantern was dropped (if it was dropped),
        // to check if Maddy moved between the lantern drop and its actual spawn.
        private Vector2? playerPositionWhenDropped;

        public Lantern(ISourceEngineContext engine, JungleHelperModule module, EntityData data, Vector2 offset)
            : base(new PixelActor(null, data.Position + offset), engine,
                ((ISourcePlatformTargetMotionEnvironment)engine).TargetMotionPolicy)
        {
            this.engine = engine;
            this.module = module;
            sprite = module.CreateLanternSprite(data);
            sprite.Y = 5;
            Add(sprite);
            Collider = new Hitbox(8, 8, -4, 0);
            startingPosition = Position;

            reskinName = data.Attr("sprite");
            lightPath = data.Attr("lightSprite", defaultValue: "JungleHelper/Lantern/Overlay");
            onlyIfMaddyNotHolding = data.Bool("onlyIfMaddyNotHolding", defaultValue: false);

            Add(new PlayerCollider(onPlayer));

            P_Regen = module.LanternRegenParticle;

            Image overlay =
                new Image(((ISourceGameGraphicsEnvironment)engine).GameGraphics.GameAtlas.Entries[lightPath]);
            overlay.Position = new Vector2((int)(Center - Position).X, (int)(Center - Position).Y);
            overlay.CenterOrigin();
            Add(lanternOverlay = new ComponentWithDepth<Image>(engine, overlay) { Depth = 1500 });

            lanternOverlay.Add(new TransitionListener
            {
                OnOut = progress =>
                {
                    if (lanternOverlay.Entity == null || !((SceneEntityState)lanternOverlay.Entity).TagCheck(
                            ((ISourcePersistentTransitionExecutionTagsEnvironment)engine)
                            .PersistentTransitionExecutionTags.Persistent.Value))
                    {
                        // fade out the light emitted by the lanterns we're leaving behind.
                        // This allows the transition to look smoother if the player dropped a lantern just before transitioning screens.
                        lanternOverlay.Component.Color = Color.White * (1 - progress);
                    }
                }
            });
        }

        public override void Added(object scene)
        {
            base.Added(scene);

            if (onlyIfMaddyNotHolding)
            {
                Player p = ((ISourceScene)Scene).SourceScene.Entities.GetTrackedEntity<Player>();
                if (p != null && EnforceSkinController.HasLantern(p.Sprite.Mode))
                {
                    // Maddy is coming from another room with a lantern: we should destroy this one.
                    RemoveSelf();
                }
            }

            if (playerPositionWhenDropped.HasValue)
            {
                Player p = ((ISourceScene)Scene).SourceScene.Entities.GetTrackedEntity<Player>();
                if (p != null)
                {
                    Position += (p.Position - playerPositionWhenDropped.Value);
                }
            }
        }

        private void onPlayer(Player player)
        {
            if (player.Input.GrabCheck &&
                (player.Data.DashAttacking(player.Machine.State) || !player.Data.Motion.Ducking) &&
                !HasCollision<DropLanternTrigger>(player) && doesNotHaveAndWontHaveLantern(player))
            {
                // the player grabs the lantern.
                // on a technical level, Maddy's sprite changes to have her holding the lantern, and the lantern disappears.
                EnforceSkinController.ChangePlayerSpriteMode(engine, player, hasLantern: true);
                RemoveSelf();

                module.LanternLighting.StartingPosition = startingPosition;
                module.LanternLighting.DoRespawn = doRespawn;
                module.LanternLighting.ReskinName = reskinName;
                module.LanternLighting.LightPath = lightPath;
                module.LanternLighting.DropTimer = 0.25f;

                // detach the glow from the lantern and attach it to the player.
                player.Add(lanternOverlay);
                lanternOverlay.Component.Position = new Vector2((int)(player.Center - player.Position).X,
                    (int)(player.Center - player.Position).Y);
            }
        }

        private bool doesNotHaveAndWontHaveLantern(Player player)
        {
            // check if the player has the lantern.
            bool result = !EnforceSkinController.HasLantern(player.Sprite.Mode);
            if (result)
            {
                // check if the player will have the lantern on the next frame.
                PlayerSpriteMode? nextMode = (PlayerSpriteMode?)NextSpriteMode.GetValue(player);
                if (nextMode != null)
                {
                    result = !EnforceSkinController.HasLantern(nextMode ?? default);
                }
            }

            return result;
        }

        public override void Update()
        {
            base.Update();

            // enforce the respawn delay.
            if (respawnDelay > 0f)
            {
                respawnDelay -= engine.DeltaTime;
                if (respawnDelay <= 0f)
                {
                    // move back the lantern to its starting point.
                    Position = startingPosition;
                    regrabDelay = 0f;
                    speedY = 0f;
                    Visible = Collidable = true;

                    ((IOriginalGameplayProcess)engine).AudioSession.Play("event:/game/04_cliffside/arrowblock_reappear",
                        Position, ((IOriginalGameplayProcess)engine).SaveContext);
                    (Scene as Level).ParticlesFG.Emit(P_Regen, 16, Center, Vector2.One * 2f);
                }
                else
                {
                    // don't update the lantern (no need to apply gravity and all that, it doesn't exist :a:)
                    return;
                }
            }

            // enforce the regrab delay.
            if (regrabDelay > 0f)
            {
                regrabDelay -= engine.DeltaTime;
                if (regrabDelay <= 0f)
                {
                    Collidable = (sprite.CurrentAnimationID != "unlit");
                }
            }

            // enforce gravity.
            if (!OnGround())
            {
                float acceleration = 800f;
                if (Math.Abs(speedY) <= 30f)
                {
                    acceleration *= 0.5f;
                }

                speedY = ScalarMath.Approach(speedY, 200f, acceleration * engine.DeltaTime,
                    ((Celeste.Core.Mathematics.Extrema.ISourceExtremaEnvironment)engine).ExtremaArithmetic);
            }

            MoveV(speedY * engine.DeltaTime, onCollideV);

            // enforce fading out.
            if (OnGround() && sprite.CurrentAnimationID == "unlit")
            {
                fadeOutAlpha = ScalarMath.Approach(fadeOutAlpha, 0, engine.DeltaTime,
                    ((Celeste.Core.Mathematics.Extrema.ISourceExtremaEnvironment)engine).ExtremaArithmetic);
                sprite.Color = Color.White * fadeOutAlpha;
                if (fadeOutAlpha == 0f)
                {
                    // it ended fading out. so make it respawn
                    lanternIsLost();
                    fadeOutAlpha = 1f;
                    sprite.Color = Color.White;
                    sprite.Play("idle");

                    // move it back to its original position to be sure we move it out of water.
                    Position = startingPosition;
                }
            }

            // handle the lantern getting dropped off the level
            if (Top > (Scene as Level).Bounds.Bottom)
            {
                lanternIsLost();
            }

            // trigger touch switches while falling.
            bool oldCollidable = Collidable;
            Collidable = true;
            foreach (TouchSwitch touchSwitch in Collisions<TouchSwitch>())
            {
                touchSwitch.TurnOn();
            }

            if (module.HasMaxHelpingHandFlagSwitches)
            {
                turnOnFlagTouchSwitches();
            }

            Collidable = oldCollidable;

            // if touching water, the lantern should destroy itself.
            if (sprite.CurrentAnimationID != "unlit" && HasCollision<Water>(this))
            {
                Collidable = false;
                sprite.Play("unlit");
            }

            // turn off lantern overlay if the lantern isn't lit.
            lanternOverlay.Component.Visible = sprite.CurrentAnimationID != "unlit" && Visible;
        }

        // this method only exists to isolate usages of Max Helping Hand.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void turnOnFlagTouchSwitches()
        {
            foreach (FlagTouchSwitch touchSwitch in Collisions<FlagTouchSwitch>())
            {
                touchSwitch.TurnOn();
            }
        }

        private void lanternIsLost()
        {
            Visible = Collidable = false;
            if (doRespawn)
            {
                respawnDelay = 2f;
            }
            else
            {
                // this happens when the lantern comes from another screen and thus can't respawn
                RemoveSelf();
            }
        }

        private void onCollideV(PixelCollision data)
        {
            if (speedY > 0f)
            {
                // there is a 99% chance this sound is going to be changed, but here it is.
                ((IOriginalGameplayProcess)engine).AudioSession.Play(
                    "event:/game/05_mirror_temple/crystaltheo_hit_ground", Position,
                    ((IOriginalGameplayProcess)engine).SaveContext, "crystal_velocity", 0f);
            }

            if (speedY > 160f)
            {
                // emit bounce particles
                (Scene as Level).Particles.Emit(((IOriginalGameplayProcess)engine).ParticleTypes.TheoCrystal.P_Impact,
                    12, BottomCenter, Vector2.UnitY * 6f, -(float)(Math.PI / 2f));
            }

            if (speedY > 140f && !(data.Hit is SwapBlock) && !(data.Hit is DashSwitch))
            {
                // bounce
                speedY *= -0.4f;
            }
            else
            {
                speedY = 0f;
            }
        }


        internal void ConfigureDropped(Player player, Vector2? originalPosition, bool originalRespawn, bool destroy)
        {
            regrabDelay = .25f;
            Collidable = false;
            if (originalPosition.HasValue)
            {
                startingPosition = originalPosition.Value;
                doRespawn = originalRespawn;
            }

            if (destroy) sprite.Play("unlit");
            playerPositionWhenDropped = player.Position;
        }

        private List<T> Collisions<T>() where T : SceneEntityState
        {
            var result = new List<T>();
            var tracked = ((ISourceScene)Scene).SourceScene.Entities.GetTrackedEntities(typeof(T));
            for (int i = 0; i < tracked.Count; i++)
                if (SourceEntityCollision.Check(this, (T)tracked[i]))
                    result.Add((T)tracked[i]);
            return result;
        }

        private static bool HasCollision<T>(SceneEntityState source) where T : SceneEntityState
        {
            var tracked = ((ISourceScene)source.Scene).SourceScene.Entities.GetTrackedEntities(typeof(T));
            for (int i = 0; i < tracked.Count; i++)
                if (SourceEntityCollision.Check(source, (T)tracked[i]))
                    return true;
            return false;
        }
    }
}
