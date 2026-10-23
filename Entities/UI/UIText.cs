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
using Celeste.Game.Content.Dialogs;
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
    [RuntimeTypeAlias("Celeste.Mod.JungleHelper.Triggers.UIText")]
    public sealed class UIText : SceneEntityState
    {
        private readonly ISourceEngineContext engine;
        private readonly VirtualRenderTarget textTarget;
        private string text;
        private bool newlineApplied;
        private readonly Vector2 textOffset;
        private bool disposed;
        public float Alpha;
        public bool Displayed;

        public UIText(ISourceEngineContext engine, Vector2 textOffset, string text) : base(
            ((ISourceEntitySourceDataEnvironment)engine).EntitySourceDataRegistration)
        {
            this.engine = engine;
            var graphics = ((IOriginalEngineGraphicsEnvironment)engine).EngineGraphics;
            textTarget = ((ISourceVirtualContentEnvironment)engine).VirtualContent.CreateRenderTarget("text",
                Math.Min(1920, graphics.ViewWidth), Math.Min(1080, graphics.ViewHeight));
            this.text = text;
            this.textOffset = textOffset;
            var tags = ((IOriginalGameTagsEnvironment)engine).Tags;
            Tags = tags.HUD.Value | tags.FrozenUpdate.Value;
            OwnedComponents.Add(new BeforeRenderCallbackComponentState(BeforeRender));
        }

        public void BeforeRender()
        {
            if (!disposed)
            {
                var device = ((ISourceGraphicsDeviceEnvironment)engine).GraphicsDevice;
                device.SetRenderTarget(textTarget.Target);
                device.Clear(PackedColor.Transparent);
                var context = ((ISourceRenderEnvironment)engine).CurrentContext;
                var batch = ((ISourceDrawScope)context).Draw.SpriteBatch;
                batch.Begin(new SourceRenderBatchState(SourceSpriteSortMode.Deferred, SourceBlendState.AlphaBlend,
                    SourceSamplerState.LinearClamp, null, null, null,
                    Matrix4x4.CreateScale(textTarget.Width / 1920f)));
                if (!string.IsNullOrEmpty(text))
                {
                    DrawText(context, new Vector2(-2, 0), PackedColor.Black);
                    DrawText(context, new Vector2(2, 0), PackedColor.Black);
                    DrawText(context, new Vector2(0, -2), PackedColor.Black);
                    DrawText(context, new Vector2(0, 2), PackedColor.Black);
                    DrawText(context, Vector2.Zero, PackedColor.White);
                }

                batch.End();
            }
        }

        private void DrawText(ISourceRenderContext context, Vector2 offset, PackedColor color)
        {
            if (!((OriginalLevelSceneRoot)Scene).FrozenOrPaused)
            {
                var font = ((IOriginalGameTextEnvironment)engine).Text.ActiveFont;
                if (text != null && !newlineApplied)
                {
                    text = font.Font.Get(font.BaseSize).AutoNewline(text, 1024);
                    newlineApplied = true;
                }

                font.Draw(context, this, text, new Vector2(960, 540) + offset + textOffset,
                    new Vector2(.5f, .5f), Vector2.One * 1.5f, color);
            }
        }

        private void Dispose()
        {
            if (!disposed)
            {
                textTarget.Dispose();
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
            ((ISourceDrawScope)context).Draw.SpriteBatch.Draw(textTarget.Target, Vector2.Zero, textTarget.Bounds,
                (PackedColor.White * Alpha).PackedValue, 0, Vector2.Zero, 1920f / textTarget.Width,
                SourceSpriteEffects.None, 0);
        }
    }
}
