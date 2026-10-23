using Celeste.Graphics.Content;
using System.Collections;
using System.Numerics;
using Celeste.Core.Components;
using Celeste.Core.Mathematics.Easing;
using Celeste.Core.Mathematics.Extrema;
using Celeste.Core.Reflection.Runtime;
using Celeste.Core.Scheduling;
using Celeste.Everest.Modules.Entities.Metadata;
using Celeste.Formats.Map.Entities.Attributes;
using Celeste.Formats.Map.Entities.Structure;
using Celeste.Game.Content.Maps.Entities;
using Celeste.Game.Content.Maps.Levels;
using Celeste.Game.Execution.Graphics;
using Celeste.Game.Execution.Levels;
using Celeste.Mods.JungleHelper.Entities.UI;
using Celeste.Scene.Entities;
using Celeste.Scene.Tracking;
using Celeste.Simulation;
using Celeste.Simulation.Gameplay.Triggers.Foundation;

namespace Celeste.Mods.JungleHelper.Triggers.UI
{
    [CustomEntity("JungleHelper/UIImageTrigger")]
    [RuntimeTypeAlias("Celeste.Mod.JungleHelper.Triggers.UIImageTrigger")]
    public sealed class UIImageTrigger : SourceTriggerEntityState
    {
        private readonly ISourceEngineContext engine;
        private readonly UIImage img;
        private readonly float fadeIn;
        private readonly float fadeOut;
        private readonly string flag;
        private SimulationCoroutine fader;
        private bool flagWasActive;

        public static SceneEntityState Load(OriginalLevelSceneRoot level, OriginalLevelRoomData room, Vector2 offset,
            OriginalLevelEntityData data) => new UIImageTrigger(level.EngineContext, data, offset);

        public UIImageTrigger(ISourceEngineContext engine, EntityData data, Vector2 offset) : base(
            ((ISourceEntitySourceDataEnvironment)engine).EntitySourceDataRegistration, data.Position + offset,
            data.Width, data.Height, ((ISourceExtremaEnvironment)engine).ExtremaArithmetic)
        {
            this.engine = engine;
            fader = new SimulationCoroutine(engine);
            var imageOffset = new Vector2(EntityAttributeReader.Float(data.Values, "ImageX"),
                EntityAttributeReader.Float(data.Values, "ImageY"));
            fadeIn = EntityAttributeReader.Float(data.Values, "FadeIn", 1);
            fadeOut = EntityAttributeReader.Float(data.Values, "FadeOut", 1);
            flag = EntityAttributeReader.Attr(data.Values, "Flag", "");
            img = new UIImage(engine, imageOffset,
                ((OriginalGameGraphicsResources)((ISourceGameGraphicsEnvironment)engine).GameGraphics).GuiAtlas[
                    EntityAttributeReader.Attr(data.Values, "ImagePath", "")]);
        }

        public override void Awake(object scene)
        {
            ((ISourceScene)scene).SourceScene.AddEntity(img);
            base.Awake(scene);
        }

        public override void OnEnter(RoomPlayerSession player)
        {
            if (((OriginalLevelSceneRoot)Scene).Session.GetFlag(flag) || flag == "")
            {
                fader.RemoveSelf();
                OwnedComponents.Add(fader = new SimulationCoroutine(engine, MakeImageAppear()));
            }

            base.OnEnter(player);
        }

        public override void OnLeave(RoomPlayerSession player)
        {
            if (((OriginalLevelSceneRoot)Scene).Session.GetFlag(flag) || flag == "")
            {
                fader.RemoveSelf();
                OwnedComponents.Add(fader = new SimulationCoroutine(engine, MakeImageDisappear()));
            }

            base.OnLeave(player);
        }

        private IEnumerator MakeImageAppear()
        {
            img.Displayed = true;
            for (float t = 0; t < 1; t += engine.RawDeltaTime / fadeIn)
            {
                img.Alpha = ((ISourceEasingEnvironment)engine).EasingArithmetic.CubeOut(t);
                yield return null;
            }

            img.Alpha = 1;
        }

        private IEnumerator MakeImageDisappear()
        {
            img.Displayed = false;
            for (float t = 0; t < 1; t += engine.RawDeltaTime / fadeOut)
            {
                img.Alpha = ((ISourceEasingEnvironment)engine).EasingArithmetic.CubeIn(1 - t);
                yield return null;
            }

            img.Alpha = 0;
        }

        public override void Update()
        {
            if (flag != "" && flagWasActive != ((OriginalLevelSceneRoot)Scene).Session.GetFlag(flag))
            {
                if (PlayerIsInside && img.Alpha < 0.1)
                {
                    fader.RemoveSelf();
                    OwnedComponents.Add(fader = new SimulationCoroutine(engine, MakeImageAppear()));
                }
                else if (PlayerIsInside && img.Alpha > 0.9)
                {
                    fader.RemoveSelf();
                    OwnedComponents.Add(fader = new SimulationCoroutine(engine, MakeImageDisappear()));
                }

                flagWasActive = ((OriginalLevelSceneRoot)Scene).Session.GetFlag(flag);
            }

            base.Update();
        }
    }
}
