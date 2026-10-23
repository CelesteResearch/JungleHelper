using System;
using System.Numerics;
using Celeste.Core.Components;
using Celeste.Core.Components.Rendering;
using Celeste.Core.Display;
using Celeste.Core.Reflection.Runtime;
using Celeste.Core.Rendering;
using Celeste.Core.Rendering.States;
using Celeste.Core.Scheduling;
using Celeste.Game.Execution.Engine;
using Celeste.Game.Execution.Levels;
using Celeste.Game.Execution.Startup;
using Celeste.Graphics.Content;
using Celeste.Graphics.Content.Atlas;
using Celeste.Graphics.Content.VirtualAssets;
using Celeste.Graphics.Rendering;
using Celeste.Graphics.Rendering.Devices;
using Celeste.Graphics.Rendering.Drawing;
using Celeste.Scene.Entities;

namespace Celeste.Mods.JungleHelper.Entities.UI
{
    [RuntimeTypeAlias("Celeste.Mod.JungleHelper.Triggers.UIImage")]
    public sealed class UIImage : SceneEntityState
    {
        private readonly ISourceEngineContext engine;
        private readonly VirtualRenderTarget imageTarget;
        private readonly AtlasEntry image;
        private readonly Vector2 imageOffset;
        private bool disposed;
        public float Alpha;
        public bool Displayed;

        public UIImage(ISourceEngineContext engine, Vector2 imageOffset, AtlasEntry image) : base(
            ((ISourceEntitySourceDataEnvironment)engine).EntitySourceDataRegistration)
        {
            this.engine = engine;
            var graphics = ((IOriginalEngineGraphicsEnvironment)engine).EngineGraphics;
            imageTarget = ((ISourceVirtualContentEnvironment)engine).VirtualContent.CreateRenderTarget("text",
                Math.Min(1920, graphics.ViewWidth), Math.Min(1080, graphics.ViewHeight));
            this.image = image;
            this.imageOffset = imageOffset;
            var tags = ((IOriginalGameTagsEnvironment)engine).Tags;
            Tags = tags.HUD.Value | tags.FrozenUpdate.Value;
            OwnedComponents.Add(new BeforeRenderCallbackComponentState(BeforeRender));
        }

        public void BeforeRender()
        {
            if (!disposed)
            {
                var device = ((ISourceGraphicsDeviceEnvironment)engine).GraphicsDevice;
                device.SetRenderTarget(imageTarget.Target);
                device.Clear(PackedColor.Transparent);
                var context = ((ISourceRenderEnvironment)engine).CurrentContext;
                var batch = ((ISourceDrawScope)context).Draw.SpriteBatch;
                batch.Begin(new SourceRenderBatchState(SourceSpriteSortMode.Deferred, SourceBlendState.AlphaBlend,
                    SourceSamplerState.LinearClamp, null, null, null,
                    Matrix4x4.CreateScale(imageTarget.Width / 1920f)));
                if (!((OriginalLevelSceneRoot)Scene).FrozenOrPaused)
                    SourceAtlasDrawing.Draw(context, this, image, imageOffset, Vector2.Zero, PackedColor.White);
                batch.End();
            }
        }

        private void Dispose()
        {
            if (!disposed)
            {
                imageTarget.Dispose();
                RemoveSelf();
                disposed = true;
            }
        }

        public override void Removed(object scene)
        {
            base.Removed(scene);
            Dispose();
        }

        public override void SceneEnd(object scene)
        {
            base.SceneEnd(scene);
            Dispose();
        }

        public override void Render(ISourceRenderContext context)
        {
            ((ISourceDrawScope)context).Draw.SpriteBatch.Draw(imageTarget.Target, Vector2.Zero, imageTarget.Bounds,
                (PackedColor.White * Alpha).PackedValue, 0, Vector2.Zero, 1920f / imageTarget.Width,
                SourceSpriteEffects.None, 0);
        }
    }
}
