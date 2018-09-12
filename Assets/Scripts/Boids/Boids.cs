using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;

/* 
    Game plan

    Loosely following along with these: 
    https://software.intel.com/en-us/articles/get-started-with-the-unity-entity-component-system-ecs-c-sharp-job-system-and-burst-compiler
    https://github.com/Unity-Technologies/EntityComponentSystemSamples/blob/master/Documentation/content/ecs_in_detail.md#automatic-job-dependency-management-jobcomponentsystem
    Once interop with classic unity is introduce shit goes complex quite fast

    Don't even use transforms. I will make my own renderer thing.
    Though, starting with Transform use would make development faster. :)

    Todo

    How to use archetypes for making types and instantiating them?

 */

[System.Serializable]
public struct BoidPosition : IComponentData {
    public float3 Value;
}

public struct BoidVelocity : IComponentData {
    public float3 Value;
}

// The below wrap the datatypes for showing in the inspector
//public class BoidVel0cityComponent : ComponentDataWrapper<BoidVelocity> { }
//public class BoidPositionComponent : ComponentDataWrapper<BoidPosition> { }

public class BoidSpawnerSystem : MonoBehaviour {
    private GameObject _boidPrefab; // Bleh. Thin skeleton definining archetype.
    private EntityManager _manager; // Use this to get at everything in a world, managers, ents, comps, etc. Not accessible in jobs, use intermediate apis.

    private void Start() {
        _manager = World.Active.GetOrCreateManager<EntityManager>();
    }

    private void Update() {
        if (Input.GetKeyDown(KeyCode.Space)) {
            AddBoids(16);
        }
    }

    private void AddBoids(int count) {
        var entities = new NativeArray<Entity>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        _manager.Instantiate(_boidPrefab, entities);

        for (int i = 0; i < entities.Length; i++) {
            _manager.SetComponentData(entities[i], new BoidPosition());
            _manager.SetComponentData(entities[i], new BoidVelocity());
        }
        entities.Dispose();
    }
}

public class BoidPositionSystem : JobComponentSystem {
    
    [BurstCompile]
    struct MovementJob : IJobProcessComponentData<BoidPosition, BoidVelocity> {
        public float dt;

        public void Execute(ref BoidPosition p, [ReadOnly] ref BoidVelocity v) {
            p.Value += v.Value * dt;
        }
    }

    // Set up jobs per frame
    protected override JobHandle OnUpdate(JobHandle inputDeps) {
        var mj = new MovementJob() {
            dt = 0.01f,
        };
        var h = mj.Schedule(this, 64, inputDeps);
        return h;
    }
}
