using System;
using System.Numerics;
using Celeste.Core.Scheduling;
using Celeste.Mods.JungleHelper.Entities.Lanterns;
using Celeste.Mods.JungleHelper.Modules;
using Celeste.Scene.Tracking;
using Celeste.Simulation;
using Celeste.Simulation.Gameplay.Player.Presentation.Sprites;

namespace Celeste.Mods.JungleHelper.Runtime.Lanterns
{
    public sealed class JungleLanternLighting
    {
        private readonly ISourceEngineContext engine;
        private readonly JungleHelperModule module;
        internal Vector2? StartingPosition;
        internal bool DoRespawn;
        internal string ReskinName;
        internal string LightPath;
        internal float DropTimer;

        public JungleLanternLighting(ISourceEngineContext engine, JungleHelperModule module)
        {
            this.engine = engine;
            this.module = module;
        }

        public float GetClosestDistanceTo(Vector2 position, object scene, out Vector2 objectPosition)
        {
            float distance = float.MaxValue;
            objectPosition = Vector2.Zero;
            var registry = ((ISourceScene)scene).SourceScene.Entities;
            var player = registry.GetTrackedEntity<RoomPlayerSession>();
            if (player != null) objectPosition = player.Center;
            if (player != null && !EnforceSkinController.HasLantern(player.Sprite.Mode)) player = null;
            if (player != null) distance = Celeste.Core.Mathematics.Vector2Math.Length(position - player.Center);
            var lanterns = registry.GetTrackedEntities(typeof(Lantern));
            for (int i = 0; i < lanterns.Count; i++)
            {
                var lantern = (Lantern)lanterns[i];
                if (!lantern.IsLit || !lantern.Visible) continue;
                float candidateDistance = Celeste.Core.Mathematics.Vector2Math.Length(position - lantern.Center);
                if (distance > candidateDistance)
                {
                    distance = candidateDistance;
                    objectPosition = lantern.Center;
                }
            }

            return distance;
        }

        internal void ResetPlayerInfo()
        {
            StartingPosition = null;
            DoRespawn = false;
            ReskinName = null;
            LightPath = null;
            DropTimer = 0f;
        }

        public void DropLantern(RoomPlayerSession player, bool destroy)
        {
            EnforceSkinController.ChangePlayerSpriteMode(engine, player, false);
            var data = new Celeste.Formats.Map.Entities.Structure.EntityData
            {
                Position = new Vector2((int)player.Center.X, (int)player.Center.Y - 5f),
                Values = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "sprite", ReskinName ?? "" }, { "lightSprite", LightPath ?? "" }
                }
            };
            var lantern = new Lantern(engine, module, data, Vector2.Zero);
            lantern.ConfigureDropped(player, StartingPosition, DoRespawn, destroy);
            ((ISourceScene)player.Scene).SourceScene.AddEntity(lantern);
            player
                .Get<Celeste.Mods.JungleHelper.Components.ComponentWithDepth<Celeste.Graphics.Animation.SpriteImage>>()
                ?.RemoveSelf();
        }
    }
}
