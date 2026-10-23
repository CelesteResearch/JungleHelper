using System;
using System.Numerics;
using Celeste.Core.Components;
using Celeste.Core.Mathematics;
using Celeste.Core.Reflection.Runtime;
using Celeste.Core.Scheduling;
using Celeste.Everest.Modules.Entities.Metadata;
using Celeste.Formats.Map.Entities.Structure;
using Celeste.Graphics.Animation;
using Celeste.Mods.JungleHelper.Modules;
using Celeste.Mods.JungleHelper.Runtime.Lanterns;
using Celeste.Physics.Collision.Shapes;
using Celeste.Scene.Entities;
using Celeste.Simulation;
using Celeste.Simulation.Audio.Sources;
using Celeste.Simulation.Gameplay.Player.Collision.Components;

namespace Celeste.Mods.JungleHelper.Entities.Hazards
{
    [CustomEntity("JungleHelper/Cobweb")]
    [RuntimeTypeAlias("Celeste.Mod.JungleHelper.Entities.Cobweb")]
    public sealed class Cobweb : SceneEntityState
    {
        private readonly ISourceEngineContext engine;
        private readonly Sprite sprite;
        private RoomPlayerSession stuckPlayer;
        private readonly SoundSourceComponentState sfx;

        public Cobweb(ISourceEngineContext engine, EntityData data, Vector2 offset)
            : base(((ISourceEntitySourceDataEnvironment)engine).EntitySourceDataRegistration, data.Position + offset)
        {
            this.engine = engine;
            Collider = new CircleColliderGeometry(((ISourceVectorEnvironment)engine).VectorArithmetic, 10f);
            Add(sprite = JungleHelperModule.For(engine).CreateReskinnableSprite(data.Attr("sprite", ""), "cobweb"));
            Add(new PlayerColliderComponentState(CatchPlayer));
            Add(sfx = new SoundSourceComponentState(engine));
        }

        private void CatchPlayer(RoomPlayerSession player)
        {
            stuckPlayer = player;
            Collidable = false;
            player.Machine.State = 11;
            player.ForceCameraUpdate = true;
            player.Visible = false;
            player.Dummy.Gravity = false;
            sprite.Play("catch");
            sfx.Play("event:/junglehelper/sfx/Cobweb_Entry");
            player.Center = Position;
            RoundUpPlayerPosition(player);
            player.Data.Motion.Normal.Speed = Vector2.Zero;
            player.Data.Dashes = Math.Max(player.Data.Dashes, 1);
            if (EnforceSkinController.HasLantern(player.Sprite.Mode))
                JungleHelperModule.For(engine).LanternLighting.DropLantern(player, false);
        }

        public override void Update()
        {
            base.Update();
            if (stuckPlayer == null) return;
            stuckPlayer.Center = Position;
            RoundUpPlayerPosition(stuckPlayer);
            stuckPlayer.Data.Motion.Normal.Speed = Vector2.Zero;
            if (stuckPlayer.Motor.Dash.CanDash)
            {
                stuckPlayer.Machine.State = stuckPlayer.StartDash();
                stuckPlayer.ForceCameraUpdate = false;
                stuckPlayer.Visible = true;
                stuckPlayer.Dummy.Gravity = true;
                stuckPlayer = null;
                sprite.Play("break");
                sprite.OnFinish = _ => RemoveSelf();
                sfx.Play("event:/junglehelper/sfx/Cobweb_Snap");
            }
        }

        private static void RoundUpPlayerPosition(RoomPlayerSession player)
            => player.Position = new Vector2((int)player.Position.X, (int)player.Position.Y);
    }
}
