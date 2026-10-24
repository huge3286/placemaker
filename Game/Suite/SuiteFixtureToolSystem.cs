// Game runner: an invisible tool that applies the fixture's definitions (roads, zoning, plopped buildings).
// CS2 applies temporaries only through the ACTIVE tool's applyMode (same reason as Game/Build/BuildToolSystem), so the
// runner activates this tool, queues work that runs inside its OnUpdate (ToolUpdate phase: the definitions are created
// where vanilla tools create theirs), asks for Apply a few frames later and gives the tool slot back. It also draws the
// game's zone grid while a zoning picture is taken (the grid is drawn only while the active tool asks for it).
// Registered only when the suite gate is open (Game/Suite/SuiteGate).

using System;
using System.Collections.Generic;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.Scripting;

namespace Citymaker.Game.Suite
{
    public sealed partial class SuiteFixtureToolSystem : ToolBaseSystem
    {
        public const string Id = "Citymaker Suite Fixture";
        public override string toolID => Id;
        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;

        internal ApplyMode Requested = ApplyMode.None;
        internal int Updates;
        /// <summary>While active: the game draws the zone grid (zoned cells in their zone colour), as with the zoning tool.</summary>
        internal bool ShowZones;
        private readonly Queue<Action> _work = new Queue<Action>();
        private readonly List<Entity> _definitions = new List<Entity>();

        public ToolBaseSystem DefaultTool => m_DefaultToolSystem;
        public ToolSystem Tools => m_ToolSystem;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
        }

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            m_ToolRaycastSystem.typeMask = global::Game.Common.TypeMask.None;
        }

        /// <summary>Runs <paramref name="a"/> in this tool's next update (only while it is the active tool).</summary>
        internal void Enqueue(Action a) => _work.Enqueue(a);

        internal Entity NewDefinition()
        {
            var e = EntityManager.CreateEntity();
            _definitions.Add(e);
            return e;
        }

        internal int DestroyDefinitions()
        {
            int n = 0;
            foreach (var e in _definitions)
                if (EntityManager.Exists(e)) { EntityManager.DestroyEntity(e); n++; }
            _definitions.Clear();
            return n;
        }

        [Preserve]
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            Updates++;
            while (_work.Count > 0)
            {
                var a = _work.Dequeue();
                try { a(); }
                catch (Exception e) { Mod.Log.Error(e, "Citymaker suite: fixture work failed"); }
            }
            applyMode = Requested;
            Requested = ApplyMode.None;
            requireZones = ShowZones;
            return inputDeps;
        }

        [Preserve]
        protected override void OnStopRunning()
        {
            applyMode = ApplyMode.None;
            Requested = ApplyMode.None;
            requireZones = false;
            base.OnStopRunning();
        }
    }
}
