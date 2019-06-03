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

    private NativeArray<float3> _controls;
    private NativeArray<float> _widths;
    private NativeArray<float3> _colors;

    private Rng _rng;

    private void Awake() {

        _rng = new Rng(1234);
    }

    private void Start() {
        // const int numStrokes = 16;
        // _controls = new NativeArray<float3>(numStrokes * BDCCubic3d.NUM_POINTS, Allocator.Persistent);

        int numBranches = 10;
        int numCurves = SumPowersOfTwo(numBranches);
        _controls = new NativeArray<float3>(numCurves * 4, Allocator.Persistent);
        _widths = new NativeArray<float>(numCurves, Allocator.Persistent);
        _colors = new NativeArray<float3>(numCurves, Allocator.Persistent);

        var treeJob = new GenerateFlowerJob();
        treeJob.numLevels = numBranches;
        treeJob.controlPoints = _controls;
        treeJob.widths = _widths;
        treeJob.colors = _colors;
        treeJob.rng = new Rng(1234);
        treeJob.Schedule().Complete();

        _painter.Init(numCurves);
    }

    private void OnDestroy() {
        _controls.Dispose();
        _widths.Dispose();
        _colors.Dispose();
    }

    private void Update() {
        // CreateStrokesForSurface();
        _painter.Clear();
        _painter.Draw(_controls, _widths, _colors);
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

    // [BurstCompile] // Note: can't do this yet due to temp buffer creation inside job
    private struct GenerateFlowerJob : IJob {
        [ReadOnly] public int numLevels;
        public Rng rng;
        public NativeArray<float3> controlPoints;
        [WriteOnly] public NativeArray<float> widths;
        [WriteOnly] public NativeArray<float3> colors;

        public void Execute() {
            rng.InitState(12345);

            var stack = new NativeStack<int>(SumPowersOfTwo(numLevels), Allocator.Temp);
            var tree = new Tree(SumPowersOfTwo(numLevels));

            var rootIndex = tree.NewNode();
            GrowBranch(
                controlPoints.Slice(0, 4),
                new float3(0f, 0f, 1f),
                rng.NextFloat3(new float3(-1f, 0.5f, -1f),
                new float3(1f, 1f, 1f)),
                0f,
                ref rng);
            stack.Push(rootIndex);

            while (stack.Count > 0) {
                var parent = tree.GetNode(stack.Peek());

                if (stack.Count < numLevels) {
                    float normalizedLevel = stack.Count / (numLevels-1);

                    if (parent.leftChild == -1) {
                        var newChildIndex = tree.NewNode();
                        var controlPointIndex = newChildIndex * 4;
                        var parentC = controlPoints[parent.index * 4 + 2];
                        var parentD = controlPoints[parent.index * 4 + 3];
                        var tangent = parentD - parentC;
                        var startPos =  parentD - tangent * 0.5f;
                        GrowBranch(controlPoints.Slice(controlPointIndex, 4), startPos, tangent, normalizedLevel, ref rng);
                        colors[newChildIndex] = GenerateColor(normalizedLevel, ref rng);
                        widths[newChildIndex] = stack.Count == numLevels - 1 ? 3f : 8f / stack.Count;
                        parent.leftChild = newChildIndex;
                        stack.Push(newChildIndex);
                        tree.Set(parent);
                        continue;
                    } else
                    if (parent.rightChild == -1) {
                        var newChildIndex = tree.NewNode();
                        var controlPointIndex = newChildIndex * 4;
                        var parentC = controlPoints[parent.index * 4 + 2];
                        var parentD = controlPoints[parent.index * 4 + 3];
                        var tangent = parentD - parentC;
                        var startPos = parentD - tangent * 0.3f;
                        GrowBranch(controlPoints.Slice(controlPointIndex, 4), startPos, tangent, normalizedLevel, ref rng);
                        colors[newChildIndex] = GenerateColor(normalizedLevel, ref rng);
                        widths[newChildIndex] = stack.Count == numLevels - 1 ? 3f : 8f / stack.Count;
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

        private static float3 GenerateColor(float normalizedLevel, ref Rng rng) {
            return ToFloat3(Color.HSVToRGB(
                normalizedLevel * 0.4f + rng.NextFloat() * 0.2f,
                0.5f + normalizedLevel * 0.2f,
                0.9f)) * (0.6f + 0.3f * math.pow(normalizedLevel, 2f));
        }

        private static float3 ToFloat3(Color c) {
            return new float3(c.r, c.g, c.b);
        }

        private static void GrowBranch(NativeSlice<float3> curve, float3 pos, float3 startTangent, float level, ref Rng rng) {
            curve[0] = pos;
            pos += startTangent;
            curve[1] = pos;
            float maxOutward = rng.NextFloat(2f, level == 1.0 ? 3f : 14f);
            for (int b = 2; b < 4; b++) {
                var growth = 
                    rng.NextFloat3(
                        new float3(-1f - level * maxOutward, 0.5f - 2f * level, -1f - level * maxOutward),
                        new float3( 1f + level * maxOutward, 1f   - 2f * level,  1f + level * maxOutward)) +
                    new float3(level * 1f, 1f * level, 0f);
                growth *= level == 1.0 ? 0.5f : 1f;

                // Todo: add factor that avoids some clumping in the center canopy, biasing to growing outward radially

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

    [BurstCompile]
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
}