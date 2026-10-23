using System;
using Celeste.Core.Scheduling;
using Celeste.Everest.Modules.Runtime;
using Celeste.Graphics.Animation;
using Celeste.Graphics.Content;

namespace Celeste.Mods.JungleHelper.Modules
{
    public sealed partial class JungleHelperModule
    {
        internal static JungleHelperModule For(ISourceEngineContext engine)
        {
            foreach (var candidate in ((ISourceEverestModulesEnvironment)engine).Modules)
                if (candidate is JungleHelperModule module)
                    return module;
            throw new InvalidOperationException("JungleHelper is not loaded for this engine.");
        }

        internal Sprite CreateReskinnableSprite(string reskinName, string defaultSpriteName)
            => string.IsNullOrEmpty(reskinName)
                ? lanternSpriteBank.Create(defaultSpriteName)
                : ((ISourceGameGraphicsEnvironment)SourceEngine).GameGraphics.SpriteFactory.Create(reskinName);
    }
}
