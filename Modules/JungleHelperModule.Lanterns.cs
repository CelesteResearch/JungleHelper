using System;
using System.Collections.Generic;
using System.Reflection;
using System.Xml;
using Celeste.Core.Scheduling;
using Celeste.Core.Rendering;
using Celeste.Everest.Content.Registry;
using Celeste.Everest.Modules.Runtime;
using Celeste.Formats.Graphics.Sprites;
using Celeste.Formats.Map.Entities.Structure;
using Celeste.Game.Execution.Graphics;
using Celeste.Game.Execution.Levels;
using Celeste.Graphics.Animation;
using Celeste.Graphics.Content;
using Celeste.Mods.JungleHelper.Runtime.Lanterns;
using Celeste.Scene.Tracking;
using Celeste.Simulation;
using Celeste.Simulation.Gameplay.Player.Dash;
using Celeste.Simulation.Gameplay.Player.Movement;
using Celeste.Simulation.Gameplay.Player.Presentation.Hair;
using Celeste.Simulation.Gameplay.Player.Presentation.Sprites;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mods.JungleHelper.Modules
{
    public sealed partial class JungleHelperModule
    {
        private JungleLanternLighting lanternLighting;

        public JungleLanternLighting LanternLighting =>
            lanternLighting ??= new JungleLanternLighting(SourceEngine, this);

        private readonly List<IDisposable> lanternHooks = new();
        private SpriteBank lanternSpriteBank;
        private Celeste.Simulation.Presentation.Particles.Definitions.ParticleTypePresentation lanternRegenParticle;

        internal Celeste.Simulation.Presentation.Particles.Definitions.ParticleTypePresentation LanternRegenParticle =>
            lanternRegenParticle ??= new Celeste.Simulation.Presentation.Particles.Definitions.ParticleTypePresentation(
                ((Celeste.Game.Execution.Processes.IOriginalGameplayProcess)SourceEngine).ParticleTypes.Refill.P_Regen)
            {
                Color = Celeste.Graphics.Display.Colors.SourceHexColor.HexToColor("FFB564"),
                Color2 = Celeste.Graphics.Display.Colors.SourceHexColor.HexToColor("FCFFC3")
            };

        private static readonly MethodInfo SetSpriteMode =
            typeof(PlayerSprite).GetProperty(nameof(PlayerSprite.Mode)).GetSetMethod(true);

        internal bool HasMaxHelpingHandFlagSwitches
        {
            get
            {
                foreach (var candidate in ((ISourceEverestModulesEnvironment)SourceEngine).Modules)
                    if (candidate.Metadata.Name == "MaxHelpingHand" &&
                        candidate.Metadata.Version >= new Version(1, 10, 0))
                        return true;
                return false;
            }
        }

        public override void LoadContent(bool firstLoad)
        {
            var document = new XmlDocument();
            using (var stream = ((IEverestContentRegistryEnvironment)SourceEngine).SourceContentRegistry
                   .Get("Graphics/JungleHelper/CustomSprites").Stream)
                document.Load(stream);
            lanternSpriteBank = new SpriteBank(SourceEngine,
                ((ISourceGameGraphicsEnvironment)SourceEngine).GameGraphics.GameAtlas, document);
        }

        internal Sprite CreateLanternSprite(EntityData data)
            => CreateReskinnableSprite(data.Attr("sprite"), "lantern");

        private void LoadLanternHooks()
        {
            lanternHooks.Add(new Hook(
                typeof(PlayerSprite).GetConstructor(new[] { typeof(ISourceEngineContext), typeof(PlayerSpriteMode) }),
                (Action<Action<PlayerSprite, ISourceEngineContext, PlayerSpriteMode>, PlayerSprite, ISourceEngineContext
                    , PlayerSpriteMode>)CreatePlayerSprite));
            lanternHooks.Add(new Hook(typeof(DashSequence).GetProperty(nameof(DashSequence.CanDash)).GetMethod,
                (Func<Func<DashSequence, bool>, DashSequence, bool>)CanDash));
            lanternHooks.Add(new Hook(typeof(NormalSequence).GetMethod(nameof(NormalSequence.Update)),
                (Func<Func<NormalSequence, int>, NormalSequence, int>)NormalUpdate));
            lanternHooks.Add(new Hook(typeof(SwimSequence).GetMethod(nameof(SwimSequence.Update)),
                (Func<Func<SwimSequence, int>, SwimSequence, int>)SwimUpdate));
            lanternHooks.Add(new Hook(typeof(RoomPlayerSession).GetMethod(nameof(RoomPlayerSession.OnTransition)),
                (Action<Action<RoomPlayerSession>, RoomPlayerSession>)OnPlayerTransition));
            lanternHooks.Add(new Hook(typeof(RoomPlayerSession).GetMethod(nameof(RoomPlayerSession.Removed)),
                (Action<Action<RoomPlayerSession, object>, RoomPlayerSession, object>)OnPlayerRemoved));
            lanternHooks.Add(new Hook(typeof(RoomPlayerSession).GetMethod(nameof(RoomPlayerSession.SceneEnd)),
                (Action<Action<RoomPlayerSession, object>, RoomPlayerSession, object>)OnPlayerSceneEnd));
            lanternHooks.Add(new ILHook(typeof(RoomPlayerSession).GetConstructors()[0], PatchPlayerConstructor));
            lanternHooks.Add(new ILHook(
                typeof(Celeste.Game.Execution.LevelLoader.OriginalLevelLoaderScene).GetConstructor(new[]
                {
                    typeof(ISourceEngineContext), typeof(Celeste.Simulation.Runtime.Session.ChapterCompletionSession),
                    typeof(Celeste.Game.Execution.Levels.Construction.OriginalLevelSceneConstruction),
                    typeof(Celeste.Game.Execution.LevelLoader.OriginalLevelLoadingResources),
                    typeof(Celeste.Game.Execution.Areas.IOriginalAreaDataResolver),
                    typeof(Celeste.Game.Execution.Threading.OriginalGameThreadSession),
                    typeof(Celeste.Game.Execution.LevelLoader.IInstalledLevelLoaderPolicy),
                    typeof(System.Numerics.Vector2?)
                }), PatchLevelLoaderConstructor));
            lanternHooks.Add(new ILHook(
                typeof(OriginalLevelSceneRoot).GetMethod("VariantMode", BindingFlags.Instance | BindingFlags.NonPublic),
                PatchOtherSelfVariant));
            lanternHooks.Add(new Hook(typeof(Celeste.Everest.Content.Reloading.EverestAssetReloadProcess)
                    .GetMethod("ReloadLevel", Type.EmptyTypes),
                (Action<Action<Celeste.Everest.Content.Reloading.EverestAssetReloadProcess>,
                    Celeste.Everest.Content.Reloading.EverestAssetReloadProcess>)OnReloadLevel));
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            lanternHooks.Add(new ILHook(
                typeof(PlayerHairAppearance).GetMethod(nameof(PlayerHairAppearance.UpdateColor)), PatchSpriteModes));
            lanternHooks.Add(new ILHook(typeof(RoomPlayerSession).GetMethod("EmitNormalDashParticle", flags),
                PatchSpriteModes));
            lanternHooks.Add(new ILHook(typeof(RoomPlayerSession).GetMethod("CreateTrail", flags), PatchSpriteModes));
            LoadSkinWarnings();
        }

        private void UnloadLanternHooks()
        {
            for (int i = lanternHooks.Count - 1; i >= 0; i--) lanternHooks[i].Dispose();
            lanternHooks.Clear();
            lanternLighting?.ResetPlayerInfo();
            lanternLighting = null;
            lanternSpriteBank = null;
            lanternRegenParticle = null;
            if (addedSkinControllerLoadString) recognizedLoadStrings.Remove("JungleHelper/EnforceSkinController");
            addedSkinControllerLoadString = false;
            recognizedLoadStrings = null;
            overwrittenSpriteBanks.Clear();
            spriteBankOverwritingSkinActive = false;
            showSkinWarningPostcard = false;
            skinWarningPostcard?.RemoveSelf();
            skinWarningPostcard = null;
            actualSpriteId = null;
        }

        private void CreatePlayerSprite(Action<PlayerSprite, ISourceEngineContext, PlayerSpriteMode> original,
            PlayerSprite sprite, ISourceEngineContext engine, PlayerSpriteMode mode)
        {
            PlayerSpriteMode requested = (int)mode < 0 ? (PlayerSpriteMode)unchecked((int)mode + (1 << 31)) : mode;
            bool custom = ReferenceEquals(engine, SourceEngine) && EnforceSkinController.HasLantern(requested);
            original(sprite, engine, custom ? PlayerSpriteMode.Madeline : mode);
            if (!custom) return;
            string id = requested == EnforceSkinController.MadelineLantern
                ? "junglehelper_madeline_lantern"
                : "junglehelper_badeline_lantern";
            var graphics = (OriginalGameGraphicsResources)((ISourceGameGraphicsEnvironment)engine).GameGraphics;
            graphics.SpriteFactory.CreateOn(sprite, id);
            SetSpriteMode.Invoke(sprite, new object[] { requested });
            sprite.Play("idle", true);
            sprite.OnFinish = animation =>
            {
                if (animation != "lookUp") return;
                sprite.Play("lookUp", true);
                sprite.SetAnimationFrame(5);
            };
        }

        private bool CanDash(Func<DashSequence, bool> original, DashSequence sequence) =>
            original(sequence) && (!ReferenceEquals(sequence.EngineContext, SourceEngine)
                                   || !EnforceSkinController.HasLantern(((RoomPlayerWorld)sequence.World).SourcePlayer
                                       .Sprite.Mode));

        private int NormalUpdate(Func<NormalSequence, int> original, NormalSequence sequence)
        {
            if (ReferenceEquals(sequence.EngineContext, SourceEngine))
            {
                var player = ((RoomPlayerWorld)sequence.World).SourcePlayer;
                if (EnforceSkinController.HasLantern(player.Sprite.Mode))
                {
                    LanternLighting.DropTimer -= SourceEngine.DeltaTime;
                    if (player.Input.GrabCheck && player.Input.MoveY.Value > 0 && LanternLighting.DropTimer <= 0f)
                        LanternLighting.DropLantern(player, false);
                }
            }

            return original(sequence);
        }

        private int SwimUpdate(Func<SwimSequence, int> original, SwimSequence sequence)
        {
            if (ReferenceEquals(sequence.EngineContext, SourceEngine))
            {
                var player = ((RoomPlayerWorld)sequence.World).SourcePlayer;
                if (EnforceSkinController.HasLantern(player.Sprite.Mode)) LanternLighting.DropLantern(player, true);
            }

            return original(sequence);
        }

        private bool OwnsPlayer(RoomPlayerSession player, object scene = null) =>
            (scene ?? player.Scene) is OriginalLevelSceneRoot level &&
            ReferenceEquals(level.EngineContext, SourceEngine);

        private void OnPlayerTransition(Action<RoomPlayerSession> original, RoomPlayerSession player)
        {
            original(player);
            if (OwnsPlayer(player)) LanternLighting.DoRespawn = false;
        }

        private void OnPlayerRemoved(Action<RoomPlayerSession, object> original, RoomPlayerSession player, object scene)
        {
            original(player, scene);
            if (OwnsPlayer(player, scene)) LanternLighting.ResetPlayerInfo();
        }

        private void OnPlayerSceneEnd(Action<RoomPlayerSession, object> original, RoomPlayerSession player,
            object scene)
        {
            original(player, scene);
            if (OwnsPlayer(player, scene)) LanternLighting.ResetPlayerInfo();
        }

        private void OnReloadLevel(Action<Celeste.Everest.Content.Reloading.EverestAssetReloadProcess> original,
            Celeste.Everest.Content.Reloading.EverestAssetReloadProcess reload)
        {
            if (ReferenceEquals(reload,
                    ((Celeste.Everest.Content.Reloading.IEverestAssetReloadEnvironment)SourceEngine).AssetReload))
            {
                var level = SourceEngine.Frame.Scenes.CurrentScene as OriginalLevelSceneRoot;
                if (level == null &&
                    SourceEngine.Frame.Scenes.CurrentScene is Celeste.Everest.Content.Reloading.EverestAssetReloadScene)
                    level = reload.ReturnToScene as OriginalLevelSceneRoot;
                var player = level?.SourceScene.Entities.GetTrackedEntity<RoomPlayerSession>();
                if (player?.Sprite != null && EnforceSkinController.HasLantern(player.Sprite.Mode))
                    LanternLighting.DropLantern(player, false);
            }

            original(reload);
        }

        private void PatchPlayerConstructor(ILContext context)
        {
            var cursor = new ILCursor(context);
            while (cursor.TryGotoNext(MoveType.Before, instruction => instruction.OpCode == OpCodes.Ret))
            {
                cursor.Emit(OpCodes.Ldarg_0);
                cursor.Emit(OpCodes.Ldarg, 4);
                cursor.EmitDelegate<Action<RoomPlayerSession, ISourceEngineContext>>((player, engine) =>
                {
                    if (!ReferenceEquals(engine, SourceEngine)) return;
                    player.Sprite.OnLastFrame += animation =>
                    {
                        if (EnforceSkinController.HasLantern(player.Sprite.Mode) &&
                            player.Sprite.LastAnimationID.StartsWith("idle") &&
                            player.Sprite.LastAnimationID != "idle_carry")
                            player.Sprite.Play("idle");
                    };
                });
                cursor.Index++;
            }
        }

        private void PatchLevelLoaderConstructor(ILContext context)
        {
            var cursor = new ILCursor(context);
            while (cursor.TryGotoNext(MoveType.Before, instruction => instruction.OpCode == OpCodes.Ret))
            {
                cursor.Emit(OpCodes.Ldarg_1);
                cursor.EmitDelegate<Action<ISourceEngineContext>>(engine =>
                {
                    if (!ReferenceEquals(engine, SourceEngine)) return;
                    var graphics = (OriginalGameGraphicsResources)((ISourceGameGraphicsEnvironment)engine).GameGraphics;
                    graphics.CreateFramesMetadata("junglehelper_madeline_lantern");
                    graphics.CreateFramesMetadata("junglehelper_badeline_lantern");
                });
                cursor.Index++;
            }
        }

        private void PatchOtherSelfVariant(ILContext context)
        {
            var cursor = new ILCursor(context);
            if (!cursor.TryGotoNext(instruction => instruction.MatchLdstr("MENU_VARIANT_PLAYASBADELINE")))
                throw new InvalidOperationException("Other Self menu callback was not found.");
            if (!cursor.TryGotoNext(MoveType.Before,
                    instruction => instruction.Operand is Mono.Cecil.MethodReference method && method.Name == "Change"))
                throw new InvalidOperationException("Other Self Change callback was not found.");
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate<Func<Action<bool>, OriginalLevelSceneRoot, Action<bool>>>((original, level) =>
            {
                if (!ReferenceEquals(level.EngineContext, SourceEngine)) return original;
                return on =>
                {
                    var player = level.SourceScene.Entities.GetTrackedEntity<RoomPlayerSession>();
                    bool hasLantern = player != null && EnforceSkinController.HasLantern(player.Sprite.Mode);
                    original(on);
                    if (hasLantern) EnforceSkinController.ChangePlayerSpriteMode(SourceEngine, player, true);
                };
            });
        }

        private static void PatchSpriteModes(ILContext context)
        {
            var cursor = new ILCursor(context);
            while (cursor.TryGotoNext(MoveType.After,
                       instruction => instruction.MatchCallvirt<PlayerSprite>("get_Mode")))
                cursor.EmitDelegate<Func<PlayerSpriteMode, PlayerSpriteMode>>(
                    EnforceSkinController.MasqueradeAsBadeline);
        }
    }
}
