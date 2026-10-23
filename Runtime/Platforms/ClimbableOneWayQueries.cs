using System.Numerics;
using Celeste.Core.Components;
using Celeste.Mods.JungleHelper.Entities.Platforms;
using Celeste.Physics.Collision;
using Celeste.Scene.Tracking;
using Celeste.Simulation;

namespace Celeste.Mods.JungleHelper.Runtime.Platforms
{
    internal static class ClimbableOneWayQueries
    {
        public static ClimbableOneWayPlatform FirstOutside(ISourceEntityCollider mover, object scene, Vector2 at,
            bool leftToRight)
        {
            if (scene == null) return null;
            foreach (ClimbableOneWayPlatform other in ((ISourceScene)scene).SourceScene.Entities.GetTrackedEntities(
                         typeof(ClimbableOneWayPlatform)))
                if (other.AllowLeftToRight == leftToRight && !SourceEntityCollision.Check(mover, other) &&
                    SourceEntityCollision.CheckAt(mover, other, at))
                    return other;
            return null;
        }

        public static ClimbableOneWayPlatform FacingPlatform(RoomPlayerSession player)
            => FirstOutside(player, player.Scene, player.Position + new Vector2(player.Facing * 2, 0f),
                player.Facing == -1);

        public static bool EntityContact(bool original, RoomPlayerSession player, Vector2 offset, bool wallJump = false)
        {
            if (original) return true;
            if (offset.X == 0f || wallJump && !player.Input.GrabCheck) return false;
            var platform = FirstOutside(player, player.Scene, player.Position + offset, offset.X < 0f);
            return platform != null &&
                   platform.Collider.AbsoluteBottom >= player.Collider.AbsoluteTop + offset.Y + 3f &&
                   (!wallJump || platform.AllowWallJumping);
        }

        public static bool PointContact(bool original, object scene, Vector2 point)
        {
            if (original) return true;
            if (scene == null) return false;
            foreach (ClimbableOneWayPlatform other in ((ISourceScene)scene).SourceScene.Entities.GetTrackedEntities(
                         typeof(ClimbableOneWayPlatform)))
                if (other.Collidable && other.Collider.Collide(point))
                    return true;
            return false;
        }
    }
}
