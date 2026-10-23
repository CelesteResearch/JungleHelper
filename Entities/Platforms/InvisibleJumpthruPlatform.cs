using System.Linq;
using System.Numerics;
using Celeste.Everest.Modules.Entities.Metadata;
using Celeste.Game.Content.Maps.Entities;
using Celeste.Game.Content.Maps.Levels;
using Celeste.Game.Execution.Levels;
using Celeste.Game.Gameplay.Platforms.Jumpthrus;
using Celeste.Graphics.Animation;
using Celeste.Simulation.Gameplay.Platforms.Collision;
using Celeste.Simulation.Gameplay.Platforms.Foundation;

namespace Celeste.Mods.JungleHelper.Entities.Platforms
{
    [CustomEntity("JungleHelper/InvisibleJumpthruPlatform = Load")]
    [global::Celeste.Core.Reflection.Runtime.RuntimeTypeAlias(
        "Celeste.Mod.JungleHelper.Entities.InvisibleJumpthruPlatform")]
    public class InvisibleJumpthruPlatform : JumpthruPlatformState
    {
        public static InvisibleJumpthruPlatform Load(OriginalLevelSceneRoot level, OriginalLevelRoomData room,
            Vector2 offset, OriginalLevelEntityData data)
        {
            data.Values["texture"] = "JungleHelper/Invisible";
            return new InvisibleJumpthruPlatform(data, offset,
                ((ISourcePlatformCollisionScene)level).SolidWorldFactory.CreateJumpThru(data.Position + offset, true));
        }

        public InvisibleJumpthruPlatform(OriginalLevelEntityData data, Vector2 offset, ISourceSolidWorld world) : base(
            data, offset, world)
        {
        }

        public override void Awake(object scene)
        {
            base.Awake(scene);
            foreach (var image in Components.OfType<SpriteImage>().ToArray()) Remove(image);
        }
    }
}
