using System.Runtime.CompilerServices;
using Celeste.Core.Scheduling;
using Celeste.Game.Execution.Processes;
using Celeste.Simulation;
using Celeste.Simulation.Gameplay.Player.Presentation.Sprites;

namespace Celeste.Mods.JungleHelper.Runtime.Lanterns
{
    [Celeste.Core.Reflection.Runtime.RuntimeTypeAlias("Celeste.Mod.JungleHelper.Entities.EnforceSkinController")]
    [Celeste.Everest.Modules.Entities.Metadata.CustomEntity("JungleHelper/EnforceSkinController = Load")]
    public static class EnforceSkinController
    {
        public static Celeste.Scene.Entities.SceneEntityState Load(
            Celeste.Game.Execution.Levels.OriginalLevelSceneRoot level,
            Celeste.Game.Content.Maps.Levels.OriginalLevelRoomData room, System.Numerics.Vector2 offset,
            Celeste.Game.Content.Maps.Entities.OriginalLevelEntityData data) => null;

        public const PlayerSpriteMode MadelineLantern = (PlayerSpriteMode)444482;
        public const PlayerSpriteMode BadelineLantern = (PlayerSpriteMode)444483;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool HasLantern(PlayerSpriteMode mode) => mode == MadelineLantern || mode == BadelineLantern;

        internal static PlayerSpriteMode MasqueradeAsBadeline(PlayerSpriteMode mode) =>
            mode == BadelineLantern ? PlayerSpriteMode.MadelineAsBadeline : mode;

        public static void ChangePlayerSpriteMode(ISourceEngineContext engine, RoomPlayerSession player,
            bool hasLantern)
        {
            bool badeline = ((IOriginalGameplayProcess)engine).SaveContext.CurrentSave.Assists.PlayAsBadeline;
            PlayerSpriteMode mode = hasLantern
                ? badeline ? BadelineLantern : MadelineLantern
                : badeline
                    ? PlayerSpriteMode.MadelineAsBadeline
                    : player.DefaultSpriteMode;
            if (player.Active) player.ResetSpriteNextFrame(mode);
            else player.ResetSprite(mode);
        }
    }
}
