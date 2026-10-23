using System;
using Celeste.Core.Components;
using Celeste.Scene.Tracking.Attributes;

namespace Celeste.Mods.JungleHelper.Runtime.Moss
{
    [SceneTracked]
    public sealed class MossPreUpdateHook : SourceComponentState
    {
        public Action OnPreUpdate { get; }

        public MossPreUpdateHook(Action onPreUpdate) : base(false, false)
        {
            OnPreUpdate = onPreUpdate;
        }
    }
}
