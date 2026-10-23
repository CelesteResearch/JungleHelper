using System;
using System.Reflection;
using Celeste.Core.Scheduling;
using Celeste.Everest.Modules.Runtime;
using Celeste.Game.Execution.Levels;
using Celeste.Mods.JungleHelper.Runtime.Moss;
using Celeste.Scene.Entities;
using Celeste.Scene.Tracking;
using MonoMod.RuntimeDetour;

namespace Celeste.Mods.JungleHelper.Modules
{
    public sealed partial class JungleHelperModule : SourceEverestModule
    {
        private IDisposable mossHook;
        private Runtime.Platforms.ClimbableOneWayHooks climbableOneWayHooks;

        public JungleHelperModule(ISourceEngineContext engine) : base(engine)
        {
        }

        public override void Load()
        {
            try
            {
                climbableOneWayHooks = new Runtime.Platforms.ClimbableOneWayHooks(SourceEngine);
                mossHook = new Hook(typeof(OriginalLevelSceneRoot).GetMethod(nameof(OriginalLevelSceneRoot.Update)),
                    (Action<Action<OriginalLevelSceneRoot>, OriginalLevelSceneRoot>)OnLevelUpdate);
                LoadLanternHooks();
            }
            catch
            {
                climbableOneWayHooks?.Dispose();
                climbableOneWayHooks = null;
                UnloadLanternHooks();
                mossHook?.Dispose();
                mossHook = null;
                throw;
            }
        }

        public override void Unload()
        {
            climbableOneWayHooks?.Dispose();
            climbableOneWayHooks = null;
            UnloadLanternHooks();
            mossHook?.Dispose();
            mossHook = null;
        }

        private void OnLevelUpdate(Action<OriginalLevelSceneRoot> orig, OriginalLevelSceneRoot level)
        {
            if (ReferenceEquals(level.EngineContext, SourceEngine))
            {
                var tracker = ((ISourceScene)level).SourceScene.Components;
                var components = tracker.GetTrackedComponents(tracker.Catalog.Find(typeof(MossPreUpdateHook)).Name);
                foreach (MossPreUpdateHook component in components)
                    if (((SceneEntityState)component.Entity).Active)
                        component.OnPreUpdate();
            }

            orig(level);
        }
    }
}
