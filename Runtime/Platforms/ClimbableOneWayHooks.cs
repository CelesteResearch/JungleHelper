using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Celeste.Core.Scheduling;
using Celeste.Game.Execution.LevelLoader;
using Celeste.Game.Menus.Overworld;
using Celeste.Mods.JungleHelper.Configuration;
using Celeste.Mods.JungleHelper.Entities.Platforms;
using Celeste.Physics.Collision;
using Celeste.Scene.Tracking;
using Celeste.Simulation;
using Celeste.Simulation.Gameplay.Collision.Directional;
using Celeste.Simulation.Gameplay.Platforms.Audio;
using Celeste.Simulation.Gameplay.Platforms.Collision;
using Celeste.Simulation.Gameplay.Player.Collision.Contacts;
using Celeste.Simulation.Gameplay.Player.Collision.Responses;
using Celeste.Simulation.Gameplay.Player.Movement;
using Celeste.Simulation.Gameplay.Player.Presentation.Animation;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mods.JungleHelper.Runtime.Platforms
{
    public sealed class ClimbableOneWayHooks : IDisposable
    {
        private readonly ISourceEngineContext engine;
        private readonly List<IDisposable> loaders = new List<IDisposable>();
        private readonly List<IDisposable> active = new List<IDisposable>();
        private bool enabled;
        public bool Enabled => enabled;

        public ClimbableOneWayHooks(ISourceEngineContext engine)
        {
            this.engine = engine;
            try
            {
                foreach (var ctor in typeof(OriginalLevelLoaderScene).GetConstructors())
                    loaders.Add(new ILHook(ctor,
                        il => AppendConstructor(il,
                            self => Configure(
                                (Celeste.Game.Content.Maps.OriginalChapterMapData)((OriginalLevelLoaderScene)self)
                                .Session.MapData, ((OriginalLevelLoaderScene)self).EngineContext))));
                foreach (var ctor in typeof(OverworldLoaderSequence).GetConstructors())
                    loaders.Add(new ILHook(ctor, il =>
                    {
                        var cursor = new ILCursor(il);
                        while (cursor.TryGotoNext(MoveType.Before, i => i.OpCode == OpCodes.Ret))
                        {
                            cursor.Emit(OpCodes.Ldarg_0);
                            cursor.Emit(OpCodes.Ldarg_2);
                            cursor.EmitDelegate<Action<OverworldLoaderSequence, OverworldStartMode>>((self, mode) =>
                            {
                                if (ReferenceEquals(self.EngineContext, engine) && (int)mode != -1) SetEnabled(false);
                            });
                            cursor.Index++;
                        }
                    }));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private static void AppendConstructor(ILContext il, Action<object> callback)
        {
            var cursor = new ILCursor(il);
            while (cursor.TryGotoNext(MoveType.Before, i => i.OpCode == OpCodes.Ret))
            {
                cursor.Emit(OpCodes.Ldarg_0);
                cursor.EmitDelegate(callback);
                cursor.Index++;
            }
        }

        private void Configure(Celeste.Game.Content.Maps.OriginalChapterMapData map, ISourceEngineContext owner)
        {
            if (!ReferenceEquals(owner, engine)) return;
            SetEnabled(map?.Levels?.Any(room => room.Entities?.Any(data =>
                data.Name == "JungleHelper/ClimbableOneWayPlatform" ||
                data.Name == "JungleHelper/AttachedClimbableOneWayPlatform") == true) == true);
        }

        public void SetEnabled(bool value)
        {
            if (enabled == value) return;
            if (!value)
            {
                enabled = false;
                for (int i = active.Count - 1; i >= 0; i--) active[i].Dispose();
                active.Clear();
                return;
            }

            enabled = true;
            try
            {
                active.Add(new ILHook(Method(typeof(PixelActor), "MoveHExact"), PatchMovement));
                active.Add(new ILHook(Method(typeof(PlayerContacts), "ClimbCheck"), PatchContactChecks));
                active.Add(new ILHook(Method(typeof(PlayerContacts), "WallJumpCheck"), PatchContactChecks));
                active.Add(new ILHook(Method(typeof(PlayerContacts), "SlipCheck"), PatchPointChecks));
                active.Add(new Hook(Method(typeof(PlayerContacts), "ClimbHopBlocked"),
                    (Func<Func<PlayerContacts, bool, bool>, PlayerContacts, bool, bool>)Hop));
                active.Add(new Hook(Method(typeof(ClimbSequence), "Begin"),
                    (Action<Action<ClimbSequence>, ClimbSequence>)Begin));
                active.Add(new Hook(Method(typeof(ClimbSequence), "Update"),
                    (Func<Func<ClimbSequence, int>, ClimbSequence, int>)Update));
                active.Add(new ILHook(Method(typeof(ClimbSequence), "Begin"), PatchSolidChecks));
                active.Add(new ILHook(Method(typeof(ClimbSequence), "Update"), PatchSolidChecks));
                active.Add(new Hook(Method(typeof(RoomPlayerSession), "ClimbJump"),
                    (Action<Action<RoomPlayerSession>, RoomPlayerSession>)Jump));
                active.Add(new Hook(Method(typeof(SourceSurfacePlatformSelection), "Select"),
                    (Func<Func<List<object>, RoomPlayerSession, SourceSurfaceSoundSelection>, List<object>,
                        RoomPlayerSession, SourceSurfaceSoundSelection>)Surface));
                active.Add(new ILHook(Method(typeof(PlayerAnimation), "Update"), PatchSolidChecks));
                active.Add(new ILHook(Method(typeof(PlayerCollisionResponse), "Horizontal"), PatchHorizontalDuck));
                active.Add(new Hook(Method(typeof(PlayerCollisionResponse), "HorizontalSolidAt"),
                    (Func<Func<PlayerCollisionResponse, Vector2, bool>, PlayerCollisionResponse, Vector2, bool>)
                    Horizontal));
                // The original ldsfld Input.Grab anchor does not match the current GrabCheck call.
            }
            catch
            {
                SetEnabled(false);
                throw;
            }
        }

        private static MethodInfo Method(Type type, string name) =>
            type.GetMethod(name,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) ??
            throw new MissingMethodException(type.FullName, name);

        private bool Own(RoomPlayerSession player) => ReferenceEquals(player.EngineContext, engine);

        private static RoomPlayerSession Player(PlayerContacts contacts) =>
            (RoomPlayerSession)contacts.Body.SourceColliderOwner;

        private void PatchContactChecks(ILContext il)
        {
            var cursor = new ILCursor(il);
            var offset = new VariableDefinition(il.Import(typeof(Vector2)));
            il.Body.Variables.Add(offset);
            bool wallJump = il.Method.Name == "WallJumpCheck";
            int count = 0;
            while (cursor.TryGotoNext(MoveType.Before, i => i.MatchCall(typeof(PlayerContacts), "SolidAt")))
            {
                cursor.Emit(OpCodes.Stloc, offset);
                cursor.Emit(OpCodes.Ldloc, offset);
                cursor.Index++;
                cursor.Emit(OpCodes.Ldarg_0);
                cursor.Emit(OpCodes.Ldloc, offset);
                cursor.EmitDelegate<Func<bool, PlayerContacts, Vector2, bool>>((result, self, at) =>
                {
                    var player = Player(self);
                    return Own(player)
                        ? ClimbableOneWayQueries.EntityContact(result, player, at, wallJump)
                        : result;
                });
                count++;
            }

            if (count != 1)
                throw new InvalidOperationException("Jungle player contact probe count mismatch: " +
                                                    il.Method.FullName);
        }

        private void PatchPointChecks(ILContext il)
        {
            var cursor = new ILCursor(il);
            var point = new VariableDefinition(il.Import(typeof(Vector2)));
            il.Body.Variables.Add(point);
            int count = 0;
            while (cursor.TryGotoNext(MoveType.Before,
                       i => i.MatchCallvirt(typeof(ICollisionWorld), "SolidAt") &&
                            ((MethodReference)i.Operand).Parameters[0].ParameterType.FullName ==
                            typeof(Vector2).FullName))
            {
                cursor.Emit(OpCodes.Stloc, point);
                cursor.Emit(OpCodes.Ldloc, point);
                cursor.Index++;
                cursor.Emit(OpCodes.Ldarg_0);
                cursor.Emit(OpCodes.Ldloc, point);
                cursor.EmitDelegate<Func<bool, PlayerContacts, Vector2, bool>>((result, self, at) =>
                {
                    var player = Player(self);
                    return Own(player) ? ClimbableOneWayQueries.PointContact(result, player.Scene, at) : result;
                });
                count++;
            }

            if (count != 2)
                throw new InvalidOperationException("Jungle player slip probe count mismatch: " + il.Method.FullName);
        }

        private bool Hop(Func<PlayerContacts, bool, bool> orig, PlayerContacts self, bool seeds)
        {
            bool result = orig(self, seeds);
            var player = Player(self);
            if (result || !Own(player) || player.Scene == null) return result;
            foreach (ClimbableOneWayPlatform other in ((ISourceScene)player.Scene).SourceScene.Entities
                     .GetTrackedEntities(typeof(ClimbableOneWayPlatform)))
                if (!SourceEntityCollision.Check(player, other) &&
                    SourceEntityCollision.CheckAt(player, other, player.Position + Vector2.UnitX * player.Facing))
                    return true;
            return false;
        }

        private void Begin(Action<ClimbSequence> orig, ClimbSequence self)
        {
            var player = ((RoomPlayerWorld)self.World).SourcePlayer;
            if (Own(player)) ClimbableOneWayQueries.FacingPlatform(player)?.BeforeClimbBegin(player);
            orig(self);
        }

        private int Update(Func<ClimbSequence, int> orig, ClimbSequence self)
        {
            var player = ((RoomPlayerWorld)self.World).SourcePlayer;
            if (Own(player)) ClimbableOneWayQueries.FacingPlatform(player)?.BeforeClimbUpdate(player);
            return orig(self);
        }

        private void Jump(Action<RoomPlayerSession> orig, RoomPlayerSession self)
        {
            orig(self);
            if (Own(self)) ClimbableOneWayQueries.FacingPlatform(self)?.AfterClimbJump(self);
        }

        private bool Horizontal(Func<PlayerCollisionResponse, Vector2, bool> orig, PlayerCollisionResponse self,
            Vector2 offset)
        {
            bool result = orig(self, offset);
            return Own(self.Player) ? ClimbableOneWayQueries.EntityContact(result, self.Player, offset) : result;
        }

        private SourceSurfaceSoundSelection Surface(
            Func<List<object>, RoomPlayerSession, SourceSurfaceSoundSelection> orig, List<object> platforms,
            RoomPlayerSession player)
        {
            if (platforms.Count == 0 && player != null && Own(player) && player.Scene != null)
                foreach (ClimbableOneWayPlatform other in ((ISourceScene)player.Scene).SourceScene.Entities
                         .GetTrackedEntities(typeof(ClimbableOneWayPlatform)))
                    if (SourceEntityCollision.CheckAt(player, other, player.Center + Vector2.UnitX * player.Facing))
                        return other.SurfaceIndex != -1
                            ? new SourceSurfaceSoundSelection(other.SurfaceIndex)
                            : orig(platforms, player);
            return orig(platforms, player);
        }

        private delegate bool MovementResult(bool result, PixelActor actor, Vector2 at, ref PixelPlatform hit);

        private void PatchMovement(ILContext il)
        {
            var cursor = new ILCursor(il);
            if (!cursor.TryGotoNext(MoveType.Before, i => i.MatchCall(typeof(PixelActor), "SolidAt")))
                throw new InvalidOperationException("Jungle Actor.MoveHExact solid probe not found.");
            var hit = new VariableDefinition(il.Import(typeof(PixelPlatform).MakeByRefType()));
            var at = new VariableDefinition(il.Import(typeof(Vector2)));
            il.Body.Variables.Add(hit);
            il.Body.Variables.Add(at);
            cursor.Emit(OpCodes.Stloc, hit);
            cursor.Emit(OpCodes.Stloc, at);
            cursor.Emit(OpCodes.Ldloc, at);
            cursor.Emit(OpCodes.Ldloc, hit);
            cursor.Index++;
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldloc, at);
            cursor.Emit(OpCodes.Ldloc, hit);
            cursor.EmitDelegate<MovementResult>((bool result, PixelActor actor, Vector2 position,
                ref PixelPlatform originalHit) =>
            {
                if (result || !(actor.SourceColliderOwner is RoomPlayerSession player) || !Own(player)) return result;
                var wall = ClimbableOneWayQueries.FirstOutside(player, player.Scene, position,
                    position.X < actor.Position.X);
                if (wall == null || !player.Input.GrabCheck || wall.ClimbJumpGrabCooldown > 0f) return false;
                if (wall.StaminaBehavior == StaminaBehavior.None && player.Data.Stamina < 20f) return false;
                if (wall.StaminaBehavior != StaminaBehavior.None)
                    player.Data.Stamina = Math.Max(player.Data.Stamina, 21f);
                originalHit = DirectionalCollisionTargetFactory
                    .Create(((ISourcePlatformCollisionScene)player.Scene).SolidWorldFactory).Platform;
                return true;
            });
        }

        private void PatchSolidChecks(ILContext il)
        {
            var cursor = new ILCursor(il);
            int count = 0;
            while (cursor.TryGotoNext(MoveType.Before,
                       i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) &&
                            i.Operand is MethodReference call &&
                            (call.Name == "SolidAt" || call.Name == "SolidPointAt") && call.Parameters.Count == 1 &&
                            call.Parameters[0].ParameterType.FullName == typeof(Vector2).FullName))
            {
                var call = (MethodReference)cursor.Next.Operand;
                var world = new VariableDefinition(il.Import(typeof(object)));
                var offset = new VariableDefinition(il.Import(typeof(Vector2)));
                il.Body.Variables.Add(world);
                il.Body.Variables.Add(offset);
                cursor.Emit(OpCodes.Stloc, offset);
                cursor.Emit(OpCodes.Stloc, world);
                cursor.Emit(OpCodes.Ldloc, world);
                cursor.Emit(OpCodes.Castclass, call.DeclaringType);
                cursor.Emit(OpCodes.Ldloc, offset);
                cursor.Index++;
                cursor.Emit(OpCodes.Ldloc, world);
                cursor.Emit(OpCodes.Ldloc, offset);
                bool point = call.Name == "SolidPointAt";
                cursor.EmitDelegate<Func<bool, object, Vector2, bool>>((result, actual, at) =>
                    actual is RoomPlayerWorld owner && Own(owner.SourcePlayer)
                        ? point
                            ? ClimbableOneWayQueries.PointContact(result, owner.SourcePlayer.Scene, owner.Position + at)
                            : ClimbableOneWayQueries.EntityContact(result, owner.SourcePlayer, at)
                        : result);
                count++;
            }

            if (count == 0)
                throw new InvalidOperationException("Jungle player solid probes not found: " + il.Method.FullName);
        }

        private void PatchHorizontalDuck(ILContext il)
        {
            var cursor = new ILCursor(il);
            if (!cursor.TryGotoNext(MoveType.Before,
                    i => i.Operand is MethodReference call && call.Name == "DuckFreeAt"))
                throw new InvalidOperationException("Jungle horizontal duck probe not found.");
            var offset = new VariableDefinition(il.Import(typeof(Vector2)));
            il.Body.Variables.Add(offset);
            cursor.Emit(OpCodes.Stloc, offset);
            cursor.Emit(OpCodes.Ldloc, offset);
            cursor.Index++;
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Ldloc, offset);
            cursor.EmitDelegate<Func<bool, PlayerCollisionResponse, Vector2, bool>>((free, self, at) =>
            {
                var player = self.Player;
                if (!free || !Own(player)) return free;
                var contacts = player.World.Contacts;
                var saved = player.Collider;
                player.Collider = contacts.DuckHitbox;
                bool blocked = ClimbableOneWayQueries.EntityContact(false, player, at);
                player.Collider = saved;
                return !blocked;
            });
        }

        public void Dispose()
        {
            SetEnabled(false);
            for (int i = loaders.Count - 1; i >= 0; i--) loaders[i].Dispose();
            loaders.Clear();
        }
    }
}
