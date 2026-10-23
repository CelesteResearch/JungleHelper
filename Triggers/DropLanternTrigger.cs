using System.Linq;
using System.Numerics;
using Celeste.Core.Components;
using Celeste.Core.Mathematics.Extrema;
using Celeste.Core.Scheduling;
using Celeste.Core.Reflection.Runtime;
using Celeste.Everest.Modules.Entities.Metadata;
using Celeste.Everest.Modules.Runtime;
using Celeste.Formats.Map.Entities.Structure;
using Celeste.Mods.JungleHelper.Modules;
using Celeste.Mods.JungleHelper.Runtime.Lanterns;
using Celeste.Scene.Tracking.Attributes;
using Celeste.Simulation;
using Celeste.Simulation.Gameplay.Triggers.Foundation;

namespace Celeste.Mods.JungleHelper.Triggers
{
    [CustomEntity("JungleHelper/DropLanternTrigger")]
    [SceneTracked(true)]
    [RuntimeTypeAlias("Celeste.Mod.JungleHelper.Triggers.DropLanternTrigger")]
    public sealed class DropLanternTrigger : SourceTriggerEntityState
    {
        private readonly bool oneUse;
        private readonly bool destroyLantern;
        private readonly JungleHelperModule module;

        public DropLanternTrigger(ISourceEngineContext engine, EntityData data, Vector2 offset)
            : base(((ISourceEntitySourceDataEnvironment)engine).EntitySourceDataRegistration,
                data.Position + offset, data.Width, data.Height, ((ISourceExtremaEnvironment)engine).ExtremaArithmetic)
        {
            module = ((ISourceEverestModulesEnvironment)engine).Modules.OfType<JungleHelperModule>().Single();
            oneUse = data.Bool("oneUse");
            destroyLantern = data.Bool("destroyLantern");
        }

        public override void OnEnter(RoomPlayerSession player)
        {
            base.OnEnter(player);
            if (!EnforceSkinController.HasLantern(player.Sprite.Mode)) return;
            module.LanternLighting.DropLantern(player, destroyLantern);
            if (oneUse) RemoveSelf();
        }
    }
}
