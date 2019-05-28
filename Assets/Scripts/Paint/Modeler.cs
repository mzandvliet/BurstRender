using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Rng = Unity.Mathematics.Random;
using Ramjet;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;
using System;

/* 
    Todo:

    - In this system as well: fix the error where we're first dividing by w and
    then interpolating. We need to interpolate and perspective divide the resulting
    point.

    - Make splines out of curves
    - Make oriented patches out of curves
    - When part of a spline falls outside of frustum, it needs to be cut off, or culled?
        - Maybe we don't need to do that, only generate strokes on geometry that lies
        within view in the first place.

    make compositions

    Render grandfather's painting

    Explore the generalized notion of the B-Spline Curve
 */

public class Modeler : MonoBehaviour {
    [SerializeField] private Surface _surface;
    [SerializeField] private Painter _painter;

    private Camera _camera;

    private NativeArray<float3> _controls;
    private NativeArray<float3> _projectedControls;
    private NativeArray<float3> _colors;

    private Rng _rng;

    private void Awake() {
        _camera = gameObject.GetComponent<Camera>();
        _camera.enabled = true;

        _rng = new Rng(1234);
    }

    private void OnDestroy() {
        _controls.Dispose();
        _projectedControls.Dispose();
        _colors.Dispose();
    }

    private void Start() {
        // const int numStrokes = 16;
        // _controls = new NativeArray<float3>(numStrokes * BDCCubic3d.NUM_POINTS, Allocator.Persistent);

        int numBranches = 6;
        int numCurves = SumPowersOfTwo(numBranches);
        _controls = new NativeArray<float3>(numCurves * 4, Allocator.Persistent);
        _projectedControls = new NativeArray<float3>(_controls.Length, Allocator.Persistent);
        _colors = new NativeArray<float3>(numCurves, Allocator.Persistent);

        var treeJob = new GenerateFlowerJob();
        treeJob.numBranches = numBranches;
        treeJob.controlPoints = _controls;
        treeJob.colors = _colors;
        treeJob.rng = new Rng(1234);
        treeJob.Schedule().Complete();

        _painter.Init(numCurves);
    }

    private void Update() {
        if (Time.frameCount % 2 == 0) {
            Paint();
        }
    }

    private void Paint() {
        var h = new JobHandle();
        
        // CreateStrokesForSurface();

        _painter.Clear();

        var pj = new ProjectCurvesJob();
        pj.mat = _camera.projectionMatrix * _camera.worldToCameraMatrix;
        pj.controlPoints = _controls;
        pj.projectedControls = _projectedControls;
        h = pj.Schedule(h);
        h.Complete();

        _painter.Draw(_projectedControls, _colors);
    }

    private void OnDrawGizmos() {
        if (!Application.isPlaying) {
            return;
        }
        
        Draw3dSplines();
    }

    private void Draw3dSplines() {
        Gizmos.color = Color.white;
        var numCurves = _controls.Length / 4;
        for (int c = 0; c < numCurves; c++) {
            float3 pPrev = BDCCubic3d.GetAt(_controls, 0f, c);
            Gizmos.DrawSphere(pPrev, 0.01f);
            int steps = 8;
            for (int i = 1; i <= steps; i++) {
                float t = i / (float)(steps);
                float3 p = BDCCubic3d.GetAt(_controls, t, c);
                float3 tg = BDCCubic3d.GetTangentAt(_controls, t, c);
                float3 n = BDCCubic3d.GetNormalAt(_controls, t, new float3(0, 1, 0), c);
                Gizmos.DrawLine(pPrev, p);
                Gizmos.DrawSphere(p, 0.01f);

                // Gizmos.color = Color.blue;
                // Gizmos.DrawRay(p, n * 0.3f);
                // Gizmos.DrawRay(p, -n * 0.3f);
                // Gizmos.color = Color.green;
                // Gizmos.DrawRay(p, tg);

                pPrev = p;
            }    
        }
    }

    private static float3 ToFloat3(in float2 v) {
        return new float3(v.x, v.y, 0f);
    }

    /*  Now this will be a tricky thing

        Given some surface patch, we want to generate brush
        strokes over it, satisfying some constraints.

        Constraints can be:

        - Should cover surface, leave no gaps
        - Have n gradations of lighting
        - Should follow this hashing pattern

        etc.

        Another function might want to:
        - Extract silhouette
        
    */

    private void CreateStrokesForSurface() {
        var points = _surface.Points;
        var numSurfaceCurves = _surface.Points.Length / 4;

        var surfacePoints = new NativeArray<float3>(numSurfaceCurves * BDCCubic3d.NUM_POINTS, Allocator.TempJob);
        Util.CopyToNative(points, surfacePoints);

        var j = new GenerateSurfaceStrokesJob();
        j.surfacePoints = surfacePoints;
        j.rng = new Rng((uint)_rng.NextInt());
        j.controlPoints = _controls;
        j.colors = _colors;
        j.Schedule().Complete();

        surfacePoints.Dispose();
    }

    private struct GenerateFlowerJob : IJob {
        [ReadOnly] public int numBranches;
        public Rng rng;
        public NativeArray<float3> controlPoints;
        [WriteOnly] public NativeArray<float3> colors;

        public void Execute() {
            rng.InitState(12345);

            var stack = new NativeStack<int>(SumPowersOfTwo(numBranches), Allocator.Temp);
            var tree = new Tree(SumPowersOfTwo(numBranches));

            var rootIndex = tree.NewNode();
            GrowBranch(controlPoints.Slice(0, 4), new float3(0f, 0f, 1f), ref rng);
            stack.Push(rootIndex);

            while (stack.Count > 0) {
                var parent = tree.GetNode(stack.Peek());

                if (stack.Count < numBranches) {
                    
                    if (parent.leftChild == -1) {
                        var newChildIndex = tree.NewNode();
                        var controlPointIndex = newChildIndex * 4;
                        var pos = controlPoints[parent.index * 4 + 3];
                        GrowBranch(controlPoints.Slice(controlPointIndex, 4), pos, ref rng);
                        colors[newChildIndex] = rng.NextFloat3();
                        parent.leftChild = newChildIndex;
                        stack.Push(newChildIndex);
                        tree.Set(parent);
                        continue;
                    } else
                    if (parent.rightChild == -1) {
                        var newChildIndex = tree.NewNode();
                        var controlPointIndex = newChildIndex * 4;
                        var pos = controlPoints[parent.index * 4 + 3];
                        GrowBranch(controlPoints.Slice(controlPointIndex, 4), pos, ref rng);
                        colors[newChildIndex] = rng.NextFloat3();
                        parent.rightChild = newChildIndex;
                        stack.Push(newChildIndex);
                        tree.Set(parent);
                        continue;
                    } else {
                        stack.Pop();
                    }
                } else {
                    stack.Pop();
                }
            }

            stack.Dispose();
            tree.Dispose();
        }

        private static void GrowBranch(NativeSlice<float3> curve, float3 pos, ref Rng rng) {
            curve[0] = pos;
            for (int b = 1; b < 4; b++) {
                var growth = rng.NextFloat3(new float3(-1f, 0.5f, -1f), new float3(1f, 1f, 1f));
                pos += growth;
                curve[b] = pos;
            }
        }

        private struct Tree : IDisposable {
            private NativeArray<TreeNode> _nodes;
            private int _count;

            public Tree(int capacity) {
                _nodes = new NativeArray<TreeNode>(capacity, Allocator.Temp);
                _count = 0;
            }

            public void Dispose() {
                _nodes.Dispose();
            }

            public int NewNode() {
                if (_count == _nodes.Length) {
                    throw new Exception("Tree can't allocate more nodes");
                }
                int index = _count++;
                _nodes[index] = new TreeNode() {
                    index = index,
                    leftChild = -1,
                    rightChild = -1,
                };
                return index;
            }

            public void Set(TreeNode node) {
                _nodes[node.index] = node;
            }

            public TreeNode GetNode(int index) {
                return _nodes[index];
            }
        }

        private struct TreeNode {
            public int index;
            public int leftChild;
            public int rightChild;
        }
    }

    public static int SumPowersOfTwo(int n) {
        return IntPow(2, n) - 1;
    }

    public static int IntPow(int n, int pow) {
        int v = 1;
        while (pow != 0) {
            if ( (pow & 1) == 1) {
                v *= n;
            }
            n *= n;
            pow >>= 1;
        }
        return v;
    }

    private struct GenerateSurfaceStrokesJob : IJob {
        public Rng rng;

        [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<float3> surfacePoints;

        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float3> controlPoints;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float3> colors;

        public void Execute() {
            int numTargetCurves = controlPoints.Length / BDCCubic3d.NUM_POINTS;

            rng.InitState(1234);

            var tempCurve = new NativeArray<float3>(4, Allocator.Temp, NativeArrayOptions.ClearMemory);

            // Todo: Instead of GetAt(points), we may simply Get(points.Slice)
            for (int c = 0; c < numTargetCurves; c++) {
                float tVert = c / (float)(numTargetCurves-1);

                tempCurve[0] = BDCCubic3d.GetAt(surfacePoints, tVert, 0);
                tempCurve[1] = BDCCubic3d.GetAt(surfacePoints, tVert, 1);
                tempCurve[2] = BDCCubic3d.GetAt(surfacePoints, tVert, 2);
                tempCurve[3] = BDCCubic3d.GetAt(surfacePoints, tVert, 3);

                for (int j = 0; j < BDCCubic3d.NUM_POINTS; j++) {
                    float tHori = j / (float)(BDCCubic3d.NUM_POINTS - 1);
                    
                    var p = BDCCubic3d.Get(tempCurve, tHori);
                    controlPoints[c * BDCCubic3d.NUM_POINTS + j] = p;
                }

                float3 albedo = new float3(143f / 255f, 111 / 255f, 84 / 255f);
                colors[c] = albedo * (0.5f + tVert * 1f);
            }

            tempCurve.Dispose();
        }
    }

    private struct ProjectCurvesJob : IJob {
        [ReadOnly] public float4x4 mat;
        [ReadOnly] public NativeArray<float3> controlPoints;
        [WriteOnly] public NativeArray<float3> projectedControls;

        public void Execute() {
            for (int i = 0; i < controlPoints.Length; i++) {
                float4 p = new float4(controlPoints[i], 1f);
                p = math.mul(mat, p);
                projectedControls[i] = new float3(p.x, p.y, p.w);
            }
        }
    }
}

public struct NativeStack<T> : IDisposable where T : struct {
    private NativeArray<T> _items;
    private int _current;

    public int Count {
        get { return _current+1; }
    }

    public NativeStack(int capacity, Allocator allocator) {
        _items = new NativeArray<T>(capacity, allocator, NativeArrayOptions.ClearMemory);
        _current = -1;
    }

    public void Dispose() {
        _items.Dispose();
    }

    public void Push(T item) {
        if (_current + 1 > _items.Length) {
            throw new Exception("Push failed. Stack has already reached maximum capacity.");
        }

        _current++;
        _items[_current] = item;
    }

    public T Pop() {
        if(_current == -1) {
            throw new Exception("Pop failed. Stack is empty.");
        }

        T item = _items[_current];
        _current--;
        return item;
    }

    public T Peek() {
        if (_current == -1) {
            throw new Exception("Peek failed. Stack is empty.");
        }

        T item = _items[_current];
        return item;
    }
}