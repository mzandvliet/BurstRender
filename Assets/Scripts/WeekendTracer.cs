using UnityEngine;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;
using Ramjet;
using Random = Unity.Mathematics.Random;

/* 
Todo:

Performance varies wildly for small variations in scene. Sometimes 70MRay/sec, sometimes 10. Egalize.

- Find a nicer way to stop jobs in progress in case you want to abort a render. It's messes up the editor now.
    - Splitting a render into a sequence of smaller jobs would work well
    - We want an iterative renderer anyway
    - It will also lessen the appearance of the 4 frame TempAllocator warning
    - Would probably also stop major cpu/os stalls from happening
- Show incremental results
- This way getting from a Burst-style screenbuffer to a cpu-texture to a gpu render is dumb
    - Compute buffer could be faster

- Ray termination time is wildly divergent. You need to make that constant-ish. There are many ways.

Now that we've done this we can compare some aspects of this style of tracing to distance fields
- Without any culling or sorting, each ray here has to be prepared to intersect with all possible
objects in the scene. So each step you loop over all objects. This gets expensive crazy fast, it
goes up as n objects and r bounces happen.
- With a single distance field to sample from that encodes multiple parts of the scene, for many
objects and many rays, it starts winning.

So, to optimize there's a couple of things we should try to do:
- Don't use ad-hoc functional recursion
- Cull objects, or otherwise limit the amount of objects a ray has to computationally interact with
- Limit super-sampling for parts of the image that need it.
- When generating random numbers, think carefully how many bits of entropy you need
- Can you at all use cached irradiance? When hitting a surface, if there has been prior local light information, use it?
- Render iteratively, adaptively

For multiple object types and lists per scene, we can make a type system that matches primitive type to
trace function.
*/

namespace Tracing {
    public class WeekendTracer : MonoBehaviour {
        [SerializeField] private bool _drawDebugRays;
        [SerializeField] private bool _saveImage;
        [SerializeField] private string _saveFolder = "C:\\Users\\Martijn\\Desktop\\weekendtracer\\";

        private NativeArray<float3> _fibs;

        private Scene _scene;
        private Camera _camera;

        private ClearJob _clear;

        private const int _maxJobsPerFrame = 1024;

        private NativeArray<float3> _buffer;

        int2 resolution = new int2(128,128);
        const float aspect = 1;//21f / 9f;
        const float vfov = 25f;
        const float aperture = 0.1f;

        private Texture2D _tex;



        private void Awake() {
            var position = new float3(-3f, 1f, -2f);
            var lookDir = new float3(0, 0, 1) - position;
            var focus = math.length(lookDir);
            var rotation = quaternion.LookRotation(lookDir / focus, new float3(0, 1, 0));
            _camera = new Camera(vfov, aspect, aperture, focus);
            _camera.Position = position;
            _camera.Rotation = rotation;

            int totalPixels = (int)(resolution.x * resolution.y);
            Debug.Log("Resolution = " + resolution + ", total pixels: " + totalPixels);

            _scene = MakeScene();

            _fibs = new NativeArray<float3>(4096, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            Math.GenerateFibonacciSphere(_fibs);

            _handles = new NativeQueue<TraceJobHandle>(Allocator.Persistent);
            _renderTargets = new NativeArray<RenderResult>[_maxJobsPerFrame];
            for (int i = 0; i < _maxJobsPerFrame; i++) {
                _renderTargets[i] = new NativeArray<RenderResult>(1, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }

            _buffer = new NativeArray<float3>(totalPixels, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private void OnDestroy() {
            _scene.Dispose();
            _fibs.Dispose();
            _handles.Dispose();

            for (int i = 0; i < _maxJobsPerFrame; i++) {
                _renderTargets[i].Dispose();
            }
            _buffer.Dispose();
        }

        private static Scene MakeScene() {
            var scene = new Scene();

            scene.LightDir = math.normalize(new float3(-2f, -1, -0.33f));
            scene.LightColor = new float3(0.5f, 0.7f, 1f);

            var rng = new Random(1234);

            scene.Spheres = new NativeArray<Sphere>(7, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            scene.Spheres[0] = new Sphere(new float3(0, -100.5f, 1), 100f, new Material(MaterialType.Lambertian, new float3(0.8f, 0.8f, 0f)));
            scene.Spheres[1] = new Sphere(new float3(-1, 0, 1), 0.5f, new Material(MaterialType.Lambertian, new float3(0.1f, 0.2f, 0.5f)));
            scene.Spheres[2] = new Sphere(new float3(1, 0, 1), 0.5f, new Material(MaterialType.Metal, new float3(0.8f, 0.6f, 0.2f)));
            scene.Spheres[3] = new Sphere(new float3(0, 0, 1), 0.5f, new Material(MaterialType.Dielectric, new float3(1f, 1f, 1f)));
            scene.Spheres[4] = new Sphere(new float3(0, 0, 1), -0.45f, new Material(MaterialType.Dielectric, new float3(1f, 1f, 1f)));
            scene.Spheres[5] = new Sphere(new float3(0, 0, 1), 0.4f, new Material(MaterialType.Dielectric, new float3(1f, 1f, 1f)));
            scene.Spheres[6] = new Sphere(new float3(0, 0, 1), -0.35f, new Material(MaterialType.Dielectric, new float3(1f, 1f, 1f)));

            scene.Planes = new NativeArray<Plane>(0, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            scene.Disks = new NativeArray<Disk>(0, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            return scene;
        }

        private void Start() {
            StartRender();
        }

        
        private NativeQueue<TraceJobHandle> _handles;
        private NativeArray<RenderResult>[] _renderTargets;

        private int _mipLevel;
        private RenderPass _pass;
        private int _pixelIndex;
        private bool _isRendering;
        private ulong _rayCount;
        private System.Diagnostics.Stopwatch _watch;

        private void StartRender() {
            Debug.Log("Rendering...");

            if (_isRendering) {
                Debug.Log("Already rendering...");
                return;
            }

            _isRendering = true;
            _watch = System.Diagnostics.Stopwatch.StartNew();

            StartRenderPass(8);
        }

        private void CompleteRender() {
            _isRendering = false;

            _watch.Stop();
            Debug.Log("Done! Time taken: " + _watch.ElapsedMilliseconds + "ms, Num Rays: " + _rayCount);
            Debug.Log("That's about " + (_rayCount / (_watch.ElapsedMilliseconds / 1000.0d)) / 1000000.0d + " MRay/sec");

            if (_saveImage) {
                Util.ExportImage(_tex, _saveFolder);
            }
        }

        private void StartRenderPass(int mipLevel) {
            Debug.Log("Starting mip level pass: " + mipLevel);
            _mipLevel = mipLevel;
            _pixelIndex = 0;
            _pass = new RenderPass(new int2(resolution.x / mipLevel, resolution.x / mipLevel));
        }

        private void CompleteRenderPass() {
            _tex = new Texture2D((int)_pass.Resolution.x, (int)_pass.Resolution.y, TextureFormat.ARGB32, false, true);
            _tex.filterMode = FilterMode.Point;

            Util.ToTexture2D(_buffer, _tex, _pass.Resolution);

            if (_mipLevel > 1) {
                StartRenderPass(_mipLevel-1);
            }
        }

        

        private void Update() {
            if (!_isRendering) {
                return;
            }

            for (int i = 0; _pixelIndex < _pass.TotalPixels && i < _maxJobsPerFrame; i++) {
                var tj = new TraceJob();
                tj.Fibs = _fibs;
                tj.Scene = _scene;
                tj.Camera = _camera;
                tj.Resolution = _pass.Resolution;
                tj.RaysPerPixel = 8;
                tj.MaxDepth = 8;
                tj.PixelIndex = _pixelIndex;
                tj.RenderResult = _renderTargets[i];

                var h = new TraceJobHandle();
                h.RenderTargetIndex = i;
                h.PixelIndex = _pixelIndex;
                h.JobHandle = tj.Schedule();
                _handles.Enqueue(h);

                _pixelIndex++;
            }
            JobHandle.ScheduleBatchedJobs();
        }

        private void LateUpdate() {
            if (!_isRendering) {
                return;
            }

            while (_handles.Count > 0) {
                var h = _handles.Dequeue();
                h.JobHandle.Complete();

                var pixCoords = Math.ToXY(h.PixelIndex, _pass.Resolution);
                var result = _renderTargets[h.RenderTargetIndex][0];
                _buffer[h.PixelIndex] = result.Color;
                _rayCount += result.RayCount;
            }

            if (_pixelIndex >= _pass.TotalPixels) {
                CompleteRenderPass();
            }
        }

        private void OnGUI() {
            if (_tex != null) {
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _tex, ScaleMode.ScaleToFit);
            }
        }

        private struct RenderPass {
            public int2 Resolution;
            public int TotalPixels;

            public RenderPass(int2 resolution) {
                Resolution = resolution;
                TotalPixels = (int)(resolution.x * resolution.y);
            }
        }

        private struct RenderResult {
            public float3 Color;
            public uint RayCount;
        }

        [BurstCompile]
        private struct TraceJob : IJob {
            [ReadOnly] public Scene Scene;
            [ReadOnly] public NativeArray<float3> Fibs;
            [ReadOnly] public Camera Camera;
            [ReadOnly] public int2 Resolution;
            [ReadOnly] public int RaysPerPixel;
            [ReadOnly] public int MaxDepth;
            [ReadOnly] public int PixelIndex;

            [WriteOnly] public NativeArray<RenderResult> RenderResult; // Would be nice if we could write these and read on main thread

            public void Execute() {
                var rng = new Unity.Mathematics.Random(14387 + ((uint)PixelIndex * 7));

                var screenPos = Math.ToXYFloat(PixelIndex, Resolution);
                float3 pixel = new float3(0f);

                ushort rayCount = 0;

                for (int r = 0; r < RaysPerPixel; r++) {
                    float2 jitter = new float2(rng.NextFloat(), rng.NextFloat());
                    float2 p = (screenPos + jitter) / (float2)Resolution;
                    var ray = Camera.GetRay(p, ref rng);

                    pixel += Trace.TraceRecursive(ray, Scene, ref rng, Fibs, 0, MaxDepth, ref rayCount);

                    // float3 col = new float3(1f);
                    // ushort t = 0;
                    // for (; t < Quality.MaxDepth; t++) {
                    //     if (!Trace.TraceStep(ref ray, Scene, ref rng, Fibs, ref col)) {
                    //         break;
                    //     }
                    // }
                    // pixel += col;
                    // rayCount += t;
                }

                pixel = math.sqrt(pixel / (float)RaysPerPixel);

                RenderResult[0] = new RenderResult {
                    Color = pixel,
                    RayCount = rayCount
                };
            }
        }

        private struct TraceJobHandle {
            public JobHandle JobHandle;
            public int PixelIndex;
            public int RenderTargetIndex;
        }

        [BurstCompile]
        public struct ClearJob : IJobParallelFor {
            public NativeArray<float3> Buffer;
            public void Execute(int i) {
                Buffer[i] = new float3();
            }
        }

        [BurstCompile]
        public struct GradientJob : IJobParallelFor {
            public int2 Resolution;
            public NativeArray<float3> Buffer;
            public void Execute(int i) {
                var screenPos = Math.ToXY(i, Resolution);
                // Todo: use kernel to differentiate with neighboring pixels
            }
        }

        private void OnDrawGizmos() {
            if (!Application.isPlaying || !_drawDebugRays) {
                return;
            }

            var rng = new Unity.Mathematics.Random(14387);

            var screenPos = new float2(0);
            float3 pixel = new float3(0f);

            Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
            for (int s = 0; s < _scene.Spheres.Length; s++) {
                Gizmos.DrawSphere(_scene.Spheres[s].Center, _scene.Spheres[s].Radius);
            }

            for (int r = 0; r < 16; r++) {
                Gizmos.color = Color.HSVToRGB(r / 8f, 0.7f, 0.5f);
                float2 jitter = new float2(rng.NextFloat(), rng.NextFloat() / 16f);
                float2 p = (screenPos + jitter);

                var ray = _camera.GetRay(p, ref rng);

                for (int t = 0; t < 8; t++) {
                    const float tMin = 0f;
                    const float tMax = 1000f;

                    Gizmos.DrawSphere(ray.origin, 0.01f);

                    HitRecord hit;
                    bool hitSomething = Intersect.Scene(_scene, ray, tMin, tMax, out hit);
                    if (hitSomething) {
                        Gizmos.DrawLine(ray.origin, hit.point);
                        Ray3f subRay;
                        if (!Trace.Scatter(ray, hit, ref rng, _fibs, out subRay)) {
                            break;
                        }
                        ray = subRay;
                    } else {
                        Gizmos.DrawRay(ray.origin, math.normalize(ray.direction));
                        break;
                    }
                }
            }
        }
    }
}
