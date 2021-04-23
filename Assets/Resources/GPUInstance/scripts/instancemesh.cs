//#define VERBOSE

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GPUAnimation;
using System.Linq;

namespace GPUInstance
{
    /// <summary>
    /// Stop watch that allows for speeding up & slowing down time
    /// </summary>
    public class StopWatchTimer
    {
        private System.Diagnostics.Stopwatch s = new System.Diagnostics.Stopwatch();

        public void Start()
        {
            this.s.Start();
        }
        public void Stop()
        {
            this.s.Stop();
        }
        public void Restart()
        {
            this._previous_true_elapsed = 0.0;
            this.TimeMultiplier = 1.0;
            this._modified_time = 0.0;
            this.s.Restart();
        }
        public void Reset()
        {
            this._previous_true_elapsed = 0.0;
            this.TimeMultiplier = 1.0;
            this._modified_time = 0.0;
            this.s.Reset();
        }
        public bool IsRunning { get { return this.s.IsRunning; } }

        public double TimeMultiplier = 1.0;
        private double _modified_time = 0.0;
        private double _previous_true_elapsed = 0.0;

        /// <summary>
        /// Update the timer with a new checkpoint. Returns total elapsed milliseconds since timer started.
        /// </summary>
        public double Tick()
        {
            this.TimeMultiplier = System.Math.Max(0, this.TimeMultiplier); // no negative times

            double true_elapsed = this.s.Elapsed.TotalMilliseconds; // get the 'true' non-adjusted time from timer
            double delta_true_elapsed = true_elapsed - this._previous_true_elapsed; // get true 'delta' time

            double m_delta = this.TimeMultiplier * delta_true_elapsed; // adjust delta by global time speed
            this._modified_time += m_delta; // add to modified time

            this._previous_true_elapsed = true_elapsed; // store total elapsed time

            return this._modified_time; // return modified time!
        }
    }

    public static class Ticks
    {
        public const uint TicksPerSecond = 10000;
        public const double SecondsPerTick = 0.0001f;

        public static double GlobalTimeSpeed = 1.0;

        public static ulong CalcDeltaTicks(in ulong ticks, in StopWatchTimer s)
        {
            s.TimeMultiplier = Ticks.GlobalTimeSpeed;

            var delta_ticks = (ulong)(s.Tick() * 10) - ticks;

            delta_ticks = (delta_ticks / 10) * 10; // must always be divisible by '10'. This way timing can be slowed down and sped up by factors of 10 for individual instances, eg (1/10), (2/10), ... (21/10) etc... Animation speed works with factors of '10'. 
            return delta_ticks;
        }
    }

    /// <summary>
    /// Class for creating many identical meshes efficiently. This is object is used for internal purposes. See MeshInstancer.
    /// </summary>
    public sealed partial class instancemesh : System.IDisposable, InstanceMeshSet
    {
        private ComputeShader instancemeshShader = null;
        private ComputeBuffer transformBuffer = null;
        private ComputeBuffer deltaBuffer = null;
        private ComputeBuffer argsBuffer = null;
        private ComputeBuffer groupDataBuffer = null;
        private ComputeBuffer cameraFrustumBuffer = null;
        private ComputeBuffer animationsBuffer = null;
        private ComputeBuffer instanceIDBuffer = null;
        private ComputeBuffer textureAnimationsBuffer = null;
        private ComputeBuffer hierarchyDepthBuffer = null; // 2d array that determines the hierarchy depth of each instance
        private ComputeBuffer hierarchyDeltaBuffer = null; // used to update the hierarchyDepthBuffer
        private ComputeBuffer object2WorldBuffer = null;
        private ComputeBuffer indirectInstanceIDDeltaBuffer = null;
        private ComputeBuffer indirectInstanceIDBuffer = null;
        private ComputeBuffer groupBindPosesBuffer = null;
        private ComputeBuffer groupBindPosesDeltaBuffer = null;
        private ComputeBuffer boneMatricesBuffer = null;
        private ComputeBuffer computeIndirectArgsBuffer = null;
        private ComputeBuffer groupLODBuffer = null;
        private ComputeBuffer propertyBuffer = null;
        private ComputeBuffer propertyDeltaBuffer = null;
        private ComputeBuffer indirectPropertyIDBuffer = null;
        private ComputeBuffer indirectPropertyIDDeltaBuffer = null;
        private ComputeBuffer animToBoneAnimMapBuffer = null;
        private ComputeBuffer pathsBuffer = null;
        private ComputeBuffer pathsDeltaBuffer = null;
        private ComputeBuffer pathsDeltaIDBuffer = null;

        // resize buffers- used as temp storage so buffers can have their size increased
        private ComputeBuffer hierarchyDepthResizeBuffer = null;
        private ComputeBuffer object2WorldResizeBuffer = null;
        private ComputeBuffer transformResizeBuffer = null;
        private ComputeBuffer instanceIDResizeBuffer = null;
        private ComputeBuffer indirectInstanceIDResizeBuffer = null;
        private ComputeBuffer groupBindPosesResizeBuffer = null;
        private ComputeBuffer boneMatricesResizeBuffer = null;
        private ComputeBuffer propertyResizeBuffer = null;
        private ComputeBuffer indirectPropertyIDResizeBuffer = null;
        private ComputeBuffer pathsResizeBuffer = null;

        private int updateKernalID = -1;
        private int csum1KernalID = -1;
        private int csum2KernalID = -1;
        private int csum3KernalID = -1;
        private int csum4KernalID = -1;
        private int csum5KernalID = -1;
        private int motionKernalID = -1;
        private int calcObjectToWorldKernel = -1;
        private int updateHierarchyKernel = -1;
        private int CopyToInstanceBuffersKernel = -1;
        private int CopyFromInstanceBuffersKernel = -1;
        private int CopyToSkeletonBufferKernel = -1;
        private int CopyFromSkeletonBufferKernel = -1;
        private int CopyToHierarchyBufferKernel = -1;
        private int CopyFromHierarchyBufferKernel = -1;
        private int IndirectIDUpdateKernel = -1;
        private int UpdateGroupBindPosesBufferKernel = -1;
        private int CopyToBindPoseBufferKernel = -1;
        private int CopyFromBindPoseBufferKernel = -1;
        private int SelectRenderedInstancesKernel = -1;
        private int PrepareRenderedInstancesKernel = -1;
        private int CalculateBoneMatricesKernel = -1;
        private int UpdatePropertyBuffersKernel = -1;
        private int IndirectPropertyIDUpdateKernel = -1;
        private int CopyToPropertyBuffersKernel = -1;
        private int CopyFromPropertyBuffersKernel = -1;
        private int PropertySimulationKernel = -1;
        private int CopyToPathsBufferKernel = -1;
        private int CopyFromPathsBufferKernel = -1;
        private int UpdatePathsBufferKernel = -1;

        internal InstanceMeshDeltaBuffer<instance_delta> _delta_buffer { get; private set; }
        internal InstanceMeshDeltaBuffer<instance_properties_delta> _property_delta_buffer { get; private set; }

        /// <summary>
        /// Skeleton Vertex Mapping delta buffer object
        /// </summary>
        internal SkeletonIDMap _skeletons { get; private set; }
        /// <summary>
        /// Manages hierarchy info
        /// </summary>
        internal InstanceHierarchyMap hierarchy { get; private set; }

        internal InstanceMeshPropertyPaths _paths { get; private set; }

        internal GroupBindPosesDeltaBuffer _bind_pose_delta_buffer { get; private set; }
        private int BindPoseDeltaNumFloats { get { return this.NumSkeletonBones*GPUInstanceBoneMatrix4x4.kFloatStride+ 1; } } // number of floats in a complete bind pose delta

        private Dictionary<MeshTypeKey, MeshType> meshtypes = new Dictionary<MeshTypeKey, MeshType>(); //mesh types being drawn
        private HashSet<AnimationController> _controllers = new HashSet<AnimationController>(); // controllers that have been added to buffer

        private HashSet<ushort> pending_group_updates = new HashSet<ushort>(); // set of groupIDs that need to have their respective indirect args & LOD args updated on the GPU
        private Queue<ushort> pending_group_updates_queue = new Queue<ushort>(); // temp queue used for processing 'pending_group_updates' hashset
        private HashSet<ushort> pending_lod_group_updates = new HashSet<ushort>(); // hashet containing all group ids that need to adjust their LOD args on the GPU
        private Queue<ushort> pending_lod_group_updates_queue = new Queue<ushort>(); // temp queue used for processing 'pending_lod_group_updates' hashset
        private Vector3[] frustumArr; //camera frustum array

        private uint[] args; //indirect arguments
        private uint[] groupLOD; // groupLOD Buffer

        private const int kGroupDataStride = sizeof(int);//size of groupData struct
        private const int kIndirectArgCountPerGroup = 5;//length in (uint) of each args.
        public const int kThreadGroupX = 64;// number of threads per thread group for the 'x' thread group parameter for DispatchComputeShader
        /// <summary>
        /// Null ID
        /// </summary>
        public const int NULL_ID = 0; //dont change this value. NULL_ID must equal 0
        /// <summary>
        /// Any ID less than zero is invalid
        /// </summary>
        public const int INVALID_ID = -1;
        private const int frustum_count = 12;//number of Vector3's in a camera frustum

        /// <summary>
        /// Number of bones allocated for each skeleton. Note* this number acts like a max allowance (skeletons that use less bones will still work)
        /// </summary>
        public int NumSkeletonBones { get; private set; } = 32;

        /// <summary>
        /// max number of per-instance changes that can be applied per update. Note that this will require
        /// 'MaxDeltaCount' instances memory space to be allocated
        /// </summary>
        public int MaxDeltaCount { get; private set; }
        /// <summary>
        /// public maximum number of (mesh,material) types allowed
        /// </summary>
        public int MaxMeshTypes { get { return maxMeshTypes; } } // subtract one
        /// <summary>
        /// maximum allowed groupID
        /// </summary>
        public int MaxMeshTypeGroupID { get { return maxMeshTypes; } }
        /// <summary>
        /// Maximum allowed meshtypes: This value cannot be changed (shader csum expects max=4096. Also, BoneType (in Instance Data) assumed no bigger than ushort
        /// </summary>
        public const int maxMeshTypes = 4096;

        /// <summary>
        /// Number of MeshTypes being used
        /// </summary>
        public int MeshTypeCount { get { return meshtypes.Count; } }
        /// <summary>
        /// Number of MeshTypes that can still be allocated
        /// </summary>
        public int MeshTypeCountRemaining { get { return instancemesh.maxMeshTypes - (this.GroupIDS.InstanceCount); } }

        /// <summary>
        /// Maxmimum allowed depth of instance hierarchy
        /// </summary>
        public int MaxHierarchyDepth { get; private set; } = 12;

        /// <summary>
        /// If nonnull, all instanced objects not in this camera will be culled
        /// </summary>
        public Camera FrustumCamera;

        /// <summary>
        /// The default MeshType! (An untextured vertex-colored cube)
        /// </summary>
        public MeshType Default { get; private set; }

        /// <summary>
        /// Ticks since initialized
        /// </summary>
        public ulong Ticks { get; private set; } = 0;
        /// <summary>
        /// delta ticks. Updated every time Update() is invoked
        /// </summary>
        public ulong DeltaTicks { get; private set; } = 0;
        private StopWatchTimer _ticks_stopwatch = new StopWatchTimer();

        /// <summary>
        /// Number of LOD levels supported.
        /// </summary>
        public const int NumLODLevels = 5;
        static int groupLODIndex(ushort group_id) { return ((int)group_id) * instancemesh.NumLODLevels * 2; }
        static int groupLODStride() { return instancemesh.NumLODLevels * 2; }
        static uint LODRatio2Uint(float r) { return (uint)(r * 100000.0f); }

        /// <summary>
        /// Graphics command buffer
        /// </summary>
        private UnityEngine.Rendering.CommandBuffer cmd = new UnityEngine.Rendering.CommandBuffer();

        /// <summary>
        /// the type of distance frustum culling that should be performed... if it's enabled
        /// </summary>
        public enum FrustumDistanceCullingType
        {
            None=0,
            /// <summary>
            /// Use distance from camera as a basis for claculating LOD and distance-based culling
            /// </summary>
            EUCLIDEAN_DISTANCE = 1,
            /// <summary>
            /// Use a uniform value for calculating LOD and distance from camera. This can be useful for 2d use cases. Note** (object radius is still used... so larger objects will still LOD and cull at greater distances).
            /// Also Note**, generally, for 2D you dont need LOD- mipmaps will handle it for you.
            /// </summary>
            UNIFORM_DISTANCE = 2
        }
        /// <summary>
        /// What type of frustum culling should be performed?
        /// </summary>
        public FrustumDistanceCullingType DistanceCullingType = FrustumDistanceCullingType.EUCLIDEAN_DISTANCE;
        /// <summary>
        /// Uniform distance from camera for UNIFORM_DISTANCE culling types.
        /// </summary>
        public float UniformCameraDistance = 0;

        /// <summary>
        /// Set radius to distance LOD Range where (x=min,y=max) 'min' is the point where model is culled
        /// 'max' is the point where model is not culled. Radius to distance is a ratio of instance.radius / instance.distance to camera.
        /// </summary>
        public float cull_radius_distance_LOD_range { get; set; } = 0.00125f;

        /// <summary>
        /// Has this instance been initialized yet?
        /// </summary>
        public bool Initialized { get; private set; }

        public InstanceIDGenerator IDs { get; private set; }
        public InstanceIDGenerator PropertyIDs { get; private set; }
        private InstanceIDGenerator GroupIDS { get; set; }

        private System.Threading.Thread _main_thread = null;

        private void AssertInitializedAndMainThread()
        {
            if (!Initialized)
                throw new System.Exception("Error, not initialized.");
            if (System.Threading.Thread.CurrentThread != this._main_thread)
                throw new System.Exception("Error, must invoke function from main thread!");
        }

        /// <summary>
        /// [Main Thread] create a new gpuInstanceMesh. This class is able to draw many instances of arbitrary meshes and materials.
        /// Please note*, you should set the 'maxInstanceCount,maxDeltaCount,maxMeshTypes' as close to your 
        /// expected values as possible. Space to accomodate your input values will be allocated on the GPU when creating this object.
        /// </summary>
        /// <param name="maxDeltaCount"> Size of delta buffer. The delta buffer pushes updates to gpu instances. Should be greater than the expected average number of updates per frame. </param>
        /// <param name="maxSkeletonVertexMapDeltaCount"> Size of skeleton delta buffer. Pushes skeleton updates to gpu. Should be greater than the expected average number of updates per frame.  </param>
        /// <param name="InitialMaxInstanceCount"> Initial instance buffer size. (The buffer will increase dynamically with more instances- but it will never decrease) </param>
        /// <param name="InitialMaxSkeletonCount"> Initial skeleton buffer size. (The buffer will increase dynamically with more skeletons- but it will never decrease)</param>
        /// <param name=" override_default_type"> Override the default mesh type </param>
        /// <param name="MaxParentDepth"> Maximum allowed parent depth in heirarchy tree. Note* Each additional depth will add more gpu memory overhead. </param>
        public void Initialize(int maxDeltaCount = 10240,
            int InitialMaxInstanceCount = kThreadGroupX*2,
            int InitialMaxSkeletonCount=kThreadGroupX*2,
            Mesh override_mesh = null,
            Material override_material = null,
            int MaxParentDepth = 12,
            int NumSkeletonBones = 20,
            int pathCount=8,
            int InitialPropertyBufferSize=kThreadGroupX*2,
            int PropertyBufferMaxDeltaCount=10240,
            int InitialPathBufferSize=kThreadGroupX*2,
            int PathDeltaBufferSize=kThreadGroupX*200)
        {
            // Align to thread group to prevent out of bounds in shader
            int kInitialMaxCount = System.Math.Max(kThreadGroupX, InitialMaxInstanceCount + (kThreadGroupX - (InitialMaxInstanceCount % kThreadGroupX)));
            int kInitialMaxSkeletonCount = System.Math.Max(kThreadGroupX, InitialMaxSkeletonCount + (kThreadGroupX - (InitialMaxSkeletonCount % kThreadGroupX)));
            InitialPathBufferSize = System.Math.Max(kThreadGroupX, InitialPathBufferSize + (kThreadGroupX - (InitialPathBufferSize % kThreadGroupX)));
            PathDeltaBufferSize = System.Math.Max(kThreadGroupX, PathDeltaBufferSize + (kThreadGroupX - (PathDeltaBufferSize % kThreadGroupX)));
            InitialPropertyBufferSize = System.Math.Max(kThreadGroupX, InitialPropertyBufferSize + (kThreadGroupX - (InitialPropertyBufferSize % kThreadGroupX)));
            PropertyBufferMaxDeltaCount = System.Math.Max(kThreadGroupX, PropertyBufferMaxDeltaCount + (kThreadGroupX - (PropertyBufferMaxDeltaCount % kThreadGroupX)));

            if (this.Initialized)
                throw new System.Exception("Cannot reinitialize. Dispose first.");
            if (MaxParentDepth <= 0 || MaxParentDepth > 32)
                throw new System.Exception("Error invalid number of parents specified");
            this.MaxHierarchyDepth = MaxParentDepth;
            if (NumSkeletonBones <= 0 || NumSkeletonBones > 255) // Note* Num bones cannot exceed 8-bit number.
                throw new System.Exception("Error, bad skeleton bones amount.");
            this.NumSkeletonBones = NumSkeletonBones;

            this.IDs = new InstanceIDGenerator();
            this.PropertyIDs = new InstanceIDGenerator();
            this.GroupIDS = new InstanceIDGenerator(instancemesh.maxMeshTypes - 1);

            this.Initialized = true;
            this.instancemeshShader = MonoBehaviour.Instantiate(Resources.Load<ComputeShader>("GPUInstance/instancemesh"));
            if (this.instancemeshShader == null) throw new System.Exception("Error, failed to load instance mesh compute shader");

            this.cmd.SetExecutionFlags(UnityEngine.Rendering.CommandBufferExecutionFlags.AsyncCompute);
            this.cmd.name = "InstanceMeshCMDBuffer";

            //fix instance count
            this.MaxDeltaCount = System.Math.Max(kThreadGroupX, maxDeltaCount + (kThreadGroupX - (maxDeltaCount % kThreadGroupX)));

            // get kernels
            this.updateKernalID = this.instancemeshShader.FindKernel("UpdateBuffers");
            this.motionKernalID = this.instancemeshShader.FindKernel("MotionUpdate");
            this.calcObjectToWorldKernel = this.instancemeshShader.FindKernel("CalcObjectToWorld");
            this.updateHierarchyKernel = this.instancemeshShader.FindKernel("UpdateHierarchy");
            this.CopyToInstanceBuffersKernel = this.instancemeshShader.FindKernel("CopyToInstanceBuffers");
            this.CopyFromInstanceBuffersKernel = this.instancemeshShader.FindKernel("CopyFromInstanceBuffers");
            this.CopyToSkeletonBufferKernel = this.instancemeshShader.FindKernel("CopyToSkeletonBuffer");
            this.CopyFromSkeletonBufferKernel = this.instancemeshShader.FindKernel("CopyFromSkeletonBuffer");
            this.CopyToHierarchyBufferKernel = this.instancemeshShader.FindKernel("CopyToHierarchyBuffer");
            this.CopyFromHierarchyBufferKernel = this.instancemeshShader.FindKernel("CopyFromHierarchyBuffer");
            this.csum1KernalID = this.instancemeshShader.FindKernel("CSum1");
            this.csum2KernalID = this.instancemeshShader.FindKernel("CSum2");
            this.csum3KernalID = this.instancemeshShader.FindKernel("CSum3");
            this.csum4KernalID = this.instancemeshShader.FindKernel("CSum4");
            this.csum5KernalID = this.instancemeshShader.FindKernel("CSum5");
            this.IndirectIDUpdateKernel = this.instancemeshShader.FindKernel("IndirectIDUpdate");
            this.UpdateGroupBindPosesBufferKernel = this.instancemeshShader.FindKernel("UpdateGroupBindPosesBuffer");
            this.CopyToBindPoseBufferKernel = this.instancemeshShader.FindKernel("CopyToBindPoseBuffer");
            this.CopyFromBindPoseBufferKernel = this.instancemeshShader.FindKernel("CopyFromBindPoseBuffer");
            this.PrepareRenderedInstancesKernel = this.instancemeshShader.FindKernel("PrepareRenderedInstances");
            this.SelectRenderedInstancesKernel = this.instancemeshShader.FindKernel("SelectRenderedInstances");
            this.CalculateBoneMatricesKernel = this.instancemeshShader.FindKernel("CalculateBoneMatrices");
            this.UpdatePropertyBuffersKernel = this.instancemeshShader.FindKernel("UpdatePropertyBuffers");
            this.IndirectPropertyIDUpdateKernel = this.instancemeshShader.FindKernel("IndirectPropertyIDUpdate");
            this.CopyToPropertyBuffersKernel = this.instancemeshShader.FindKernel("CopyToPropertyBuffers");
            this.CopyFromPropertyBuffersKernel = this.instancemeshShader.FindKernel("CopyFromPropertyBuffers");
            this.PropertySimulationKernel = this.instancemeshShader.FindKernel("PropertySimulation");
            this.CopyToPathsBufferKernel = this.instancemeshShader.FindKernel("CopyToPathsBuffer");
            this.CopyFromPathsBufferKernel = this.instancemeshShader.FindKernel("CopyFromPathsBuffer");
            this.UpdatePathsBufferKernel = this.instancemeshShader.FindKernel("UpdatePathsBuffer");

            //set transform buffers
            this.transformBuffer = new ComputeBuffer(kInitialMaxCount, instance_data.kByteStride);
            this.transformBuffer.SetData(new instance_data[kInitialMaxCount]); // initialize buffer
            this.instancemeshShader.SetBuffer(this.updateKernalID, "transformBuffer", this.transformBuffer);
            this.instancemeshShader.SetBuffer(this.calcObjectToWorldKernel, "transformBuffer", this.transformBuffer);
            this.instancemeshShader.SetBuffer(this.motionKernalID, "transformBuffer", this.transformBuffer);
            this.instancemeshShader.SetBuffer(this.CopyToInstanceBuffersKernel, "transformBuffer", this.transformBuffer);
            this.instancemeshShader.SetBuffer(this.CopyFromInstanceBuffersKernel, "transformBuffer", this.transformBuffer);
            this.instancemeshShader.SetBuffer(this.SelectRenderedInstancesKernel, "transformBuffer", this.transformBuffer);
            this.instancemeshShader.SetBuffer(this.CalculateBoneMatricesKernel, "transformBuffer", this.transformBuffer);
            this.instancemeshShader.SetBuffer(this.PrepareRenderedInstancesKernel, "transformBuffer", this.transformBuffer);
            this.instancemeshShader.SetBuffer(this.PropertySimulationKernel, "transformBuffer", this.transformBuffer);

            //set indirect args buffer
            this.argsBuffer = new ComputeBuffer(instancemesh.maxMeshTypes * instancemesh.kIndirectArgCountPerGroup, sizeof(uint), ComputeBufferType.IndirectArguments);
            this.argsBuffer.SetData(args = new uint[instancemesh.maxMeshTypes * instancemesh.kIndirectArgCountPerGroup]); // initialize buffer
            this.instancemeshShader.SetBuffer(this.updateKernalID, "argsBuffer", this.argsBuffer);
            this.instancemeshShader.SetBuffer(this.PrepareRenderedInstancesKernel, "argsBuffer", this.argsBuffer);
            this.instancemeshShader.SetBuffer(this.SelectRenderedInstancesKernel, "argsBuffer", this.argsBuffer);
            this.instancemeshShader.SetBuffer(this.csum1KernalID, "argsBuffer", this.argsBuffer);

            //set delta buffer
            this._delta_buffer = new InstanceMeshDeltaBuffer<instance_delta>(kInitialMaxCount, this.MaxDeltaCount, instancemesh.maxMeshTypes, TrackGroupCounts: true);
            this.deltaBuffer = new ComputeBuffer(this.MaxDeltaCount, instance_delta.kByteStride);
            this.deltaBuffer.SetData(new instance_delta[this.MaxDeltaCount]);
            this.instancemeshShader.SetBuffer(this.updateKernalID, "deltaBuffer", this.deltaBuffer);

            //build group data buffers
            this.groupDataBuffer = new ComputeBuffer(instancemesh.maxMeshTypes, kGroupDataStride);
            this.groupDataBuffer.SetData(new int[instancemesh.maxMeshTypes]);
            this.instancemeshShader.SetBuffer(this.updateKernalID, "groupDataBuffer", this.groupDataBuffer);
            this.instancemeshShader.SetBuffer(this.csum1KernalID, "groupDataBuffer", this.groupDataBuffer);
            this.instancemeshShader.SetBuffer(this.csum2KernalID, "groupDataBuffer", this.groupDataBuffer);
            this.instancemeshShader.SetBuffer(this.csum3KernalID, "groupDataBuffer", this.groupDataBuffer);
            this.instancemeshShader.SetBuffer(this.csum4KernalID, "groupDataBuffer", this.groupDataBuffer);
            this.instancemeshShader.SetBuffer(this.csum5KernalID, "groupDataBuffer", this.groupDataBuffer);
            this.instancemeshShader.SetBuffer(this.SelectRenderedInstancesKernel, "groupDataBuffer", this.groupDataBuffer);
            this.instancemeshShader.SetBuffer(this.PrepareRenderedInstancesKernel, "groupDataBuffer", this.groupDataBuffer);

            //build camera frustum buffer
            this.cameraFrustumBuffer = new ComputeBuffer(frustum_count, sizeof(float) * 3);
            this.cameraFrustumBuffer.SetData(this.frustumArr = new Vector3[frustum_count]);
            this.instancemeshShader.SetBuffer(this.SelectRenderedInstancesKernel, "cameraFrustumBuffer", this.cameraFrustumBuffer);

            //set animations buffer to have only one element[0] = NULL
            this.animationsBuffer = new ComputeBuffer(1, sizeof(float));
            this.animationsBuffer.SetData(new float[1]);
            this.instancemeshShader.SetBuffer(this.motionKernalID, "animationsBuffer", this.animationsBuffer);
            this.instancemeshShader.SetBuffer(this.PropertySimulationKernel, "animationsBuffer", this.animationsBuffer);

            // set anim buffer to one element (this is done to satisfy unity constraints that buffers for kernels must be set, even if unused)
            this.animToBoneAnimMapBuffer = new ComputeBuffer(1, sizeof(int));
            this.animToBoneAnimMapBuffer.SetData(new int[1]);
            this.instancemeshShader.SetBuffer(this.motionKernalID, "animToBoneAnimMapBuffer", this.animToBoneAnimMapBuffer);
            this.instancemeshShader.SetBuffer(this.PropertySimulationKernel, "animToBoneAnimMapBuffer", this.animToBoneAnimMapBuffer);

            //set texture animations buffer to have only one element[0] = NULL
            this.textureAnimationsBuffer = new ComputeBuffer(1, sizeof(float));
            this.textureAnimationsBuffer.SetData(new float[1]);
            this.instancemeshShader.SetBuffer(this.PropertySimulationKernel, "textureAnimationsBuffer", this.textureAnimationsBuffer);

            // Set hierarchy stuff
            this.hierarchy = new InstanceHierarchyMap(kInitialMaxCount, this.MaxDeltaCount, this.MaxHierarchyDepth, instancemesh.kThreadGroupX);
            this.hierarchyDeltaBuffer = new ComputeBuffer(this.MaxDeltaCount, InstanceHierarchyMap.kHierarchyDeltaStride);
            this.hierarchyDeltaBuffer.SetData(this.hierarchy._delta_buffer.Array);
            this.instancemeshShader.SetBuffer(this.updateHierarchyKernel, "hierarchyDeltaBuffer", this.hierarchyDeltaBuffer);

            this.hierarchyDepthBuffer = new ComputeBuffer(this.hierarchy.MaxInstancesPerDepth * this.hierarchy.MaxDepth, sizeof(int));
            this.hierarchyDepthBuffer.SetData(new int[hierarchy.NumHierarchyIndices]); // index data length should be instancecount * depth
            this.instancemeshShader.SetBuffer(this.updateHierarchyKernel, "hierarchyDepthBuffer", this.hierarchyDepthBuffer);
            this.instancemeshShader.SetBuffer(this.calcObjectToWorldKernel, "hierarchyDepthBuffer", this.hierarchyDepthBuffer);
            this.instancemeshShader.SetBuffer(this.CopyToHierarchyBufferKernel, "hierarchyDepthBuffer", this.hierarchyDepthBuffer);
            this.instancemeshShader.SetBuffer(this.CopyFromHierarchyBufferKernel, "hierarchyDepthBuffer", this.hierarchyDepthBuffer);

            // object2world buffer
            this.object2WorldBuffer = new ComputeBuffer(kInitialMaxCount*2, sizeof(float) * 4 * 4);
            this.object2WorldBuffer.SetData(new float[kInitialMaxCount * 2 * 4 * 4]);
            this.instancemeshShader.SetBuffer(this.calcObjectToWorldKernel, "object2WorldBuffer", this.object2WorldBuffer);
            this.instancemeshShader.SetBuffer(this.CopyToInstanceBuffersKernel, "object2WorldBuffer", this.object2WorldBuffer);
            this.instancemeshShader.SetBuffer(this.CopyFromInstanceBuffersKernel, "object2WorldBuffer", this.object2WorldBuffer);
            this.instancemeshShader.SetBuffer(this.SelectRenderedInstancesKernel, "object2WorldBuffer", this.object2WorldBuffer);
            this.instancemeshShader.SetBuffer(this.CalculateBoneMatricesKernel, "object2WorldBuffer", this.object2WorldBuffer);
            this.instancemeshShader.SetBuffer(this.PrepareRenderedInstancesKernel, "object2WorldBuffer", this.object2WorldBuffer);

            //add instanceIDBuffer
            this.instanceIDBuffer = new ComputeBuffer(kInitialMaxCount, sizeof(int));
            this.instanceIDBuffer.SetData(new int[kInitialMaxCount]);
            this.instancemeshShader.SetBuffer(this.PrepareRenderedInstancesKernel, "instanceIDBuffer", this.instanceIDBuffer);
            this.instancemeshShader.SetBuffer(this.CopyToInstanceBuffersKernel, "instanceIDBuffer", this.instanceIDBuffer);
            this.instancemeshShader.SetBuffer(this.CopyFromInstanceBuffersKernel, "instanceIDBuffer", this.instanceIDBuffer);

            // indirect id buffer
            this.indirectInstanceIDBuffer = new ComputeBuffer(kInitialMaxCount, sizeof(int));
            this.indirectInstanceIDBuffer.SetData(new int[kInitialMaxCount]);
            this.instancemeshShader.SetBuffer(this.SelectRenderedInstancesKernel, "indirectInstanceIDBuffer", this.indirectInstanceIDBuffer);
            this.instancemeshShader.SetBuffer(this.motionKernalID, "indirectInstanceIDBuffer", this.indirectInstanceIDBuffer);
            this.instancemeshShader.SetBuffer(this.IndirectIDUpdateKernel, "indirectInstanceIDBuffer", this.indirectInstanceIDBuffer);
            this.instancemeshShader.SetBuffer(this.CopyFromInstanceBuffersKernel, "indirectInstanceIDBuffer", this.indirectInstanceIDBuffer);
            this.instancemeshShader.SetBuffer(this.CopyToInstanceBuffersKernel, "indirectInstanceIDBuffer", this.indirectInstanceIDBuffer);
            this.instancemeshShader.SetBuffer(this.PrepareRenderedInstancesKernel, "indirectInstanceIDBuffer", this.indirectInstanceIDBuffer);
            this.instancemeshShader.SetBuffer(this.CalculateBoneMatricesKernel, "indirectInstanceIDBuffer", this.indirectInstanceIDBuffer);

            // indirect id delta buffer
            this.indirectInstanceIDDeltaBuffer = new ComputeBuffer(this.MaxDeltaCount, sizeof(int) * 2);
            this.indirectInstanceIDDeltaBuffer.SetData(new int[this.MaxDeltaCount*2]);
            this.instancemeshShader.SetBuffer(this.IndirectIDUpdateKernel, "indirectInstanceIDDeltaBuffer", this.indirectInstanceIDDeltaBuffer);

            // set bind pose buffers
            const int kInitBindCount = 128; // Note* must be divisible by threadgroupx
            if (kInitBindCount % kThreadGroupX != 0) throw new System.Exception("Bind count buffer size sanity check");
            this.groupBindPosesBuffer = new ComputeBuffer(kInitBindCount * this.NumSkeletonBones, GPUInstanceBoneMatrix4x4.kByteStride); // matrix for each bone in skeleton
            this.groupBindPosesBuffer.SetData(new GPUInstanceBoneMatrix4x4[kInitBindCount * this.NumSkeletonBones]);
            this.instancemeshShader.SetBuffer(this.UpdateGroupBindPosesBufferKernel, "groupBindPosesBuffer", this.groupBindPosesBuffer);
            this.instancemeshShader.SetBuffer(this.CopyToBindPoseBufferKernel, "groupBindPosesBuffer", this.groupBindPosesBuffer);
            this.instancemeshShader.SetBuffer(this.CopyFromBindPoseBufferKernel, "groupBindPosesBuffer", this.groupBindPosesBuffer);
            this.instancemeshShader.SetBuffer(this.CalculateBoneMatricesKernel, "groupBindPosesBuffer", this.groupBindPosesBuffer);

            this.groupBindPosesDeltaBuffer = new ComputeBuffer(kInitBindCount * this.BindPoseDeltaNumFloats, sizeof(float)); // matrix for each bone + extra integer
            this.groupBindPosesDeltaBuffer.SetData(new float[kInitBindCount * this.BindPoseDeltaNumFloats]);
            this.instancemeshShader.SetBuffer(this.UpdateGroupBindPosesBufferKernel, "groupBindPosesDeltaBuffer", this.groupBindPosesDeltaBuffer);
            this._bind_pose_delta_buffer = new GroupBindPosesDeltaBuffer(128, this.BindPoseDeltaNumFloats, this.NumSkeletonBones);

            // set bone matrices buffer
            this._skeletons = new SkeletonIDMap(this.NumSkeletonBones);
            this.boneMatricesBuffer = new ComputeBuffer(this.NumSkeletonBones * kInitialMaxSkeletonCount, GPUInstanceBoneMatrix4x4.kByteStride);
            this.boneMatricesBuffer.SetData(new GPUInstanceBoneMatrix4x4[this.NumSkeletonBones * kInitialMaxSkeletonCount]);
            this.instancemeshShader.SetBuffer(this.CopyToSkeletonBufferKernel, "boneMatricesBuffer", this.boneMatricesBuffer);
            this.instancemeshShader.SetBuffer(this.CopyFromSkeletonBufferKernel, "boneMatricesBuffer", this.boneMatricesBuffer);
            this.instancemeshShader.SetBuffer(this.CalculateBoneMatricesKernel, "boneMatricesBuffer", this.boneMatricesBuffer);

            // set the LOD buffer
            this.groupLODBuffer = new ComputeBuffer(instancemesh.maxMeshTypes * groupLODStride(), sizeof(uint));
            this.groupLODBuffer.SetData(this.groupLOD = new uint[groupLODBuffer.count]);
            instancemeshShader.SetBuffer(this.SelectRenderedInstancesKernel, "groupLODBuffer", this.groupLODBuffer);

            // properties buffer
            this.propertyBuffer = new ComputeBuffer(InitialPropertyBufferSize, instance_properties.kByteStride);
            this.propertyBuffer.SetData(new instance_properties[InitialPropertyBufferSize]);
            this.instancemeshShader.SetBuffer(this.UpdatePropertyBuffersKernel, "propertyBuffer", this.propertyBuffer);
            this.instancemeshShader.SetBuffer(this.CopyToPropertyBuffersKernel, "propertyBuffer", this.propertyBuffer);
            this.instancemeshShader.SetBuffer(this.CopyFromPropertyBuffersKernel, "propertyBuffer", this.propertyBuffer);
            this.instancemeshShader.SetBuffer(this.PropertySimulationKernel, "propertyBuffer", this.propertyBuffer);
            this.instancemeshShader.SetBuffer(this.motionKernalID, "propertyBuffer", this.propertyBuffer);

            // properties delta buffer
            this._property_delta_buffer = new InstanceMeshDeltaBuffer<instance_properties_delta>(InitialPropertyBufferSize, PropertyBufferMaxDeltaCount, instancemesh.maxMeshTypes, TrackGroupCounts: false);
            this.propertyDeltaBuffer = new ComputeBuffer(PropertyBufferMaxDeltaCount, instance_properties_delta.kByteStride);
            this.propertyDeltaBuffer.SetData(new instance_properties_delta[PropertyBufferMaxDeltaCount]);
            this.instancemeshShader.SetBuffer(this.UpdatePropertyBuffersKernel, "propertyDeltaBuffer", this.propertyDeltaBuffer);

            // indirect property id buffer
            this.indirectPropertyIDBuffer = new ComputeBuffer(InitialPropertyBufferSize, sizeof(int));
            this.indirectPropertyIDBuffer.SetData(new int[InitialPropertyBufferSize]);
            this.instancemeshShader.SetBuffer(this.IndirectPropertyIDUpdateKernel, "indirectPropertyIDBuffer", this.indirectPropertyIDBuffer);
            this.instancemeshShader.SetBuffer(this.CopyToPropertyBuffersKernel, "indirectPropertyIDBuffer", this.indirectPropertyIDBuffer);
            this.instancemeshShader.SetBuffer(this.CopyFromPropertyBuffersKernel, "indirectPropertyIDBuffer", this.indirectPropertyIDBuffer);
            this.instancemeshShader.SetBuffer(this.PropertySimulationKernel, "indirectPropertyIDBuffer", this.indirectPropertyIDBuffer);

            // indirect property id delta buffer
            this.indirectPropertyIDDeltaBuffer = new ComputeBuffer(PropertyBufferMaxDeltaCount, sizeof(int) * 2);
            this.indirectPropertyIDDeltaBuffer.SetData(new int[PropertyBufferMaxDeltaCount * 2]);
            this.instancemeshShader.SetBuffer(this.IndirectPropertyIDUpdateKernel, "indirectPropertyIDDeltaBuffer", this.indirectPropertyIDDeltaBuffer);

            // Paths buffer
            this._paths = new InstanceMeshPropertyPaths(pathCount, PathDeltaBufferSize);
            this.pathsBuffer = new ComputeBuffer(InitialPathBufferSize * this._paths.PathFloatStride, sizeof(float));
            this.pathsBuffer.SetData(new float[InitialPathBufferSize * this._paths.PathFloatStride]);
            this.instancemeshShader.SetBuffer(this.CopyToPathsBufferKernel, "pathsBuffer", this.pathsBuffer);
            this.instancemeshShader.SetBuffer(this.CopyFromPathsBufferKernel, "pathsBuffer", this.pathsBuffer);
            this.instancemeshShader.SetBuffer(this.UpdatePathsBufferKernel, "pathsBuffer", this.pathsBuffer);
            this.instancemeshShader.SetBuffer(this.PropertySimulationKernel, "pathsBuffer", this.pathsBuffer);

            // Paths delta buffers
            this.pathsDeltaBuffer = new ComputeBuffer(PathDeltaBufferSize * this._paths.PathFloatStride, sizeof(float));
            this.pathsDeltaBuffer.SetData(new float[PathDeltaBufferSize * this._paths.PathFloatStride]);
            this.instancemeshShader.SetBuffer(this.UpdatePathsBufferKernel, "pathsDeltaBuffer", this.pathsDeltaBuffer);
            this.pathsDeltaIDBuffer = new ComputeBuffer(PathDeltaBufferSize, sizeof(int));
            this.pathsDeltaIDBuffer.SetData(new int[PathDeltaBufferSize]);
            this.instancemeshShader.SetBuffer(this.UpdatePathsBufferKernel, "pathsDeltaIDBuffer", this.pathsDeltaIDBuffer);

            // Set some values that will never change
            instancemeshShader.SetInt("GROUP_COUNT", instancemesh.maxMeshTypes);
            instancemeshShader.SetInt("HIERARCHY_DEPTH", this.hierarchy.MaxDepth);
            instancemeshShader.SetInt("SKELETON_BONE_COUNT", this.NumSkeletonBones);
            instancemeshShader.SetInt("PATH_COUNT", this._paths.PathCount);

            var mesh = override_mesh != null ? override_mesh : BaseMeshLibrary.CreateDefault();
            var material = override_material != null ? override_material : BaseMaterialLibrary.CreateDefault();

            // assign main thread (initialize will crash in any other thread than the unity main thread)
            this._main_thread = System.Threading.Thread.CurrentThread;

            //add default
            this.Default = AddInstancedMesh(mesh, material, mode: UnityEngine.Rendering.ShadowCastingMode.Off, receive_shadows: false);
        }

        /// <summary>
        /// [Main Thread] Set the entire animations buffer. This will also assign an AnimationID to every BoneAnimation instance in every controller.
        /// </summary>
        /// <param name="all_controllers"></param>
        public void InitializeAnimationsBuffer(AnimationController[] all_controllers)
        {
            AssertInitializedAndMainThread();

            this._controllers = new HashSet<AnimationController>();
            foreach (var c in all_controllers)
            {
                if (c == null)
                    throw new System.Exception("Error, tried to add a null animation controller");
                if (!c.IsIntialized)
                    throw new System.Exception("Error, tried to add uninitialized animation controller");
                if (this._controllers.Contains(c))
                    throw new System.Exception("Error, don't added duplicate Animation Controllers. Animation controllers are considered the same if they use the same Unity Animation Controller");
                this._controllers.Add(c);
            }

            //copy all controller animation data into a buffer
            int buffer_len = 0;
            for (int i = 0; i < all_controllers.Length; i++)
                for (int j = 0; j < all_controllers[i].bone_animations.Length; j++)
                    buffer_len += all_controllers[i].bone_animations[j].data.Length;

            float[] buffer_data = new float[buffer_len + 1]; //add one for NULL
            SimpleList<int> bone_anim_map = new SimpleList<int>(this.NumSkeletonBones * all_controllers.Length * all_controllers[0].animations.Length);
            bone_anim_map.Add(0); // =0 is null
            int n = 1; //start at n=1 to avoid NULL
            for (int i = 0; i < all_controllers.Length; i++)
            {
                var controller = all_controllers[i];

                // Init bone animations buffer
                for (int j = 0; j < controller.bone_animations.Length; j++)
                {
                    controller.bone_animations[j].data.CopyTo(buffer_data, n); //copy data
                    controller.bone_animations[j].SetID(n);  //set start index as id
                    n += controller.bone_animations[j].data.Length; //increment start index
                }

                // Init animation id -> bone animation id map buffer
                for (int j = 0; j < controller.animations.Length; j++)
                {
                    int anim_id = bone_anim_map.Count;
                    var animation = controller.animations[j];
                    animation.SetGPUAnimID(anim_id); // set anim id for animation
                    
                    for (int k = 0; k < animation.boneAnimations.Length; k++) // now map bone ids for animation id
                    {
                        bone_anim_map.Add(animation.boneAnimations[k].id);
                    }
                }
            }
            bone_anim_map.Resize(bone_anim_map.Count); // trim

            //animation buffer | index(0) = NULL | 4 bytes, num keyframes = N | animation transform, 1...N * 40 bytes | 4 bytes, num keyframes = N | animation transform, 1...N * 40 bytes |  etc... 
            if (this.animationsBuffer != null)
                this.animationsBuffer.Release();
            if (this.animToBoneAnimMapBuffer != null)
                this.animToBoneAnimMapBuffer.Release();

            this.animationsBuffer = new ComputeBuffer(buffer_data.Length, sizeof(float));
            this.animationsBuffer.SetData(buffer_data);
            this.instancemeshShader.SetBuffer(this.motionKernalID, "animationsBuffer", this.animationsBuffer);
            this.instancemeshShader.SetBuffer(this.PropertySimulationKernel, "animationsBuffer", this.animationsBuffer);

            this.animToBoneAnimMapBuffer = new ComputeBuffer(bone_anim_map.Count, sizeof(int));
            this.animToBoneAnimMapBuffer.SetData(bone_anim_map.Array);
            this.instancemeshShader.SetBuffer(this.motionKernalID, "animToBoneAnimMapBuffer", this.animToBoneAnimMapBuffer);
            this.instancemeshShader.SetBuffer(this.PropertySimulationKernel, "animToBoneAnimMapBuffer", this.animToBoneAnimMapBuffer);
        }

        /// <summary>
        /// [Main Thread] Set the entire texture animations buffer. 
        /// </summary>
        /// <param name="all_controllers"></param>
        public void InitializeTextureAnimationsBuffer(TextureAnimationLibrary lib)
        {
            AssertInitializedAndMainThread();

            if (this.textureAnimationsBuffer != null)
                this.textureAnimationsBuffer.Release();
            this.textureAnimationsBuffer = new ComputeBuffer(lib.TextureAnimationBuffer.Length, sizeof(float));
            this.textureAnimationsBuffer.SetData(lib.TextureAnimationBuffer);
            this.instancemeshShader.SetBuffer(this.PropertySimulationKernel, "textureAnimationsBuffer", this.textureAnimationsBuffer);
        }

        /// <summary>
        /// [Main Thread] Add a new mesh type.
        /// </summary>
        /// <param name="type"></param>
        public MeshType AddInstancedMesh(Mesh mesh, Material mat, UnityEngine.Rendering.ShadowCastingMode mode = UnityEngine.Rendering.ShadowCastingMode.Off, bool receive_shadows = false)
        {
            AssertInitializedAndMainThread();

            if (mat == null || mesh == null)
                throw new System.Exception("Error, null input args");

            if (mesh.subMeshCount > 1)
                throw new System.Exception("Error, sub-mesh is not supported");

            if (meshtypes.ContainsKey(new MeshTypeKey(mesh, mat)))
                throw new System.Exception("Error, already contain input mesh type");

            if (this.GroupIDS.InstanceCount >= instancemesh.maxMeshTypes)
                throw new System.Exception("Error, hit MaxMeshType count!");

            if (meshtypes.Count >= instancemesh.maxMeshTypes)
                throw new System.Exception("Hit Max Mesh Type Limit of: " + MaxMeshTypes.ToString());

            // set indirect args buffer
            ushort groupID = (ushort)this.GroupIDS.GetNewID();
            int argsIntOffset = ((int)groupID) * kIndirectArgCountPerGroup;
            var type_data = new MeshTypeData(argsIntOffset, groupID, MonoBehaviour.Instantiate(mat));
            this.args[type_data.argsIntOffset + 0] = (mesh != null) ? (uint)mesh.GetIndexCount(0) : 0; //todo: get submeshindex?
            //args[type_data.argsIntOffset + 1] = decided by the compute shader, dont initialize
            this.args[type_data.argsIntOffset + 2] = (mesh != null) ? (uint)mesh.GetIndexStart(0) : 0;
            this.args[type_data.argsIntOffset + 3] = (mesh != null) ? (uint)mesh.GetBaseVertex(0) : 0;

            // set groupLOD buffer (all LOD levels to self).
            var g_lod_index = groupLODIndex(type_data.groupID);
            for (int i = 0; i < instancemesh.NumLODLevels; i++)
                this.groupLOD[g_lod_index + i] = type_data.groupID;
            // set LOD0 ratio to 100%, others to 0
            for (int i = 1; i < instancemesh.NumLODLevels; i++)
                this.groupLOD[g_lod_index + instancemesh.NumLODLevels + i] = 0;
            this.groupLOD[g_lod_index + instancemesh.NumLODLevels] = LODRatio2Uint(1.0f);

            // Add to group id update set (note* it is possible for it to already exist if the user is adding/deleting a MeshType with the same ID in the same frame!)
            this.pending_group_updates.Add(type_data.groupID);
            this.pending_lod_group_updates.Add(type_data.groupID);

            // create new mesh type
            var type = new MeshType(mes: mesh, mat: mat, groupID: groupID, mData: type_data, castShadows: mode, receiveShadows: receive_shadows);

            //set instanceID buffer
            type_data.InstancedMaterial.SetBuffer("instanceIDBuffer", this.instanceIDBuffer);
            //set group data buffer
            type_data.InstancedMaterial.SetBuffer("groupDataBuffer", this.groupDataBuffer);
            //set group ID
            type_data.InstancedMaterial.SetInt("groupID", type_data.groupID);
            //set instance data buffer
            type_data.InstancedMaterial.SetBuffer("transformBuffer", this.transformBuffer);
            //Set Object2World buffer
            type_data.InstancedMaterial.SetBuffer("object2WorldBuffer", this.object2WorldBuffer);
            // set properties
            type_data.InstancedMaterial.SetBuffer("propertyBuffer", this.propertyBuffer);

            // Check if this mesh has bones associated with it
            if (type.IsSkinnedMesh())
            {
                if (type.shared_mesh.bindposes.Length > this.NumSkeletonBones)
                    throw new System.Exception("Error, the input number of bones from skinned mesh is greater than the number of allocated bones allowed");

                this._bind_pose_delta_buffer.AddPose(type_data.groupID, type.shared_mesh.bindposes); // add bind pose to bind pose buffer

                type_data.InstancedMaterial.SetBuffer("boneMatricesBuffer", this.boneMatricesBuffer); // set bone matrices buffer

                // Set blend weights level
                var bw = QualitySettings.skinWeights;
                type.SetBlendWeights(bw);
            }

            //add to dictionary
            this.meshtypes.Add(new MeshTypeKey(type.shared_mesh, type.shared_material), type);

            return type;
        }

        /// <summary>
        /// [Main Thread] Associate instances through LOD level
        /// </summary>
        /// <param name="lod0"></param>
        /// <param name="lod1"></param>
        /// <param name="lod2"></param>
        /// <param name="lod3"></param>
        /// <param name="lod4"></param>
        public void AddInstanceLOD(MeshType lod0, MeshType lod1, MeshType lod2, MeshType lod3, MeshType lod4, float[] lod_ratios)
        {
            AssertInitializedAndMainThread();

            if (ReferenceEquals(null, lod0) || ReferenceEquals(null, lod1) || ReferenceEquals(null, lod2) || ReferenceEquals(null, lod3) || ReferenceEquals(null, lod4))
                throw new System.Exception("Error, cannot assign a null meshtype as LOD");

            if (!lod0.Initialized || !lod1.Initialized || !lod2.Initialized || !lod3.Initialized || !lod4.Initialized)
                throw new System.Exception("Error, input LOD MeshTypes must be initialized");

            void UpdateGroupLOD(ushort groupID)
            {
                var index = groupLODIndex(groupID);
                this.groupLOD[index] =   lod0.groupID;
                this.groupLOD[index+1] = lod1.groupID;
                this.groupLOD[index+2] = lod2.groupID;
                this.groupLOD[index+3] = lod3.groupID;
                this.groupLOD[index+4] = lod4.groupID;

                var end = groupLODStride() + index;
                for (int i = 0; i < instancemesh.NumLODLevels; i++)
                {
                    if (lod_ratios[i] < 0)
                        throw new System.Exception("Error, invalid input lod ratios");
                    this.groupLOD[index + instancemesh.NumLODLevels + i] = LODRatio2Uint(lod_ratios[i]);
                }

                this.pending_lod_group_updates.Add(lod0.groupID);
            }

            UpdateGroupLOD(lod0.groupID);
            UpdateGroupLOD(lod1.groupID);
            UpdateGroupLOD(lod2.groupID);
            UpdateGroupLOD(lod3.groupID);
            UpdateGroupLOD(lod4.groupID);
        }

        /// <summary>
        /// [Main Thread] dispose of this object
        /// </summary>
        public void Dispose()
        {
            AssertInitializedAndMainThread();

            //dispose
            if (this.deltaBuffer != null)
                this.deltaBuffer.Release();
            if (this.transformBuffer != null)
                this.transformBuffer.Release();
            if (this.argsBuffer != null)
                this.argsBuffer.Release();
            if (this.groupDataBuffer != null)
                this.groupDataBuffer.Release();
            if (this.cameraFrustumBuffer != null)
                this.cameraFrustumBuffer.Release();
            if (this.instanceIDBuffer != null)
                this.instanceIDBuffer.Release();
            if (this.animationsBuffer != null)
                this.animationsBuffer.Release();
            if (this.animToBoneAnimMapBuffer != null)
                this.animToBoneAnimMapBuffer.Release();
            if (this.textureAnimationsBuffer != null)
                this.textureAnimationsBuffer.Release();
            if (this.hierarchyDeltaBuffer != null)
                this.hierarchyDeltaBuffer.Release();
            if (this.hierarchyDepthBuffer != null)
                this.hierarchyDepthBuffer.Release();
            if (this.object2WorldBuffer != null)
                this.object2WorldBuffer.Release();
            if (this.indirectInstanceIDBuffer != null)
                this.indirectInstanceIDBuffer.Release();
            if (this.indirectInstanceIDDeltaBuffer != null)
                this.indirectInstanceIDDeltaBuffer.Release();
            if (this.groupBindPosesBuffer != null)
                this.groupBindPosesBuffer.Release();
            if (this.groupBindPosesDeltaBuffer != null)
                this.groupBindPosesDeltaBuffer.Release();
            if (this.boneMatricesBuffer != null)
                this.boneMatricesBuffer.Release();
            if (this.computeIndirectArgsBuffer != null)
                this.computeIndirectArgsBuffer.Release();
            if (this.groupLODBuffer != null)
                this.groupLODBuffer.Release();
            if (this.propertyBuffer != null)
                this.propertyBuffer.Release();
            if (this.propertyDeltaBuffer != null)
                this.propertyDeltaBuffer.Release();
            if (this.indirectPropertyIDBuffer != null)
                this.indirectPropertyIDBuffer.Release();
            if (this.indirectPropertyIDDeltaBuffer != null)
                this.indirectPropertyIDDeltaBuffer.Release();
            if (this.pathsBuffer != null)
                this.pathsBuffer.Release();
            if (this.pathsDeltaBuffer != null)
                this.pathsDeltaBuffer.Release();
            if (this.pathsDeltaIDBuffer != null)
                this.pathsDeltaIDBuffer.Release();

            //reset everything
            this.Initialized = false;
            this.Default = null;

            if (this._delta_buffer != null)
                this._delta_buffer.Dispose();
            this._delta_buffer = null;

            if (this._property_delta_buffer != null)
                this._property_delta_buffer.Dispose();
            this._property_delta_buffer = null;

            if (this._skeletons != null)
                this._skeletons.Dispose();
            this._skeletons = null;

            if (this._bind_pose_delta_buffer != null)
                this._bind_pose_delta_buffer.Dispose();
            this._bind_pose_delta_buffer = null;

            if (this._paths != null)
                this._paths.Dispose();
            this._paths = null;

            this.meshtypes = new Dictionary<MeshTypeKey, MeshType>(); //mesh types being drawn
            this._controllers = new HashSet<AnimationController>();

            this.pending_group_updates = new HashSet<ushort>();
            this.pending_group_updates_queue = new Queue<ushort>();
            this.pending_lod_group_updates = new HashSet<ushort>();
            this.pending_lod_group_updates_queue = new Queue<ushort>();

            this.frustumArr = null; //camera frustum array

            this.args = null; //indirect arguments

            this.FrustumCamera = null;

            this.MaxDeltaCount = 0;

            this._main_thread = null;

            this.IDs = null;
            this.PropertyIDs = null;
            this.GroupIDS = null;

            if (this._ticks_stopwatch.IsRunning) this._ticks_stopwatch.Stop();
            this._ticks_stopwatch.Reset();

            if (this.object2WorldResizeBuffer != null || this.transformResizeBuffer != null || this.instanceIDResizeBuffer != null ||
                this.hierarchyDepthResizeBuffer != null || this.indirectInstanceIDResizeBuffer != null ||
                this.groupBindPosesResizeBuffer != null || this.boneMatricesResizeBuffer != null || this.propertyResizeBuffer != null || 
                this.indirectPropertyIDResizeBuffer != null || this.pathsResizeBuffer != null)
                throw new System.Exception("Error, a temp buffer should have been deallocated already!!");
        }

        /// <summary>
        /// dynamically resize buffers if there isnt enough space. Ideally this wouldn't but needed... but for maximum easiness for the users... it is here
        /// </summary>
        void try_resize_compute_buffers()
        {
            int next_buffer_size(in int size_needed)
            {
                var add_amount = System.Math.Min(size_needed, 500000); // maximum increments of 500k instances
                var new_size = size_needed + add_amount; // the new size will be larger than the needed size
                new_size = new_size + (kThreadGroupX - (new_size % kThreadGroupX)); // make divisible by num thread groups
                return new_size;
            }

            //Note* command buffer should be empty

            // resize bind pose buffer if needed
            if (this.meshtypes.Count * this.NumSkeletonBones > this.groupBindPosesBuffer.count) // stride of bind pose buffer is in bones (here we check if num types*skeleton size is larger than bind pose buffer bone count)
            {
                var old_count = this.groupBindPosesBuffer.count; // count is number of bones
                if (old_count % kThreadGroupX != 0) throw new System.Exception("During skeleton buffer resize, old buffer did not have correct size");

                this.groupBindPosesResizeBuffer = new ComputeBuffer(old_count, GPUInstanceBoneMatrix4x4.kByteStride); // make new buffer
                this.groupBindPosesResizeBuffer.SetData(new GPUInstanceBoneMatrix4x4[old_count]); // gpu init
                instancemeshShader.SetBuffer(CopyFromBindPoseBufferKernel, "groupBindPosesResizeBuffer", this.groupBindPosesResizeBuffer); // set kernel
                instancemeshShader.SetBuffer(CopyToBindPoseBufferKernel, "groupBindPosesResizeBuffer", this.groupBindPosesResizeBuffer); // set kernel

                this.cmd.SetExecutionFlags(UnityEngine.Rendering.CommandBufferExecutionFlags.None); // run compute shaders
                this.cmd.DispatchCompute(this.instancemeshShader, this.CopyFromBindPoseBufferKernel, old_count / kThreadGroupX, 1, 1); // dispatch
                Graphics.ExecuteCommandBuffer(this.cmd);
                this.cmd.Clear();

                var new_count = next_buffer_size(this.meshtypes.Count * this.NumSkeletonBones); // get new buffer len
                if (new_count % kThreadGroupX != 0) throw new System.Exception("During bind pose buffer resize, new buffer did not have correct size");

                this.groupBindPosesBuffer.Release();
                this.groupBindPosesBuffer = new ComputeBuffer(new_count, GPUInstanceBoneMatrix4x4.kByteStride);
                this.groupBindPosesBuffer.SetData(new GPUInstanceBoneMatrix4x4[new_count]); // gpu init
                this.instancemeshShader.SetBuffer(this.UpdateGroupBindPosesBufferKernel, "groupBindPosesBuffer", this.groupBindPosesBuffer);
                this.instancemeshShader.SetBuffer(this.CopyToBindPoseBufferKernel, "groupBindPosesBuffer", this.groupBindPosesBuffer);
                this.instancemeshShader.SetBuffer(this.CopyFromBindPoseBufferKernel, "groupBindPosesBuffer", this.groupBindPosesBuffer);
                this.instancemeshShader.SetBuffer(this.CalculateBoneMatricesKernel, "groupBindPosesBuffer", this.groupBindPosesBuffer);

                this.cmd.SetExecutionFlags(UnityEngine.Rendering.CommandBufferExecutionFlags.None); // run compute shaders
                this.cmd.DispatchCompute(this.instancemeshShader, this.CopyToBindPoseBufferKernel, old_count / kThreadGroupX, 1, 1); // dispatch
                Graphics.ExecuteCommandBuffer(this.cmd);
                this.cmd.Clear();
                this.cmd.SetExecutionFlags(UnityEngine.Rendering.CommandBufferExecutionFlags.AsyncCompute);

                this.groupBindPosesResizeBuffer.Release(); // release temp buffer
                this.groupBindPosesResizeBuffer = null;

#if UNITY_EDITOR && VERBOSE
                Debug.Log("Forced to resize bind pose Compute Buffer, new_size: " + new_count + " bone matrices allocated.");
#endif
            }

            // resize skeleton buffer if needed
            if (this._skeletons.NumSkeletonInstances * this.NumSkeletonBones > this.boneMatricesBuffer.count)
            {
                var old_count = this.boneMatricesBuffer.count;
                if (old_count % kThreadGroupX != 0) throw new System.Exception("During skeleton buffer resize, old buffer did not have correct size");

                this.boneMatricesResizeBuffer = new ComputeBuffer(old_count, GPUInstanceBoneMatrix4x4.kByteStride);
                this.boneMatricesResizeBuffer.SetData(new GPUInstanceBoneMatrix4x4[old_count]);
                instancemeshShader.SetBuffer(CopyFromSkeletonBufferKernel, "boneMatricesResizeBuffer", this.boneMatricesResizeBuffer);
                instancemeshShader.SetBuffer(CopyToSkeletonBufferKernel, "boneMatricesResizeBuffer", this.boneMatricesResizeBuffer);

                this.cmd.SetExecutionFlags(UnityEngine.Rendering.CommandBufferExecutionFlags.None); // run compute shaders
                this.cmd.DispatchCompute(this.instancemeshShader, this.CopyFromSkeletonBufferKernel, old_count / kThreadGroupX, 1, 1); // dispatch
                Graphics.ExecuteCommandBuffer(this.cmd);
                this.cmd.Clear();

                var new_count = next_buffer_size(this._skeletons.NumSkeletonInstances) * this.NumSkeletonBones; // get new skeleton buffer len
                if (new_count % kThreadGroupX != 0) throw new System.Exception("During skeleton buffer resize, new buffer did not have correct size");

                this.boneMatricesBuffer.Release();
                this.boneMatricesBuffer = new ComputeBuffer(new_count, GPUInstanceBoneMatrix4x4.kByteStride);
                this.boneMatricesBuffer.SetData(new GPUInstanceBoneMatrix4x4[new_count]);
                this.instancemeshShader.SetBuffer(this.CopyToSkeletonBufferKernel, "boneMatricesBuffer", this.boneMatricesBuffer);
                this.instancemeshShader.SetBuffer(this.CopyFromSkeletonBufferKernel, "boneMatricesBuffer", this.boneMatricesBuffer);
                this.instancemeshShader.SetBuffer(this.CalculateBoneMatricesKernel, "boneMatricesBuffer", this.boneMatricesBuffer);

                this.cmd.SetExecutionFlags(UnityEngine.Rendering.CommandBufferExecutionFlags.None); // run compute shaders
                this.cmd.DispatchCompute(this.instancemeshShader, this.CopyToSkeletonBufferKernel, old_count / kThreadGroupX, 1, 1); // dispatch
                Graphics.ExecuteCommandBuffer(this.cmd);
                this.cmd.Clear();
                this.cmd.SetExecutionFlags(UnityEngine.Rendering.CommandBufferExecutionFlags.AsyncCompute);

                this.boneMatricesResizeBuffer.Release();
                this.boneMatricesResizeBuffer = null;

                foreach (var mesh_type in meshtypes)
                {
                    var type = mesh_type.Value;
                    var instanced_material = type.mData.InstancedMaterial;

                    if (type.IsSkinnedMesh())
                    {
                        // set bone matrices buffer
                        instanced_material.SetBuffer("boneMatricesBuffer", this.boneMatricesBuffer);
                    }
                }
#if UNITY_EDITOR && VERBOSE
                Debug.LogWarning("Forced to resize Skeleton Compute Buffer, new_size: " + new_count + " bone matrices allocated. ");
#endif
            }

            // Resize instance count
            if (this._delta_buffer.MaxInstanceID > this.transformBuffer.count)
            {
                int new_count = -1;
                // first resize instance count
                {
                    if (this.object2WorldBuffer.count != this.transformBuffer.count*2)
                        throw new System.Exception("Error, o2w buffer should have double the count of transform buffer!");

                    var old_count = this.transformBuffer.count;
                    if (old_count % kThreadGroupX != 0) throw new System.Exception("During instance buffer resize, old buffer did not have correct size");

                    if (this.transformBuffer.count != old_count || this.object2WorldBuffer.count != old_count*2 || this.instanceIDBuffer.count != old_count ||
                        this.indirectInstanceIDBuffer.count != old_count)
                        throw new System.Exception("Error, all instance buffers should have same count");

                    this.transformResizeBuffer = new ComputeBuffer(old_count, instance_data.kByteStride);
                    this.transformResizeBuffer.SetData(new instance_data[old_count]); // gpu init
                    this.instancemeshShader.SetBuffer(this.CopyFromInstanceBuffersKernel, "transformResizeBuffer", this.transformResizeBuffer); // set kernel
                    this.instancemeshShader.SetBuffer(this.CopyToInstanceBuffersKernel, "transformResizeBuffer", this.transformResizeBuffer); // set kernel
                    this.object2WorldResizeBuffer = new ComputeBuffer(old_count*2, 4 * 4 * sizeof(float));
                    this.object2WorldResizeBuffer.SetData(new float[old_count * 2 * 4 * 4]); // gpu init
                    this.instancemeshShader.SetBuffer(this.CopyFromInstanceBuffersKernel, "object2WorldResizeBuffer", this.object2WorldResizeBuffer); // set kernel
                    this.instancemeshShader.SetBuffer(this.CopyToInstanceBuffersKernel, "object2WorldResizeBuffer", this.object2WorldResizeBuffer); // set kernel
                    this.instanceIDResizeBuffer = new ComputeBuffer(old_count, sizeof(int));
                    this.instanceIDResizeBuffer.SetData(new int[old_count]); // gpu init
                    this.instancemeshShader.SetBuffer(this.CopyFromInstanceBuffersKernel, "instanceIDResizeBuffer", this.instanceIDResizeBuffer); // set kernel
                    this.instancemeshShader.SetBuffer(this.CopyToInstanceBuffersKernel, "instanceIDResizeBuffer", this.instanceIDResizeBuffer); // set kernel
                    this.indirectInstanceIDResizeBuffer = new ComputeBuffer(old_count, sizeof(int));
                    this.instancemeshShader.SetBuffer(this.CopyFromInstanceBuffersKernel, "indirectInstanceIDResizeBuffer", this.indirectInstanceIDResizeBuffer);
                    this.instancemeshShader.SetBuffer(this.CopyToInstanceBuffersKernel, "indirectInstanceIDResizeBuffer", this.indirectInstanceIDResizeBuffer);

                    this.cmd.SetExecutionFlags(UnityEngine.Rendering.CommandBufferExecutionFlags.None); // run compute shaders
                    this.cmd.DispatchCompute(this.instancemeshShader, this.CopyFromInstanceBuffersKernel, old_count / kThreadGroupX, 1, 1); // dispatch
                    Graphics.ExecuteCommandBuffer(this.cmd);
                    this.cmd.Clear();

                    new_count = next_buffer_size(this._delta_buffer.MaxInstanceID); // get new skeleton buffer len
                    if (new_count % kThreadGroupX != 0) throw new System.Exception("During instance buffer resize, new buffer did not have correct size");

                    this.transformBuffer.Release(); // make new transform buffer
                    this.transformBuffer = new ComputeBuffer(new_count, instance_data.kByteStride);
                    this.transformBuffer.SetData(new instance_data[new_count]);  // gpu init
                    this.instancemeshShader.SetBuffer(this.updateKernalID, "transformBuffer", this.transformBuffer);
                    this.instancemeshShader.SetBuffer(this.SelectRenderedInstancesKernel, "transformBuffer", this.transformBuffer);
                    this.instancemeshShader.SetBuffer(this.calcObjectToWorldKernel, "transformBuffer", this.transformBuffer);
                    this.instancemeshShader.SetBuffer(this.motionKernalID, "transformBuffer", this.transformBuffer);
                    this.instancemeshShader.SetBuffer(this.CopyToInstanceBuffersKernel, "transformBuffer", this.transformBuffer);
                    this.instancemeshShader.SetBuffer(this.CopyFromInstanceBuffersKernel, "transformBuffer", this.transformBuffer);
                    this.instancemeshShader.SetBuffer(this.CalculateBoneMatricesKernel, "transformBuffer", this.transformBuffer);
                    this.instancemeshShader.SetBuffer(this.PrepareRenderedInstancesKernel, "transformBuffer", this.transformBuffer);
                    this.instancemeshShader.SetBuffer(this.PropertySimulationKernel, "transformBuffer", this.transformBuffer);

                    this.object2WorldBuffer.Release(); // make new obj 2 world buffer
                    this.object2WorldBuffer = new ComputeBuffer(new_count * 2, sizeof(float) * 4 * 4);
                    this.object2WorldBuffer.SetData(new float[new_count * 2 * 4 * 4]);  // gpu init
                    this.instancemeshShader.SetBuffer(this.calcObjectToWorldKernel, "object2WorldBuffer", this.object2WorldBuffer);
                    this.instancemeshShader.SetBuffer(this.SelectRenderedInstancesKernel, "object2WorldBuffer", this.object2WorldBuffer);
                    this.instancemeshShader.SetBuffer(this.CopyToInstanceBuffersKernel, "object2WorldBuffer", this.object2WorldBuffer);
                    this.instancemeshShader.SetBuffer(this.CopyFromInstanceBuffersKernel, "object2WorldBuffer", this.object2WorldBuffer);
                    this.instancemeshShader.SetBuffer(this.CalculateBoneMatricesKernel, "object2WorldBuffer", this.object2WorldBuffer);
                    this.instancemeshShader.SetBuffer(this.PrepareRenderedInstancesKernel, "object2WorldBuffer", this.object2WorldBuffer);

                    this.instanceIDBuffer.Release(); // make new instance id buffer
                    this.instanceIDBuffer = new ComputeBuffer(new_count, sizeof(int));
                    this.instanceIDBuffer.SetData(new int[new_count]);  // gpu init
                    this.instancemeshShader.SetBuffer(this.PrepareRenderedInstancesKernel, "instanceIDBuffer", this.instanceIDBuffer);
                    this.instancemeshShader.SetBuffer(this.CopyToInstanceBuffersKernel, "instanceIDBuffer", this.instanceIDBuffer);
                    this.instancemeshShader.SetBuffer(this.CopyFromInstanceBuffersKernel, "instanceIDBuffer", this.instanceIDBuffer);

                    this.indirectInstanceIDBuffer.Release();
                    this.indirectInstanceIDBuffer = new ComputeBuffer(new_count, sizeof(int));
                    this.indirectInstanceIDBuffer.SetData(new int[new_count]);
                    this.instancemeshShader.SetBuffer(this.SelectRenderedInstancesKernel, "indirectInstanceIDBuffer", this.indirectInstanceIDBuffer);
                    this.instancemeshShader.SetBuffer(this.motionKernalID, "indirectInstanceIDBuffer", this.indirectInstanceIDBuffer);
                    this.instancemeshShader.SetBuffer(this.IndirectIDUpdateKernel, "indirectInstanceIDBuffer", this.indirectInstanceIDBuffer);
                    this.instancemeshShader.SetBuffer(this.CopyFromInstanceBuffersKernel, "indirectInstanceIDBuffer", this.indirectInstanceIDBuffer);
                    this.instancemeshShader.SetBuffer(this.CopyToInstanceBuffersKernel, "indirectInstanceIDBuffer", this.indirectInstanceIDBuffer);
                    this.instancemeshShader.SetBuffer(this.PrepareRenderedInstancesKernel, "indirectInstanceIDBuffer", this.indirectInstanceIDBuffer);
                    this.instancemeshShader.SetBuffer(this.CalculateBoneMatricesKernel, "indirectInstanceIDBuffer", this.indirectInstanceIDBuffer);

                    this.cmd.SetExecutionFlags(UnityEngine.Rendering.CommandBufferExecutionFlags.None); // run compute shaders
                    this.cmd.DispatchCompute(this.instancemeshShader, this.CopyToInstanceBuffersKernel, old_count / kThreadGroupX, 1, 1); // dispatch
                    Graphics.ExecuteCommandBuffer(this.cmd);
                    this.cmd.Clear();
                    this.cmd.SetExecutionFlags(UnityEngine.Rendering.CommandBufferExecutionFlags.AsyncCompute);

                    this.transformResizeBuffer.Release(); // release temp buffers
                    this.transformResizeBuffer = null;
                    this.object2WorldResizeBuffer.Release();
                    this.object2WorldResizeBuffer = null;
                    this.instanceIDResizeBuffer.Release();
                    this.instanceIDResizeBuffer = null;
                    this.indirectInstanceIDResizeBuffer.Release();
                    this.indirectInstanceIDResizeBuffer = null;

                    foreach (var mesh_type in meshtypes) // reset material buffers!
                    {
                        var type = mesh_type.Value;
                        var instanced_material = type.mData.InstancedMaterial;
                        instanced_material.SetBuffer("instanceIDBuffer", instanceIDBuffer);
                        //set instance data buffer
                        instanced_material.SetBuffer("transformBuffer", transformBuffer);
                        //Set Object2World buffer
                        instanced_material.SetBuffer("object2WorldBuffer", object2WorldBuffer);
                    }
#if UNITY_EDITOR && VERBOSE
                    Debug.LogWarning("Forced to resize Instance Compute Buffers, new_size: " + new_count + " instances allocated. ");
#endif
                }
            }

            // Resize hierarchy
            var old_instance_per_depth = this.hierarchyDepthBuffer.count / this.hierarchy.MaxDepth;
            if (this.hierarchy.MaxInstancesPerDepth != old_instance_per_depth)
            {
                var old_count = this.hierarchyDepthBuffer.count;
                if (old_count % kThreadGroupX != 0 || old_instance_per_depth % kThreadGroupX != 0) throw new System.Exception("During skeleton buffer resize, old buffer did not have correct size");

                this.hierarchyDepthResizeBuffer = new ComputeBuffer(old_count, sizeof(int)); // make new buffer
                this.hierarchyDepthResizeBuffer.SetData(new int[old_count]);
                instancemeshShader.SetBuffer(CopyFromHierarchyBufferKernel, "hierarchyDepthResizeBuffer", this.hierarchyDepthResizeBuffer); // set kernel
                instancemeshShader.SetBuffer(CopyToHierarchyBufferKernel, "hierarchyDepthResizeBuffer", this.hierarchyDepthResizeBuffer); // set kernel

                this.cmd.SetExecutionFlags(UnityEngine.Rendering.CommandBufferExecutionFlags.None); // run compute shaders
                this.cmd.SetComputeIntParam(instancemeshShader, "hierarchyOldResizeInstanceCountPerDepth", old_instance_per_depth);
                this.cmd.DispatchCompute(this.instancemeshShader, this.CopyFromHierarchyBufferKernel, old_instance_per_depth / kThreadGroupX, 1, 1); // dispatch
                Graphics.ExecuteCommandBuffer(this.cmd);
                this.cmd.Clear();

                var instance_per_depth = this.hierarchy.MaxInstancesPerDepth;
                var new_count = this.hierarchy.MaxDepth * this.hierarchy.MaxInstancesPerDepth; // multiply by depth of hierarchy
                if (instance_per_depth % kThreadGroupX != 0) throw new System.Exception("Error, expected new hierarchy buffer to have instance count of thread group count");
                if (old_count % this.hierarchy.MaxDepth != 0 || instance_per_depth % kThreadGroupX != 0) throw new System.Exception("Invalid hierarchy in hierarchy gpu resize function");

                this.hierarchyDepthBuffer.Release(); // create new hierarchy depth buffer
                this.hierarchyDepthBuffer = new ComputeBuffer(new_count, sizeof(int));
                this.hierarchyDepthBuffer.SetData(new int[new_count]);
                instancemeshShader.SetBuffer(this.updateHierarchyKernel, "hierarchyDepthBuffer", this.hierarchyDepthBuffer);
                instancemeshShader.SetBuffer(this.calcObjectToWorldKernel, "hierarchyDepthBuffer", this.hierarchyDepthBuffer);
                instancemeshShader.SetBuffer(this.CopyToHierarchyBufferKernel, "hierarchyDepthBuffer", this.hierarchyDepthBuffer);
                instancemeshShader.SetBuffer(this.CopyFromHierarchyBufferKernel, "hierarchyDepthBuffer", this.hierarchyDepthBuffer);

                this.cmd.SetExecutionFlags(UnityEngine.Rendering.CommandBufferExecutionFlags.None); // run compute shaders
                this.cmd.SetComputeIntParam(instancemeshShader, "hierarchyOldResizeInstanceCountPerDepth", old_instance_per_depth);
                this.cmd.SetComputeIntParam(instancemeshShader, "hierarchyNewResizeInstanceCountPerDepth", instance_per_depth);
                this.cmd.DispatchCompute(this.instancemeshShader, this.CopyToHierarchyBufferKernel, old_instance_per_depth / kThreadGroupX, 1, 1); // dispatch
                Graphics.ExecuteCommandBuffer(this.cmd);
                this.cmd.Clear();
                this.cmd.SetExecutionFlags(UnityEngine.Rendering.CommandBufferExecutionFlags.AsyncCompute);

                this.hierarchyDepthResizeBuffer.Release(); // release temp buffer
                this.hierarchyDepthResizeBuffer = null;
#if UNITY_EDITOR && VERBOSE
                Debug.LogWarning("Forced to resize Hierarchy Depth Compute Buffer, new_size: " + new_count + " integer array allocated. ");
#endif
            }

            // Resize properties buffer
            if (this._property_delta_buffer.MaxInstanceID > this.propertyBuffer.count)
            {
                var old_count = this.propertyBuffer.count;
                if (old_count % kThreadGroupX != 0) throw new System.Exception("During instance buffer resize, old buffer did not have correct size");
                if (old_count != this.indirectPropertyIDBuffer.count) throw new System.Exception("Error, property resize indirect buffer had incorrect old_count");

                this.propertyResizeBuffer = new ComputeBuffer(old_count, instance_properties.kByteStride);
                this.propertyResizeBuffer.SetData(new instance_properties[old_count]); // gpu init
                this.instancemeshShader.SetBuffer(this.CopyToPropertyBuffersKernel, "propertyResizeBuffer", this.propertyResizeBuffer);
                this.instancemeshShader.SetBuffer(this.CopyFromPropertyBuffersKernel, "propertyResizeBuffer", this.propertyResizeBuffer);

                this.indirectPropertyIDResizeBuffer = new ComputeBuffer(old_count, sizeof(int));
                this.instancemeshShader.SetBuffer(this.CopyFromPropertyBuffersKernel, "indirectPropertyIDResizeBuffer", this.indirectPropertyIDResizeBuffer);
                this.instancemeshShader.SetBuffer(this.CopyToPropertyBuffersKernel, "indirectPropertyIDResizeBuffer", this.indirectPropertyIDResizeBuffer);

                this.cmd.SetExecutionFlags(UnityEngine.Rendering.CommandBufferExecutionFlags.None); // run compute shaders
                this.cmd.DispatchCompute(this.instancemeshShader, this.CopyFromPropertyBuffersKernel, old_count / kThreadGroupX, 1, 1); // dispatch
                Graphics.ExecuteCommandBuffer(this.cmd);
                this.cmd.Clear();

                int new_count = next_buffer_size(this._property_delta_buffer.MaxInstanceID); // get new skeleton buffer len
                if (new_count % kThreadGroupX != 0) throw new System.Exception("During property buffer resize, new buffer did not have correct size");

                // properties buffer
                this.propertyBuffer.Release();
                this.propertyBuffer = new ComputeBuffer(new_count, instance_properties.kByteStride);
                this.propertyBuffer.SetData(new instance_properties[new_count]);
                this.instancemeshShader.SetBuffer(this.UpdatePropertyBuffersKernel, "propertyBuffer", this.propertyBuffer);
                this.instancemeshShader.SetBuffer(this.CopyToPropertyBuffersKernel, "propertyBuffer", this.propertyBuffer);
                this.instancemeshShader.SetBuffer(this.CopyFromPropertyBuffersKernel, "propertyBuffer", this.propertyBuffer);
                this.instancemeshShader.SetBuffer(this.PropertySimulationKernel, "propertyBuffer", this.propertyBuffer);
                this.instancemeshShader.SetBuffer(this.motionKernalID, "propertyBuffer", this.propertyBuffer);

                // indirect property id buffer
                this.indirectPropertyIDBuffer.Release();
                this.indirectPropertyIDBuffer = new ComputeBuffer(new_count, sizeof(int));
                this.indirectPropertyIDBuffer.SetData(new int[new_count]);
                this.instancemeshShader.SetBuffer(this.IndirectPropertyIDUpdateKernel, "indirectPropertyIDBuffer", this.indirectPropertyIDBuffer);
                this.instancemeshShader.SetBuffer(this.CopyToPropertyBuffersKernel, "indirectPropertyIDBuffer", this.indirectPropertyIDBuffer);
                this.instancemeshShader.SetBuffer(this.CopyFromPropertyBuffersKernel, "indirectPropertyIDBuffer", this.indirectPropertyIDBuffer);
                this.instancemeshShader.SetBuffer(this.PropertySimulationKernel, "indirectPropertyIDBuffer", this.indirectPropertyIDBuffer);

                this.cmd.SetExecutionFlags(UnityEngine.Rendering.CommandBufferExecutionFlags.None); // run compute shaders
                this.cmd.DispatchCompute(this.instancemeshShader, this.CopyToPropertyBuffersKernel, old_count / kThreadGroupX, 1, 1); // dispatch
                Graphics.ExecuteCommandBuffer(this.cmd);
                this.cmd.Clear();
                this.cmd.SetExecutionFlags(UnityEngine.Rendering.CommandBufferExecutionFlags.AsyncCompute);

                this.propertyResizeBuffer.Release(); // release temp buffers
                this.propertyResizeBuffer = null;
                this.indirectPropertyIDResizeBuffer.Release();
                this.indirectPropertyIDResizeBuffer = null;

                foreach (var mesh_type in meshtypes) // reset material buffers!
                {
                    var type = mesh_type.Value;
                    var instanced_material = type.mData.InstancedMaterial;
                    instanced_material.SetBuffer("propertyBuffer", this.propertyBuffer);
                }
#if UNITY_EDITOR && VERBOSE
                Debug.LogWarning("Forced to resize Properties Compute Buffers, new_size: " + new_count + " property structs allocated. ");
#endif
            }

            // Resize paths buffer
            if (this._paths.NumPathAllocated * this._paths.PathFloatStride > this.pathsBuffer.count)
            {
                var old_count = this.pathsBuffer.count;
                if (old_count % kThreadGroupX != 0) throw new System.Exception("During instance buffer resize, old buffer did not have correct size");

                this.pathsResizeBuffer = new ComputeBuffer(old_count, sizeof(float));
                this.pathsResizeBuffer.SetData(new float[old_count]); // gpu init
                this.instancemeshShader.SetBuffer(this.CopyToPathsBufferKernel, "pathsResizeBuffer", this.pathsResizeBuffer);
                this.instancemeshShader.SetBuffer(this.CopyFromPathsBufferKernel, "pathsResizeBuffer", this.pathsResizeBuffer);

                this.cmd.SetExecutionFlags(UnityEngine.Rendering.CommandBufferExecutionFlags.None); // run compute shaders
                this.cmd.DispatchCompute(this.instancemeshShader, this.CopyFromPathsBufferKernel, old_count / kThreadGroupX, 1, 1); // dispatch
                Graphics.ExecuteCommandBuffer(this.cmd);
                this.cmd.Clear();

                int new_count = next_buffer_size(this._paths.NumPathAllocated); // get new skeleton buffer len
                if (new_count % kThreadGroupX != 0) throw new System.Exception("During property buffer resize, new buffer did not have correct size");

                this.pathsBuffer.Release();
                this.pathsBuffer = new ComputeBuffer(new_count * this._paths.PathFloatStride, sizeof(float));
                this.pathsBuffer.SetData(new float[new_count]);
                this.instancemeshShader.SetBuffer(this.CopyToPathsBufferKernel, "pathsBuffer", this.pathsBuffer);
                this.instancemeshShader.SetBuffer(this.CopyFromPathsBufferKernel, "pathsBuffer", this.pathsBuffer);
                this.instancemeshShader.SetBuffer(this.UpdatePathsBufferKernel, "pathsBuffer", this.pathsBuffer);
                this.instancemeshShader.SetBuffer(this.PropertySimulationKernel, "pathsBuffer", this.pathsBuffer);


                this.cmd.SetExecutionFlags(UnityEngine.Rendering.CommandBufferExecutionFlags.None); // run compute shaders
                this.cmd.DispatchCompute(this.instancemeshShader, this.CopyToPathsBufferKernel, old_count / kThreadGroupX, 1, 1); // dispatch
                Graphics.ExecuteCommandBuffer(this.cmd);
                this.cmd.Clear();
                this.cmd.SetExecutionFlags(UnityEngine.Rendering.CommandBufferExecutionFlags.AsyncCompute);

                this.pathsResizeBuffer.Release(); // release temp buffers
                this.pathsResizeBuffer = null;

#if UNITY_EDITOR && VERBOSE
                Debug.LogWarning("Forced to resize paths Compute Buffers, new_size: " + new_count +  " path arrays allocated. ");
#endif
            }
        }

        void computeshader_UpdateInitialize(float deltaTime, ulong deltaTicks)
        {
#if UNITY_EDITOR
            UnityEngine.Profiling.Profiler.BeginSample("UpdateInitialize");
#endif

            if (!Initialized)
                throw new System.Exception("Error, not initialized.");
            if (deltaTicks > 10000 * 3600) // dont really expect anyone to run into this issue but meh. One day ulong will be supported in compute shader.. one day... Warning is here because animations will desynchronize if deltaTicks gets truncated due to precision error
                Debug.LogWarning("Warning, delta tick approached an entire hour with an update. Please invoke instancemesh.Update() every frame!");

            //set camera args
            if (FrustumCamera != null)
            {
                camfrustum(this.FrustumCamera, this.frustumArr); //get new frustum
                this.cmd.SetBufferData(this.cameraFrustumBuffer, this.frustumArr);
                this.cmd.SetComputeVectorParam(this.instancemeshShader, "globalCameraPosition", new Vector4(
                    FrustumCamera.transform.position.x,
                    FrustumCamera.transform.position.y,
                    FrustumCamera.transform.position.z, 0));
                Vector4 fwd = this.FrustumCamera.transform.forward;
                Vector4 up = this.FrustumCamera.transform.up;
                Vector4 right = this.FrustumCamera.transform.right;
                this.cmd.SetComputeVectorParam(this.instancemeshShader, "globalCameraForward", fwd);
                this.cmd.SetComputeVectorParam(this.instancemeshShader, "globalCameraUp", up);
                this.cmd.SetComputeVectorParam(this.instancemeshShader, "globalCameraRight", right);
            }

            // set delta time
            this.cmd.SetComputeIntParam(this.instancemeshShader, "DELTA_TICKS", (int)deltaTicks);
            // set tick seconds
            this.cmd.SetComputeFloatParam(this.instancemeshShader, "TICKS_SECONDS", (float)(this.Ticks * GPUInstance.Ticks.SecondsPerTick));
            // set radius cull ratio
            var lod_quality = (1.0f / QualitySettings.lodBias);
            this.cmd.SetComputeFloatParam(this.instancemeshShader, "RADIUS_CULL_RATIO", cull_radius_distance_LOD_range * lod_quality);
            //set LOD quality (ie lod bias)
            this.cmd.SetComputeFloatParam(this.instancemeshShader, "LOD_QUALITY", lod_quality);
            // set cull type
            this.cmd.SetComputeIntParam(this.instancemeshShader, "CULLING_TYPE", this.FrustumCamera != null ? (int)this.DistanceCullingType : (int)FrustumDistanceCullingType.None);
            this.cmd.SetComputeFloatParam(this.instancemeshShader, "UNIFORM_CAMERA_DISTANCE", this.UniformCameraDistance);

            //apply argument changes if they were changed
            if (this.pending_group_updates.Count > 0)
            {
                // TODO: profiling..Figure out a good number of changes to just set the whole buffer.
                if (this.pending_group_updates.Count > 32)
                {
                    this.cmd.SetBufferData(this.argsBuffer, this.args); // set argsbuffer
                    this.cmd.SetBufferData(this.groupLODBuffer, this.groupLOD); //set grouplodsbuffer
                    this.pending_group_updates.Clear(); // clear 
                    this.pending_lod_group_updates.Clear();
                }
                else
                {
                    // fill temp queue
                    foreach (var gid in this.pending_group_updates)
                        this.pending_group_updates_queue.Enqueue(gid);

                    // Set data for each group id
                    while (this.pending_group_updates_queue.Count > 0)
                    {
                        // set args buffer for this group id
                        var gid = this.pending_group_updates_queue.Dequeue();
                        var offset = ((int)gid) * kIndirectArgCountPerGroup;
                        this.cmd.SetBufferData(this.argsBuffer, this.args, offset, offset, kIndirectArgCountPerGroup);

                        // set group LOD buffer for this group id
                        offset = groupLODIndex(gid);
                        this.cmd.SetBufferData(this.groupLODBuffer, this.groupLOD, offset, offset, groupLODStride());

                        // remove from pending updates buffers
                        if (!this.pending_lod_group_updates.Remove(gid))
                            throw new System.Exception("Internal Error, group ids in pending_group_ids should always exist on pending_lod_group_updates!");
                        if (!this.pending_group_updates.Remove(gid))
                            throw new System.Exception("Internal Error, groupID should exist in pending group update set!");
                    }
                }
            }

            // apply lod changes for any remaining updates (lod changes can be made seperately)
            if (this.pending_lod_group_updates.Count > 0)
            {
                // TODO: profiling..Figure out a good number of changes to just set the whole buffer.
                if (this.pending_lod_group_updates.Count > 32)
                {
                    this.cmd.SetBufferData(this.groupLODBuffer, this.groupLOD); //set grouplodsbuffer
                    this.pending_lod_group_updates.Clear();
                }
                else
                {
                    // fill temp queue
                    foreach (var gid in this.pending_lod_group_updates)
                        this.pending_lod_group_updates_queue.Enqueue(gid);

                    // Set data for each group id
                    while (this.pending_lod_group_updates_queue.Count > 0)
                    {
                        // set args buffer for this group id
                        var gid = this.pending_lod_group_updates_queue.Dequeue();

                        // set group LOD buffer for this group id
                        var offset = groupLODIndex(gid);
                        this.cmd.SetBufferData(this.groupLODBuffer, this.groupLOD, offset, offset, groupLODStride());

                        // remove from pending lod updates buffer
                        if (!this.pending_lod_group_updates.Remove(gid))
                            throw new System.Exception("Internal Error, the temp pending_lod_group_updates_queue should be 1:1 with the pending_lod_group_updates set");
                    }
                }
            }

            // execute cmd buffer
            execute_command_buffer();

#if UNITY_EDITOR
            UnityEngine.Profiling.Profiler.EndSample();
#endif
        }

        void build_instancemesh_update_command_buffer(int delta_count, int hierarchy_delta_count, int indirect_id_delta_count, int pose_delta_count, int property_delta_count, int property_indirect_id_delta_count, int paths_delta_count)
        {
            { // must happen every frame
                delta_count = System.Math.Max(delta_count, instancemesh.maxMeshTypes);
                var ngroups_delta = delta_count / kThreadGroupX;
                var thread_group_count = System.Math.Max(delta_count % kThreadGroupX == 0 ? ngroups_delta : ngroups_delta + 1, 1);
                this.cmd.DispatchCompute(this.instancemeshShader, this.updateKernalID, thread_group_count, 1, 1); // send data updates
            }

            if (property_delta_count > 0)
            { // must happen every frame
                var ngroups_property = property_delta_count / kThreadGroupX;
                var thread_group_count = System.Math.Max(property_delta_count % kThreadGroupX == 0 ? ngroups_property : ngroups_property + 1, 1);
                this.cmd.DispatchCompute(this.instancemeshShader, this.UpdatePropertyBuffersKernel, thread_group_count, 1, 1); // send data updates
            }

            if (indirect_id_delta_count > 0)
            {
                var ngroups_indirect = indirect_id_delta_count / kThreadGroupX;
                var i_thread_group_count = System.Math.Max(indirect_id_delta_count % kThreadGroupX == 0 ? ngroups_indirect : ngroups_indirect + 1, 1); //get the number of stream processors to use
                this.cmd.DispatchCompute(this.instancemeshShader, this.IndirectIDUpdateKernel, i_thread_group_count, 1, 1); // send update
            }

            if (property_indirect_id_delta_count > 0)
            {
                var ngroups_indirect_property = property_indirect_id_delta_count / kThreadGroupX;
                var thread_group_count = System.Math.Max(ngroups_indirect_property % kThreadGroupX == 0 ? ngroups_indirect_property : ngroups_indirect_property + 1, 1);
                this.cmd.DispatchCompute(this.instancemeshShader, this.IndirectPropertyIDUpdateKernel, thread_group_count, 1, 1); // send data updates
            }

            if (hierarchy_delta_count > 0)
            {
                var ngroups_hierarchy = hierarchy_delta_count / kThreadGroupX;
                var h_thread_group_count = System.Math.Max(hierarchy_delta_count % kThreadGroupX == 0 ? ngroups_hierarchy : ngroups_hierarchy + 1, 1); //get the number of stream processors to use
                this.cmd.DispatchCompute(this.instancemeshShader, this.updateHierarchyKernel, h_thread_group_count, 1, 1); // send update
            }

            if (pose_delta_count > 0)
            {
                var ngroups_pose = pose_delta_count / kThreadGroupX;
                var p_thread_group_count = System.Math.Max(pose_delta_count % kThreadGroupX == 0 ? ngroups_pose : ngroups_pose + 1, 1);
                this.cmd.DispatchCompute(this.instancemeshShader, this.UpdateGroupBindPosesBufferKernel, p_thread_group_count, 1, 1);
            }

            if (paths_delta_count > 0)
            {
                var ngroups_paths = paths_delta_count / kThreadGroupX;
                var thread_group_count = System.Math.Max(paths_delta_count % kThreadGroupX == 0 ? ngroups_paths : ngroups_paths + 1, 1);
                this.cmd.DispatchCompute(this.instancemeshShader, this.UpdatePathsBufferKernel, thread_group_count, 1, 1);
            }
        }

        /// <summary>
        /// Run update function without drawing (useful if you need to push a lot of data to the GPU in a single frame)
        /// </summary>
        /// <param name="deltaTime"></param>
        /// <param name="specify_delta_length"></param>
        bool computeshader_UpdateDataTask(in bool force_dispatch)
        {
#if UNITY_EDITOR
            UnityEngine.Profiling.Profiler.BeginSample("UpdateData_InstanceMesh");
#endif
            //prepare update
            this.cmd.SetComputeIntParam(this.instancemeshShader, "DELTA_COUNT", this._delta_buffer.CurrentDeltaCount); // instancemeshShader.SetInt("DELTA_COUNT", this._delta_buffer.CurrentDeltaCount);
            this.cmd.SetComputeIntParam(this.instancemeshShader, "HIERARCHY_BUFFER_LEN", this.hierarchy.MaxInstancesPerDepth); //instancemeshShader.SetInt("HIERARCHY_BUFFER_LEN", this.hierarchy.MaxInstancesPerDepth);
            this.cmd.SetComputeIntParam(this.instancemeshShader, "INDIRECT_ID_DELTA_COUNT", this._delta_buffer.CurrentIndirectIDDeltaCount);  //instancemeshShader.SetInt("INDIRECT_ID_DELTA_COUNT", this._delta_buffer.CurrentIndirectIDDeltaCount);
            this.cmd.SetComputeIntParam(this.instancemeshShader, "BIND_POSE_DELTA_COUNT", this._bind_pose_delta_buffer.CurrentDeltaCount);  //instancemeshShader.SetInt("BIND_POSE_DELTA_COUNT", this._bind_pose_delta_buffer.CurrentDeltaCount);
            this.cmd.SetComputeIntParam(this.instancemeshShader, "PROPERTY_DELTA_COUNT", this._property_delta_buffer.CurrentDeltaCount);
            this.cmd.SetComputeIntParam(this.instancemeshShader, "INDIRECT_PROPERTY_ID_DELTA_COUNT", this._property_delta_buffer.CurrentIndirectIDDeltaCount);
            this.cmd.SetComputeIntParam(this.instancemeshShader, "PATH_DELTA_COUNT", this._paths.CurrentDeltaCount);

            int delta_count = this._delta_buffer.UpdateComputeBuffer(this.deltaBuffer, this.cmd); // update instances
            int property_delta_count = this._property_delta_buffer.UpdateComputeBuffer(this.propertyDeltaBuffer, this.cmd); // update properties
            int indirect_id_delta_count = this._delta_buffer.UpdateIndirectIDComputeBuffer(this.indirectInstanceIDDeltaBuffer, this.cmd);   // update indirect ids
            int indirect_property_id_delta_count = this._property_delta_buffer.UpdateIndirectIDComputeBuffer(this.indirectPropertyIDDeltaBuffer, this.cmd);   // update indirect property ids
            int hierarchy_update_count = this.hierarchy.UpdateBuffer(this.hierarchyDeltaBuffer, this.cmd); // update heirarchy depth map
            int pose_delta_count = this._bind_pose_delta_buffer.UpdateComputeBuffer(this.groupBindPosesDeltaBuffer, this.cmd); // update bind poses buffer
            int paths_delta_count = this._paths.UpdateComputeBuffers(this.pathsDeltaBuffer, this.pathsDeltaIDBuffer, this.cmd);

            if (delta_count <= 0 && hierarchy_update_count <= 0 && pose_delta_count <= 0 && paths_delta_count <= 0 &&
                indirect_id_delta_count <= 0 && property_delta_count <= 0 && indirect_property_id_delta_count <= 0 && !force_dispatch)
            {
#if UNITY_EDITOR
                UnityEngine.Profiling.Profiler.EndSample();
#endif
                return false; // All pending updates finished
            }

#if UNITY_EDITOR
            UnityEngine.Profiling.Profiler.EndSample();
            UnityEngine.Profiling.Profiler.BeginSample("Compute_Updates_InstanceMesh");
#endif

            //enqueue update task
            build_instancemesh_update_command_buffer(delta_count, hierarchy_update_count, indirect_id_delta_count, pose_delta_count, property_delta_count, indirect_property_id_delta_count, paths_delta_count);
            //invoke
            execute_command_buffer();

#if UNITY_EDITOR
            UnityEngine.Profiling.Profiler.EndSample();
#endif
            return true; // Still pending updates, return true
        }

        void build_instancemesh_compute_command_buffer()
        {
            var instance_count = this._delta_buffer.IndirectBufferInstanceCount;
            instance_count = System.Math.Max(kThreadGroupX, instance_count + (kThreadGroupX - (instance_count % kThreadGroupX)));
            if (instance_count % kThreadGroupX != 0) throw new System.Exception("Error, process thread group count should be multiple of thread group count");

            var property_count = this._property_delta_buffer.IndirectBufferInstanceCount;
            property_count = System.Math.Max(kThreadGroupX, property_count + (kThreadGroupX - (property_count % kThreadGroupX)));
            if (property_count % kThreadGroupX != 0) throw new System.Exception("Error, process thread group count should be multiple of thread group count");

            // Set compute params
            this.cmd.SetComputeIntParam(this.instancemeshShader, "instanceCount", this._delta_buffer.IndirectBufferInstanceCount); // set instance count
            this.cmd.SetComputeIntParam(this.instancemeshShader, "PROPERTY_COUNT", this._property_delta_buffer.IndirectBufferInstanceCount); // set property instance count

            // Property Simulation
            this.cmd.BeginSample("PropertySimulation");
            this.cmd.DispatchCompute(this.instancemeshShader, this.PropertySimulationKernel, property_count / kThreadGroupX, 1, 1);
            this.cmd.EndSample("PropertySimulation");

            // Motion update - only used by bones
            if (this._skeletons.NumSkeletonInstances > 0) // if no bones.. just skip
            {
                this.cmd.BeginSample("MotionKernel");
                this.cmd.DispatchCompute(this.instancemeshShader, this.motionKernalID, instance_count / kThreadGroupX, 1, 1);
                this.cmd.EndSample("MotionKernel");
            }

            // Run Object2World matrix calculations
            this.cmd.BeginSample("Obj2World");
            for (int i = 0; i < this.hierarchy.MaxDepth; i++)
            {
                var num_instances = this.hierarchy.NumInstancesAtDepth(i);

                if (num_instances == 0) // If 0 instances, then calculation is done.
                    break;

                this.cmd.SetComputeIntParam(instancemeshShader, "object2WorldDepth", i);
                var ngroups = num_instances / kThreadGroupX;
                int thread_group_count = System.Math.Max(1, num_instances % kThreadGroupX == 0 ? ngroups : ngroups + 1);
                this.cmd.DispatchCompute(instancemeshShader, calcObjectToWorldKernel, thread_group_count, 1, 1);
            }
            this.cmd.EndSample("Obj2World");

            // Dispatch Select Render Instances & cumsum
            this.cmd.BeginSample("SelectRenderInstances");
            this.cmd.DispatchCompute(this.instancemeshShader, this.SelectRenderedInstancesKernel, instance_count / kThreadGroupX, 1, 1);
            this.cmd.EndSample("SelectRenderInstances");

            // Perform csum
            this.cmd.BeginSample("csum");
            const int csum2_3_4_thread_group_count = 1;
            const int csum1_5_thread_group_count = 4;
            this.cmd.DispatchCompute(this.instancemeshShader, this.csum1KernalID, csum1_5_thread_group_count, 1, 1);
            this.cmd.DispatchCompute(this.instancemeshShader, this.csum2KernalID, csum2_3_4_thread_group_count, 1, 1);
            this.cmd.DispatchCompute(this.instancemeshShader, this.csum3KernalID, csum2_3_4_thread_group_count, 1, 1);
            this.cmd.DispatchCompute(this.instancemeshShader, this.csum4KernalID, csum2_3_4_thread_group_count, 1, 1);
            this.cmd.DispatchCompute(this.instancemeshShader, this.csum5KernalID, csum1_5_thread_group_count, 1, 1);
            this.cmd.EndSample("csum");

            // Do Prepare render instances
            this.cmd.BeginSample("PrepareRenderInstances");
            this.cmd.DispatchCompute(this.instancemeshShader, this.PrepareRenderedInstancesKernel, instance_count / kThreadGroupX, 1, 1);
            this.cmd.EndSample("PrepareRenderInstances");

            // Do Calc Bone Matrices
            if (this._skeletons.NumSkeletonInstances > 0) // if no bones.. just skip
            {
                this.cmd.BeginSample("CalcBoneMats");
                this.cmd.DispatchCompute(this.instancemeshShader, this.CalculateBoneMatricesKernel, instance_count / kThreadGroupX, 1, 1);
                this.cmd.EndSample("CalcBoneMats");
            }
        }

        void execute_command_buffer()
        {
            Graphics.ExecuteCommandBufferAsync(this.cmd, UnityEngine.Rendering.ComputeQueueType.Urgent);
            this.cmd.Clear();
            this.cmd.SetExecutionFlags(UnityEngine.Rendering.CommandBufferExecutionFlags.AsyncCompute);
        }

        void DrawIndirect()
        {
            bool ShouldRender(ushort groupID)
            { // determine if a mesh type should be drawn based on the number of instances allocated to it.
                var lod_index = groupLODIndex(groupID);
                for (int lod = 0; lod < instancemesh.NumLODLevels; lod++) // check each LOD.. see if any instances exist
                {
                    var lod_gid = (ushort)this.groupLOD[lod_index + lod];
                    if (lod_gid != instancemesh.NULL_ID && this._delta_buffer.NumInstancesForGroup(lod_gid) > 0)
                        return true;
                }
                return false;
            }

            //gpu instance draw calls
            foreach (var pair in meshtypes)
            {
                var type = pair.Value;
                var dat = pair.Value.mData;
                if (ShouldRender(type.groupID)) // only drawIndirect if there is a non-zero amount of instances- unity will always make a draw call (even when indirect args indicate no instances)
                {
                    UnityEngine.Graphics.DrawMeshInstancedIndirect(type.shared_mesh, 0, type.mData.InstancedMaterial, type.bounds,
                        argsBuffer, dat.argsByteOffset, null, type.castShadows, type.receiveShadows);
                }
            }
        }

        /// <summary>
        /// Run drawing task (Should be called at most once per frame and must be after computeshader_UpdateTask invocation(s) if any)
        /// </summary>
        /// <param name="deltaTime"></param>
        /// <param name="specify_delta_length"></param>
        void computeshader_RunDrawComputeTask()
        {
#if UNITY_EDITOR
            UnityEngine.Profiling.Profiler.BeginSample("Compute_Process_InstanceMesh");
#endif
            // compute shaders
            build_instancemesh_compute_command_buffer();

            // exec command buffer
            execute_command_buffer();

#if UNITY_EDITOR
            UnityEngine.Profiling.Profiler.EndSample();
            UnityEngine.Profiling.Profiler.BeginSample("DrawIndirect_InstanceMesh");
#endif

            //draw indirect
            DrawIndirect();

#if UNITY_EDITOR
            UnityEngine.Profiling.Profiler.EndSample();
#endif
        }

        /// <summary>
        /// [Main Thread] Update & Run Compute Tasks & Draw indirect instances
        /// </summary>
        /// <param name="dt"></param>
        public void Update(float dt)
        {
            AssertInitializedAndMainThread();

#if UNITY_EDITOR
            UnityEngine.Profiling.Profiler.BeginSample("InstanceMeshUpdate");
#endif
            // Update time
            if (!this._ticks_stopwatch.IsRunning) this._ticks_stopwatch.Start();
            this.DeltaTicks = GPUInstance.Ticks.CalcDeltaTicks(this.Ticks, this._ticks_stopwatch);
            this.Ticks += this.DeltaTicks;

            // Update compute buffer sizes if needed
            try_resize_compute_buffers();

            // Update buffers that only need be update once per frame
            computeshader_UpdateInitialize(dt, this.DeltaTicks);

            int update_count = 0;
            while (computeshader_UpdateDataTask(update_count == 0)) // arbitrary numbers of instances can be updated every frame. There are 'delta' buffers that are used to queue up and push instance data to the GPU
            {
                update_count++;
            }; // Update until all delta buffer empty

            computeshader_RunDrawComputeTask(); // run compute & draw task

#if UNITY_EDITOR
            UnityEngine.Profiling.Profiler.EndSample();
#endif
        }

#if UNITY_EDITOR
        T[] GetDataFromGPU<T>(ComputeBuffer buffer, int start_index=0, int count=int.MaxValue)
        {
            if (count == int.MaxValue)
                count = buffer.count;
            T[] arr = new T[count];
            buffer.GetData(arr, 0, start_index, count);
            return arr;
        }
#endif

        /// <summary>
        /// This function will create a new mesh instance (from the input instance data) on the GPU
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data"></param>
        /// <param name="delta_index"></param>
        public void Set<T>(in T data) where T : IInstanceMeshData
        {
            var delta = new instance_delta(data.position, data.rotation, data.scale, data.id, data.groupID, data.data1, data.parentID, data.propertyID, data.skeletonID, data.DirtyFlags, data.data2);

            _delta_buffer.UpdateInstance(delta: delta, instance_deleted: false);
            hierarchy.SetParent(delta);

            if (data.HasProperties)
            {
                _property_delta_buffer.UpdateInstance(new instance_properties_delta(data.props_offset, data.props_tiling, data.id, data.props_color,
                    data.props_instanceTicks, data.props_animationID, data.props_pathID, data.props_extra, data.propertyID, data.DirtyFlags, data.props_pathInstanceTicks, data.props_pad2),
                    instance_deleted: false);
            }
        }

        /// <summary>
        /// Delete the mesh instance with the specified id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data"></param>
        /// <param name="delta_index"></param>
        public void Delete<T>(in T data) where T : IInstanceMeshData
        {
            var delta = default(instance_delta);
            delta.id = data.id;
            delta.DirtyFlags = DirtyFlag.All;

            _delta_buffer.UpdateInstance(delta: delta, instance_deleted: true, deleted_instance_group_id: data.groupID);
            hierarchy.Delete(data.id);

            if (data.HasProperties)
            {
                var props = default(instance_properties_delta);
                props.propertyID = data.propertyID;
                props.DirtyFlags = DirtyFlag.All;
                _property_delta_buffer.UpdateInstance(props, instance_deleted: true);
            }
        }

        /// <summary>
        /// [Main Thread] Contains meshtype?
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public bool ContainsType(in Mesh mesh, in Material material)
        {
            AssertInitializedAndMainThread();

            return meshtypes.ContainsKey(new MeshTypeKey(mesh, material));
        }

        /// <summary>
        /// [Main Thread] Get Mesh type. Returns null if not found
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="material"></param>
        /// <returns></returns>
        public MeshType GetMeshType(in Mesh mesh, in Material material)
        {
            AssertInitializedAndMainThread();

            MeshType t = null;
            this.meshtypes.TryGetValue(new MeshTypeKey(mesh, material), out t);
            return t;
        }

        /// <summary>
        /// [Main Thread] Was the input controller added to the instancemesh?
        /// </summary>
        /// <param name="c"></param>
        /// <returns></returns>
        public bool ContrainsController(in AnimationController c)
        {
            AssertInitializedAndMainThread();

            return this._controllers.Contains(c);
        }

        /// <summary>
        /// [Main Thread] Remove a mesh type!
        /// </summary>
        /// <param name="type"></param>
        public void DeleteMeshType(MeshType type)
        {
            AssertInitializedAndMainThread();

            if (type == null || !type.Initialized || type.shared_material == null || type.shared_mesh == null || type.mData == null)
                throw new System.Exception("Error, input mesh type is invalid. Cannot be deleted.");

            var key = new MeshTypeKey(type);
            if (type == null || !this.meshtypes.ContainsKey(new MeshTypeKey(type)))
                throw new System.Exception("Error, tried to delete meshtype that doesnt exist");

            if (this.groupLOD[groupLODIndex(type.groupID)] != type.groupID)
                throw new System.Exception("Error, type input type is not the same as the one being deleted in the group LOD buffer!");

            if (this._delta_buffer.NumInstancesForGroup(type.groupID) != 0)
                throw new System.Exception("Error, cannot delete this mesh type! It still has allocated gpu instances! Please delete all instances before deleting the mesh type.");

            //remove from mesh types
            if (!this.meshtypes.Remove(key))
                throw new System.Exception("Error, failed to remove mesh type!");

            //reset indirect args data
            var type_data = type.mData;
            this.args[type_data.argsIntOffset] = 0;
            this.args[type_data.argsIntOffset + 1] = 0;
            this.args[type_data.argsIntOffset + 2] = 0;
            this.args[type_data.argsIntOffset + 3] = 0;
            this.args[type_data.argsIntOffset + 4] = 0;

            // reset group lod data
            var index = groupLODIndex(type.groupID);
            for (int i = 0; i < groupLODStride(); i++)
                this.groupLOD[index + i] = 0;

            // add to pending queues
            this.pending_group_updates.Add(type.groupID);
            this.pending_lod_group_updates.Add(type.groupID);

            // remove pose
            if (type.IsSkinnedMesh())
                this._bind_pose_delta_buffer.RemovePose(type.groupID);

            this.GroupIDS.ReleaseID(type.groupID);

            //set null id
            type.Dispose();
        }

        /// <summary>
        /// get a camera's frustum
        /// </summary>
        /// <param name="cam"></param>
        /// <param name="frustum"></param>
        /// <param name="DEBUG"></param>
        void camfrustum(Camera cam, Vector3[] frustum, bool DEBUG = false)
        {
            var nearCenter = cam.transform.position + cam.transform.forward * cam.nearClipPlane;
            var farCenter = cam.transform.position + cam.transform.forward * cam.farClipPlane;

            var fovRadians = cam.fieldOfView * Mathf.Deg2Rad;

            var nearHeight = 2 * Mathf.Tan(fovRadians / 2) * cam.nearClipPlane;
            var farHeight = 2 * Mathf.Tan(fovRadians / 2) * cam.farClipPlane;
            var nearWidth = nearHeight * cam.aspect;
            var farWidth = farHeight * cam.aspect;

            var camUp = cam.transform.up;
            var camRight = cam.transform.right;

            var farTopLeft = farCenter + camUp * (farHeight * 0.5f) - camRight * (farWidth * 0.5f);
            var farTopRight = farCenter + camUp * (farHeight * 0.5f) + camRight * (farWidth * 0.5f);
            var farBottomLeft = farCenter - camUp * (farHeight * 0.5f) - camRight * (farWidth * 0.5f);
            var farBottomRight = farCenter - camUp * (farHeight * 0.5f) + camRight * (farWidth * 0.5f);

            var nearTopLeft = nearCenter + camUp * (nearHeight * 0.5f) - camRight * (nearWidth * 0.5f);
            var nearTopRight = nearCenter + camUp * (nearHeight * 0.5f) + camRight * (nearWidth * 0.5f);
            var nearBottomLeft = nearCenter - camUp * (nearHeight * 0.5f) - camRight * (nearWidth * 0.5f);
            var nearBottomRight = nearCenter - camUp * (nearHeight * 0.5f) + camRight * (nearWidth * 0.5f);

            var near = new Plane(cam.transform.forward, nearCenter);
            var far = new Plane(-cam.transform.forward, farCenter);
            var left = new Plane(farTopLeft, farBottomLeft, nearBottomLeft);
            var right = new Plane(farTopRight, nearTopRight, nearBottomRight);
            var top = new Plane(farTopLeft, nearTopLeft, nearTopRight);
            var bottom = new Plane(farBottomLeft, farBottomRight, nearBottomRight);

            frustum[0] = nearCenter;
            frustum[1] = near.normal;
            frustum[2] = farCenter;
            frustum[3] = far.normal;
            frustum[4] = nearBottomLeft;
            frustum[5] = left.normal;
            frustum[6] = nearTopRight;
            frustum[7] = right.normal;
            frustum[8] = nearTopLeft;
            frustum[9] = top.normal;
            frustum[10] = nearBottomRight;
            frustum[11] = bottom.normal;

            if (DEBUG)
            {

                Debug.DrawLine(farTopLeft, farTopRight, Color.red);
                Debug.DrawLine(farBottomRight, farTopRight, Color.red);
                Debug.DrawLine(farBottomRight, farBottomLeft, Color.red);
                Debug.DrawLine(farBottomLeft, farTopLeft, Color.red);

                Debug.DrawLine(nearTopLeft, nearTopRight, Color.red);
                Debug.DrawLine(nearBottomRight, nearTopRight, Color.red);
                Debug.DrawLine(nearBottomRight, nearBottomLeft, Color.red);
                Debug.DrawLine(nearBottomLeft, nearTopLeft, Color.red);

                Debug.DrawLine(nearTopLeft, farTopLeft, Color.red);
                Debug.DrawLine(nearTopRight, farTopRight, Color.red);
                Debug.DrawLine(nearBottomLeft, farBottomLeft, Color.red);
                Debug.DrawLine(nearBottomRight, farBottomRight, Color.red);

                Debug.DrawLine((farTopLeft + farBottomLeft + nearTopLeft + nearBottomLeft) / 4,
                    (farTopLeft + farBottomLeft + nearTopLeft + nearBottomLeft) / 4 + 100 * left.normal);
                Debug.DrawLine((farTopRight + farBottomRight + nearBottomRight + nearTopRight) / 4,
                    (farTopRight + farBottomRight + nearBottomRight + nearTopRight) / 4 + 100 * right.normal);
                Debug.DrawLine(nearCenter, nearCenter + near.normal * 100);
                Debug.DrawLine(farCenter, farCenter + far.normal * 100);
                Debug.DrawLine((nearBottomLeft + nearBottomRight + farBottomLeft + farBottomRight) / 4,
                    (nearBottomLeft + nearBottomRight + farBottomLeft + farBottomRight) / 4 + bottom.normal * 100);
                Debug.DrawLine((nearTopLeft + nearTopRight + farTopLeft + farTopRight) / 4,
                    (nearTopLeft + nearTopRight + farTopLeft + farTopRight) / 4 + top.normal * 100);
            }
        }

        /// <summary>
        /// Helper class that generates instance ids
        /// </summary>
        public class InstanceIDGenerator
        {
            private int _max_instance_id = 0;
            private Queue<int> _free_ids;

            /// <summary>
            /// largest ID allowed to be created
            /// </summary>
            public int MaxAllowedID { get; private set; }
            /// <summary>
            /// Largest id generated by this object
            /// </summary>
            public int MaxAllocatedID { get { return this._max_instance_id; } }

            public InstanceIDGenerator(int MaxAllowedID = int.MaxValue-1)
            {
                _free_ids = new Queue<int>();
                this.MaxAllowedID = MaxAllowedID;
            }

            public int InstanceCount { get; private set; }

            public int GetNewID()
            {
                this.InstanceCount++;

                if (_free_ids.Count == 0)
                {
                    var new_id = this._max_instance_id + 1; // increment from previous max
                    if (new_id > this.MaxAllowedID)
                        throw new System.Exception("Error, reached maximum allowed ID!");

                    this._max_instance_id = new_id;
                    return new_id;
                }
                else
                {
                    return this._free_ids.Dequeue();
                }
            }

            public void ReleaseID(int id)
            {
                if (id > this.MaxAllowedID || id > this.MaxAllocatedID)
                    throw new System.Exception("Errorm input id is too large!");
                if (id <= 0)
                    throw new System.Exception("Invalid id realease attempt onto id queue");
                this._free_ids.Enqueue(id);
                this.InstanceCount--;
            }
        }
    }
}