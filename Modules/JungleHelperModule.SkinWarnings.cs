using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Xml;
using Celeste.Core.Scheduling;
using Celeste.Everest.Content.Graphics;
using Celeste.Everest.Content.Registry;
using Celeste.Everest.Diagnostics.Logging;
using Celeste.Everest.Runtime.Startup;
using Celeste.Game.Chapters.Flow;
using Celeste.Game.Content.Dialogs;
using Celeste.Game.Content.Maps;
using Celeste.Game.Entities.Postcards;
using Celeste.Game.Execution.Areas;
using Celeste.Game.Presentation.Environment;
using Celeste.Graphics.Animation;
using Celeste.Graphics.Content;
using Celeste.Mods.JungleHelper.Runtime.Lanterns;
using Celeste.Scene.Scenes;
using Celeste.Simulation.Gameplay.Player.Presentation.Sprites;
using Celeste.Simulation.Runtime.Session;
using MonoMod.RuntimeDetour;

namespace Celeste.Mods.JungleHelper.Modules
{
    public sealed partial class JungleHelperModule
    {
        private readonly HashSet<string> overwrittenSpriteBanks = new();
        private bool spriteBankOverwritingSkinActive;
        private bool showSkinWarningPostcard;
        private PostcardState skinWarningPostcard;
        private string actualSpriteId;
        private HashSet<string> recognizedLoadStrings;
        private bool addedSkinControllerLoadString;

        private void LoadSkinWarnings()
        {
            var entityLoads =
                ((Celeste.Everest.Runtime.Levels.Loading.Entities.IEverestLevelEntityLoadProcessEnvironment)
                    SourceEngine).EntityLoadProcess;
            recognizedLoadStrings =
                (HashSet<string>)typeof(Celeste.Everest.Runtime.Levels.Loading.Entities.EverestLevelEntityLoadProcess)
                    .GetField("loadStrings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(entityLoads);
            addedSkinControllerLoadString = recognizedLoadStrings.Add("JungleHelper/EnforceSkinController");
            const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
            lanternHooks.Add(new Hook(
                typeof(EverestSpriteBankXmlFilter).GetMethod(nameof(EverestSpriteBankXmlFilter.FilterVanillaCopies)),
                (Func<Func<XmlDocument, XmlDocument, string, EverestLoggerProcess, XmlDocument>, XmlDocument,
                    XmlDocument, string, EverestLoggerProcess, XmlDocument>)DetectSpriteBankOverwrites));
            lanternHooks.Add(new Hook(typeof(EverestGameLoaderScene).GetMethod("GetNextScene", hidden),
                (Func<Func<EverestGameLoaderScene, HiresSnowState, SourceSceneState>, EverestGameLoaderScene,
                    HiresSnowState, SourceSceneState>)CommitSpriteBankOverwrites));
            lanternHooks.Add(new Hook(typeof(LevelEnterSequence).GetMethod(nameof(LevelEnterSequence.Go)),
                (Action<Action<ChapterCompletionSession, bool, IOriginalLevelEnterProcess>, ChapterCompletionSession,
                    bool, IOriginalLevelEnterProcess>)OnLevelEnter));
            lanternHooks.Add(new Hook(typeof(LevelEnterSequence).GetMethod("Routine", hidden),
                (Func<Func<LevelEnterSequence, IEnumerator>, LevelEnterSequence, IEnumerator>)AddSkinWarningPostcard));
            lanternHooks.Add(new Hook(typeof(LevelEnterSequence).GetMethod(nameof(LevelEnterSequence.BeforeRender)),
                (Action<Action<LevelEnterSequence>, LevelEnterSequence>)RenderSkinWarningPostcard));
            using (new DetourConfigContext(new DetourConfig("JungleHelper_BeforeAll").WithPriority(int.MinValue)).Use())
                lanternHooks.Add(new Hook(
                    typeof(SpriteTemplateCollection).GetMethod(nameof(SpriteTemplateCollection.CreateOn)),
                    (Func<Func<SpriteTemplateCollection, Sprite, string, Sprite>, SpriteTemplateCollection, Sprite,
                        string, Sprite>)OnSpriteBankCreateOn));
        }

        private XmlDocument DetectSpriteBankOverwrites(
            Func<XmlDocument, XmlDocument, string, EverestLoggerProcess, XmlDocument> original,
            XmlDocument vanilla, XmlDocument mod, string path, EverestLoggerProcess logger)
        {
            var result = original(vanilla, mod, path, logger);
            if (ReferenceEquals(logger, ((IEverestLoggerProcessEnvironment)SourceEngine).Logger))
                foreach (XmlNode node in result["Sprites"].ChildNodes)
                    if (node is XmlElement)
                        overwrittenSpriteBanks.Add(node.Name);
            return result;
        }

        private SourceSceneState CommitSpriteBankOverwrites(
            Func<EverestGameLoaderScene, HiresSnowState, SourceSceneState> original,
            EverestGameLoaderScene loader, HiresSnowState snow)
        {
            if (ReferenceEquals(loader.EngineContext, SourceEngine))
                spriteBankOverwritingSkinActive = (overwrittenSpriteBanks.Contains("player") ||
                                                   overwrittenSpriteBanks.Contains("player_no_backpack") ||
                                                   overwrittenSpriteBanks.Contains("player_badeline"))
                                                  && !(overwrittenSpriteBanks.Contains(
                                                           "junglehelper_madeline_lantern") ||
                                                       overwrittenSpriteBanks.Contains(
                                                           "junglehelper_badeline_lantern"));
            return original(loader, snow);
        }

        private Sprite OnSpriteBankCreateOn(Func<SpriteTemplateCollection, Sprite, string, Sprite> original,
            SpriteTemplateCollection bank, Sprite sprite, string id)
        {
            if (ReferenceEquals(bank, ((ISourceGameGraphicsEnvironment)SourceEngine).GameGraphics.SpriteFactory))
                actualSpriteId = id;
            return original(bank, sprite, id);
        }

        private string GetActualAppliedSpriteXml(PlayerSpriteMode mode)
        {
            _ = new PlayerSprite(SourceEngine, mode);
            return actualSpriteId;
        }

        private void OnLevelEnter(Action<ChapterCompletionSession, bool, IOriginalLevelEnterProcess> original,
            ChapterCompletionSession session, bool fromSaveData, IOriginalLevelEnterProcess process)
        {
            if (ReferenceEquals(process, SourceEngine)) CheckForSkins(session);
            original(session, fromSaveData, process);
        }

        private void CheckForSkins(ChapterCompletionSession session)
        {
            var keys = ((IOriginalAreaKeyEnvironment)SourceEngine).AreaKeys;
            var area = keys.GetAreaById(session.AreaID);
            if (area == null || area.Mode.Length <= session.Mode || area.Mode[session.Mode] == null) return;
            bool found = false;
            foreach (var room in ((OriginalChapterMapData)session.MapData).Levels)
            {
                foreach (var entity in room.Entities)
                    if (entity.Name == "JungleHelper/EnforceSkinController")
                    {
                        found = true;
                        break;
                    }

                if (found) break;
            }

            if (!found) return;
            if (spriteBankOverwritingSkinActive)
            {
                showSkinWarningPostcard = true;
                return;
            }

            bool textureOverwritingSkin = false;
            bool lanternOverwritten = false;
            foreach (var pair in ((IEverestContentRegistryEnvironment)SourceEngine).SourceContentRegistry.Map)
            {
                if (EnforceSkinVanillaSpriteDump.VanillaPlayerSprites.Contains(pair.Key)) textureOverwritingSkin = true;
                if (pair.Key.StartsWith("Graphics/Atlases/Gameplay/JungleHelper/Lantern/") &&
                    pair.Value.Source?.Mod != null &&
                    pair.Value.Source.Mod.Name != "JungleHelper") lanternOverwritten = true;
            }

            if (textureOverwritingSkin && !lanternOverwritten)
            {
                showSkinWarningPostcard = true;
                return;
            }

            bool maddyOverwritten = GetActualAppliedSpriteXml(PlayerSpriteMode.Madeline) != "player"
                                    || GetActualAppliedSpriteXml(PlayerSpriteMode.MadelineNoBackpack) !=
                                    "player_no_backpack"
                                    || GetActualAppliedSpriteXml(PlayerSpriteMode.MadelineAsBadeline) !=
                                    "player_badeline";
            bool maddyLanternOverwritten = GetActualAppliedSpriteXml(EnforceSkinController.MadelineLantern) !=
                                           "junglehelper_madeline_lantern"
                                           || GetActualAppliedSpriteXml(EnforceSkinController.BadelineLantern) !=
                                           "junglehelper_badeline_lantern";
            if (maddyOverwritten && !maddyLanternOverwritten) showSkinWarningPostcard = true;
        }

        private IEnumerator AddSkinWarningPostcard(Func<LevelEnterSequence, IEnumerator> original,
            LevelEnterSequence scene)
        {
            if (!ReferenceEquals(scene.EngineContext, SourceEngine)) return original(scene);
            return SkinWarningRoutine(scene, original);
        }

        private IEnumerator SkinWarningRoutine(LevelEnterSequence scene, Func<LevelEnterSequence, IEnumerator> original)
        {
            if (showSkinWarningPostcard)
            {
                showSkinWarningPostcard = false;
                skinWarningPostcard = new PostcardState(SourceEngine,
                    ((IOriginalGameTextEnvironment)SourceEngine).Text.Dialog.Get("JUNGLEHELPER_POSTCARD_SKINWARNING"),
                    "event:/ui/main/postcard_csides_in", "event:/ui/main/postcard_csides_out");
                scene.SourceScene.AddEntity(skinWarningPostcard);
                yield return skinWarningPostcard.DisplayRoutine();
                skinWarningPostcard = null;
            }

            yield return new SourceSwapImmediately(original(scene));
        }

        private void RenderSkinWarningPostcard(Action<LevelEnterSequence> original, LevelEnterSequence scene)
        {
            original(scene);
            if (ReferenceEquals(scene.EngineContext, SourceEngine)) skinWarningPostcard?.BeforeRender();
        }
    }
}
