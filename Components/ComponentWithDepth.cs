using System;
using Celeste.Core.Components;
using Celeste.Core.Scheduling;
using Celeste.Scene.Entities;
using Celeste.Scene.Tracking;
using Celeste.Scene.Scheduling.ExecutionTags;

namespace Celeste.Mods.JungleHelper.Components
{
    public sealed class ComponentWithDepth<T> : SourceComponentState where T : SourceComponentState
    {
        private sealed class ComponentEntity : SceneEntityState
        {
            internal readonly T Component;

            internal ComponentEntity(ISourceEngineContext engine, T component)
                : base(((ISourceEntitySourceDataEnvironment)engine).EntitySourceDataRegistration)
            {
                AddTag(((ISourcePersistentTransitionExecutionTagsEnvironment)engine).PersistentTransitionExecutionTags
                    .Persistent.Value);
                Add(Component = component);
            }
        }

        public int Depth;
        public T Component => componentEntity.Component;
        private readonly ComponentEntity componentEntity;

        public ComponentWithDepth(ISourceEngineContext engine, T component) : base(true, false)
        {
            componentEntity = new ComponentEntity(engine, component);
        }

        public void Add(SourceComponentState component) => componentEntity.Add(component);

        public override void Added(object entity)
        {
            if (Entity is SceneEntityState oldOwner) oldOwner.Remove(this);
            base.Added(entity);
            if (((SceneEntityState)entity).Scene != null) AddEntityToScene(((SceneEntityState)entity).Scene);
        }

        public override void EntityAdded(object scene)
        {
            base.EntityAdded(scene);
            AddEntityToScene(scene);
        }

        private void AddEntityToScene(object scene)
        {
            var scheduler = ((ISourceScene)scene).SourceScene;
            scheduler.AddEntity(componentEntity);
            scheduler.CancelScheduledRemoval(componentEntity);
            Update();
        }

        public override void Removed(object entity)
        {
            base.Removed(entity);
            componentEntity.RemoveSelf();
        }

        public override void EntityRemoved(object scene)
        {
            base.EntityRemoved(scene);
            componentEntity.RemoveSelf();
        }

        public override void Update()
        {
            base.Update();
            var owner = (SceneEntityState)Entity;
            componentEntity.Depth = Depth;
            componentEntity.Position = owner.Position;
            componentEntity.Visible = owner.Visible;
        }
    }
}
