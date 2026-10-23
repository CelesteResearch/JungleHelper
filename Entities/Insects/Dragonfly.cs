using System.Numerics;
using Celeste.Core.Components;
using Celeste.Core.Randomness;
using Celeste.Core.Reflection.Runtime;
using Celeste.Core.Scheduling;
using Celeste.Everest.Modules.Entities.Metadata;
using Celeste.Formats.Map.Entities.Structure;
using Celeste.Game.Content.Maps.Entities;
using Celeste.Graphics.Display.Colors;
using Celeste.Mods.JungleHelper.Modules;
using Celeste.Scene.Entities;

namespace Celeste.Mods.JungleHelper.Entities.Insects
{
    [CustomEntity("JungleHelper/Dragonfly")]
    [RuntimeTypeAlias("Celeste.Mod.JungleHelper.Entities.Dragonfly")]
    public class Dragonfly : SceneEntityState
    {
        public Dragonfly(ISourceEngineContext engine, EntityData data, Vector2 offset)
            : base(((ISourceEntitySourceDataEnvironment)engine).EntitySourceDataRegistration, data.Position + offset)
        {
            Depth = 5000;
            float frameOffset = (float)((ISourceRandomEnvironment)engine).Random.NextDouble();
            var module = JungleHelperModule.For(engine);
            var body = module.CreateReskinnableSprite(data.Attr("sprite"), "dragonfly");
            body.PlayOffset("body", frameOffset);
            OwnedComponents.Add(body);
            var wings = module.CreateReskinnableSprite(data.Attr("sprite"), "dragonfly");
            wings.PlayOffset("wings", frameOffset);
            wings.Color = SourceHexColor.HexToColor(data.Attr("wingsColor"));
            OwnedComponents.Add(wings);
        }
    }
}
