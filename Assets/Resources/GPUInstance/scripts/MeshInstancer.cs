using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Threading;

namespace GPUInstance
{
    // Wrapper object for instancemesh.cs

    /// <summary>
    /// Create, Modify, and Destroy GPU Instances.
    /// </summary>
    public class MeshInstancer : System.IDisposable
    {
        /// <summary>
        /// underlying object that actually runs all the compute shaders and everything else.
        /// </summary>
        private instancemesh mesh { get; set; }

        // Various getters- if you get a null ref exception.. then you forgot to initialize the MeshInstancer object

        /// <summary>
        /// Default mesh type helper. The default is a cube using a basic surface shader with instance coloring
        /// </summary>
        public MeshType Default { get { return this.mesh.Default; } }
        /// <summary>
        /// Camera to perform frustum culling with.
        /// </summary>
        public Camera FrustumCamera { get { return this.mesh.FrustumCamera; } set { this.mesh.FrustumCamera = value; } }
        /// <summary>
        /// Method used for LOD & distance culling calculation.
        /// </summary>
        public instancemesh.FrustumDistanceCullingType DistanceCullingType { get { return this.mesh.DistanceCullingType; } set { this.mesh.DistanceCullingType = value; } }
        /// <summary>
        /// Uniform culling distance value
        /// </summary>
        public float UniformCullingDistance { get { return this.mesh.UniformCameraDistance; } set { this.mesh.UniformCameraDistance = value; } }
        /// <summary>
        /// Ticks since initialized. Each tick is 1/10000 seconds.
        /// </summary>
        public ulong Ticks { get { return this.mesh.Ticks; } }
        /// <summary>
        /// Num steps per path
        /// </summary>
        public int PathCount { get { return this.mesh._paths.PathCount; } }
        /// <summary>
        /// Maximum allowed bones per skeleton
        /// </summary>
        public int SkeletonBoneCount { get { return this.mesh.NumSkeletonBones; } }

        /// <summary>
        /// Maximum allowed mesh and skeleton LOD level
        /// </summary>
        public const int MaxLODLevel = instancemesh.NumLODLevels - 1;
        /// <summary>
        /// Number of LOD levels
        /// </summary>
        public const int NumLODLevels = instancemesh.NumLODLevels;
        /// <summary>
        /// Maximum allowed mesh types allowed to be added to the mesh instancer
        /// </summary>
        public const int MaxMeshTypes = instancemesh.maxMeshTypes;
        /// <summary>
        /// Null instance id. Null path id. Null animation id. '0' is considered a 'null' id for all gpu id values
        /// </summary>
        public const int NULL_ID = instancemesh.NULL_ID;

        /// <summary>
        /// Total number of allocated instances on GPU
        /// </summary>
        public int NumInstancesAllocated
        {
            get
            {
                lock (this._instance_lock)
                {
                    return this.mesh._delta_buffer.IndirectBufferInstanceCount;
                }
            }
        }
        /// <summary>
        /// Num allocated instances for a particular type
        /// </summary>
        /// <param name="m"></param>
        /// <returns></returns>
        public int NumInstancesAllocatedForMeshType(MeshType m)
        {
            lock (this._instance_lock)
            {
                return (int)this.mesh._delta_buffer.NumInstancesForGroup(m.groupID);
            }
        }
        /// <summary>
        /// Number of skeleton allocated on GPU
        /// </summary>
        public int NumSkeletonsAllocated
        {
            get
            {
                lock (this._instance_lock)
                {
                    return this.mesh._skeletons.NumSkeletonInstances;
                }
            }
        }
        /// <summary>
        /// Number of paths allocated to GPU
        /// </summary>
        public int NumPathsAllocated
        {
            get
            {
                lock (this._instance_lock)
                {
                    return this.mesh._paths.NumPathAllocated;
                }
            }
        }
        /// <summary>
        /// Num properties allocated on GPU
        /// </summary>
        public int NumPropertiesAllocated
        {
            get
            {
                lock (this._instance_lock)
                {
                    return this.mesh.PropertyIDs.InstanceCount;
                }
            }
        }

        private object _instance_lock = new object();

        public int NumMeshTypesAllocated
        {
            get
            {
                return this.mesh.MeshTypeCount;
            }
        }

        public bool Initialized() { return this.mesh != null; }

        /// <summary>
        /// [Main Thread] Initialize the mesh instancer
        /// </summary>
        /// <param name="deltaBufferCount"> Size (in number of instances) of the GPU delta buffer. The delta buffer pushes updates to the gpu instances. The size should be the average expected number of instance updates per frame. </param>
        /// <param name="InitialInstanceBufferCount"> Initial size of the GPU instance buffer (num instances). The GPU instance buffer dynamically resizes with more instances (but does not size-down) </param>
        /// <param name="initialSkeletonCount"> Initial size of the skeleton GPU buffer (in num skeletons) . Works the same as the instance buffer. </param>
        /// <param name="max_parent_depth"> Maximum allowed parent hierarchy depth. Note* each depth level will add GPU overhead. </param>
        /// <param name="num_skeleton_bones"> All skeleton will allocate space for this number of bones. </param>
        /// <param name="InitialPathBufferSize"> Initial number of paths allocaed on gpu buffers</param>
        /// <param name="PathDeltaBufferSize"> Max number of paths that can be sent to the GPU per update kernel. Try to set this to the expected number of path updates per frame. </param>
        /// <param name="override_material"> override the default material </param>
        /// <param name="override_mesh"> override the default mesh </param>
        /// <param name="pathCount"> How many 3d points are allocated for each path. (Paths dont have to use all allocated points- they just cant use any more) </param>
        /// <param name="InitialPropertyBufferSize"> Initial property buffer size in number of instances. </param>
        /// <param name="PropertyBufferMaxDeltaCount"> Max number of instances that can be sent to the GPU per update kernel. Try to set this to the expected number of property updates per frame. </param>
        public void Initialize(int InitialInstanceBufferCount = 128,
            int deltaBufferCount = 10240, 
            int initialSkeletonCount = 128,
            Mesh override_mesh = null, 
            Material override_material = null,
            int max_parent_depth = 14,
            int num_skeleton_bones = 32,
            int pathCount=8,
            int InitialPropertyBufferSize = 128,
            int PropertyBufferMaxDeltaCount = 10240,
            int InitialPathBufferSize = 128,
            int PathDeltaBufferSize = 1280)
        {
            lock (this._instance_lock)
            {
                if (Initialized())
                    throw new System.Exception("Error, already initialized.");

                this.mesh = new instancemesh();
                this.mesh.Initialize(
                    maxDeltaCount: deltaBufferCount,
                    InitialMaxInstanceCount: InitialInstanceBufferCount,
                    InitialMaxSkeletonCount: initialSkeletonCount,
                    override_mesh: override_mesh,
                    override_material: override_material,
                    MaxParentDepth: max_parent_depth,
                    NumSkeletonBones: num_skeleton_bones,
                    pathCount: pathCount,
                    InitialPropertyBufferSize: InitialPropertyBufferSize,
                    PropertyBufferMaxDeltaCount: PropertyBufferMaxDeltaCount,
                    InitialPathBufferSize: InitialPathBufferSize,
                    PathDeltaBufferSize: PathDeltaBufferSize);
            }
        }

        /// <summary>
        /// [Main Thread] Send update buffer to the gpu and dispatch shaders
        /// </summary>
        public void Update(float deltaTime)
        {
            lock (this._instance_lock)
            {
                this.mesh.Update(deltaTime);
            }
        }

        /// <summary>
        ///  Append an item to the update buffer.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data"> data to be appended. The input data will be modified. </param>
        public void Append<T>(ref InstanceData<T> data) where T : InstanceDataProps
        {
            lock (this._instance_lock)
            {
                if (data.DirtyFlags == DirtyFlag.None)
                    return;

                if (!data.gpu_initialized)
                    throw new System.Exception("Error, cannot update uninitialized data");

                this.mesh.Set<InstanceData<T>>(data);

                data.DirtyFlags = DirtyFlag.None;//set not dirty
                return;
            }
        }

        /// <summary>
        ///  Append many items to the update buffer. 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data"></param>
        public void AppendMany<T>(InstanceData<T>[] data) where T : InstanceDataProps
        {
            lock (this._instance_lock)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    if (data[i].DirtyFlags == DirtyFlag.None)
                        continue;

                    if (!data[i].gpu_initialized)
                        throw new System.Exception("Error, data object not initialized.");

                    this.mesh.Set<InstanceData<T>>(data[i]);

                    data[i].DirtyFlags = DirtyFlag.None; //set not dirty
                }
            }
        }

        /// <summary>
        ///  Initialize a mesh instance. This will automatically set the IInstanceMeshData.id, and groupID
        /// as well as IGPUInstance.gpu_initialized fields. **For this function, Only set the data.parentID to an Instane who has already been initialized
        /// by the MeshInstancer.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data"></param>
        public void Initialize<T>(ref InstanceData<T> data) where T : InstanceDataProps
        {
            lock (this._instance_lock)
            {
                //initialize if necessary
                if (!data.gpu_initialized)
                {
                    int free_id = this.mesh.IDs.GetNewID();
                    int free_prop_id = data.HasProperties ? this.mesh.PropertyIDs.GetNewID() : instancemesh.NULL_ID;

                    data.id = free_id; //assign free gpu id
                    data.propertyID = free_prop_id;
                    data.gpu_initialized = true; //set to initialized

                    if (data.parentID < 0)
                        data.parentID = instancemesh.NULL_ID;

                    if (ReferenceEquals(null, data.mesh_type))
                        throw new System.Exception("Error, must have a non-null mesh type for instance");

                    data.groupID = data.mesh_type.groupID; //set type
                    return;
                }
                else
                    throw new System.Exception("Error, input data already initialized.");
            }
        }

        /// <summary>
        ///  Initialize many mesh instances. This will automatically set the IInstanceMeshData.id, and groupID
        /// as well as IGPUInstance.gpu_initialized fields. This function will also observe LOCAL parent relationships.
        /// Ie. If data[0].parentID = 1, then data[0] will be drawn as a child of data[1]. Set parentID to less than 0 to have no parent.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data"></param>
        public void InitializeSet<T>(InstanceData<T>[] data) where T : InstanceDataProps
        {
            lock (this._instance_lock)
            {
                //initialize ids
                for (int i = 0; i < data.Length; i++)
                {
                    if (!data[i].gpu_initialized)
                    {
                        int free_id = this.mesh.IDs.GetNewID();
                        int free_prop_id = data[i].HasProperties ? this.mesh.PropertyIDs.GetNewID() : instancemesh.NULL_ID;

                        data[i].id = free_id; //assign free gpu id
                        data[i].propertyID = free_prop_id;
                        data[i].gpu_initialized = true; //set to initialized

                        if (ReferenceEquals(null, data[i].mesh_type))
                            throw new System.Exception("Error, must have a non-null mesh type for instance");

                        data[i].groupID = data[i].mesh_type.groupID; //set type
                    }
                    else
                        throw new System.Exception("Error, no entries in InitiazeSet should already be initialized.");
                }

                //initialize parents and set data
                for (int i = 0; i < data.Length; i++)
                {
                    if (data[i].parentID >= 0)
                        data[i].parentID = data[data[i].parentID].id; //set parent id to appropriate gpu id
                    else
                        data[i].parentID = instancemesh.NULL_ID; //otherwise, set parent to null
                }
            }
        }

        /// <summary>
        ///  Delete a mesh instance. 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data"></param>
        public void Delete<T>(ref InstanceData<T> data) where T : InstanceDataProps
        {
            lock (this._instance_lock)
            {
                if (data.id == instancemesh.NULL_ID)
                    throw new System.Exception("Error, input id to delete is 0 (NULL)");
                if (data.id < 0)
                    throw new System.Exception("No negative ids");
                if (!data.gpu_initialized)
                    throw new System.Exception("Error, not initialized");
                if (ReferenceEquals(null, data.mesh_type) || data.groupID == instancemesh.NULL_ID)
                    throw new System.Exception("Error, input instance data does not have an assigned mesh type!");
                if ((data.HasProperties && data.propertyID == instancemesh.NULL_ID) || (!data.HasProperties && data.propertyID != instancemesh.NULL_ID))
                    throw new System.Exception("Error, input propertyID is invalid");
                if (data.propertyID < 0)
                    throw new System.Exception("Error, input propertyID is negative.");

                mesh.Delete(data);
                this.mesh.IDs.ReleaseID(data.id);
                if (data.HasProperties) this.mesh.PropertyIDs.ReleaseID(data.propertyID);

                data.id = instancemesh.NULL_ID;
                data.gpu_initialized = false;
                data.parentID = instancemesh.NULL_ID;
            }
        }

        /// <summary>
        ///  Delete Many mesh instances. 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data"></param>
        public void DeleteMany<T>(InstanceData<T>[] data) where T : InstanceDataProps
        {
            lock (this._instance_lock)
            {
                // delete in reverse order because skeletons will commonly be deleted by this function-
                // child instances are sorted towards the end of the array. Deleting children first in complex hierarchies will be faster (ie, deleting a parent forces updates to its children recurssivley)
                // skeletons are sorted by hierarchy depth- so deleting in reverse order results in minimal hierarchy updates
                for (int i = data.Length-1; i > -1; i--)
                {
                    if (data[i].id == instancemesh.NULL_ID)
                        throw new System.Exception("Error, input id to delete is 0 (NULL)");
                    if (data[i].id < 0)
                        throw new System.Exception("No negative ids");
                    if (!data[i].gpu_initialized)
                        throw new System.Exception("Error, not initialized");
                    if (ReferenceEquals(null, data[i].mesh_type) || data[i].groupID == instancemesh.NULL_ID)
                        throw new System.Exception("Error, input instance data does not have an assigned mesh type!");
                    if ((data[i].HasProperties && data[i].propertyID == instancemesh.NULL_ID) || (!data[i].HasProperties && !data[i].IsBone && data[i].propertyID != instancemesh.NULL_ID))
                        throw new System.Exception("Error, input propertyID is invalid");
                    if (data[i].propertyID < 0)
                        throw new System.Exception("Error, input propertyID is negative.");

                    mesh.Delete(data[i]);
                    this.mesh.IDs.ReleaseID(data[i].id);
                    if (data[i].HasProperties) this.mesh.PropertyIDs.ReleaseID(data[i].propertyID);

                    data[i].id = instancemesh.NULL_ID;
                    data[i].gpu_initialized = false;
                    data[i].parentID = instancemesh.NULL_ID;
                }
            }
        }

        /// <summary>
        /// [Main Thread] Add a new mesh type. 
        /// </summary>
        /// <param name="type"></param>
        public MeshType AddNewMeshType(in UnityEngine.Mesh mesh, in UnityEngine.Material material, UnityEngine.Rendering.ShadowCastingMode mode = UnityEngine.Rendering.ShadowCastingMode.Off, bool receive_shadows = false)
        {
            lock (this._instance_lock)
            {
                return this.mesh.AddInstancedMesh(mesh: mesh, mat: material, mode: mode, receive_shadows: receive_shadows);
            }
        }

        /// <summary>
        /// [Main Thread]  Add a GPUSkinnedMesh. This will allow for instancing of the desired skinned mesh. This function is somewhat expensive. Try to add all mesh types on start.
        /// </summary>
        /// <param name="c">input GPUSkinnedMeshComponent</param>
        /// <param name="ignore_already_added_types"> Ignore exceptions for mesh types that have already been added? If true, the existing mesh type will just be retrieved rather than created. </param>
        /// <param name="override_shadows"> If true, the input shadow_mode & receive_shadows will be used, rather than what is defined by the skin. </param>
        /// <returns></returns>
        public MeshType[][] AddGPUSkinnedMeshType(in GPUSkinnedMeshComponent c, bool ignore_already_added_mesh_types=true, bool override_shadows = false,
            UnityEngine.Rendering.ShadowCastingMode shadow_mode = UnityEngine.Rendering.ShadowCastingMode.Off, bool receive_shadows = false)
        {
            lock (this._instance_lock)
            {
                // do a bunch of validation checks, otherwise, bad errors could happen
                if (!this.Initialized())
                    throw new System.Exception("Error, the MeshInstancer has not been initialized. Please Initialize it before adding mesh types.");
                if (ReferenceEquals(c, null) || ReferenceEquals(c.lods, null) || c.lods.Count == 0 || ReferenceEquals(null, c.anim))
                    throw new System.Exception("Error, the input GPUSkinnedMeshComponent is not properly formatted.");
                if (c.MeshTypes != null && c.MeshTypes.Length > 0)
                    throw new System.Exception("Error, the input GPUSkinnedMeshComponent has already been added to the MeshInstancer");
                if (!c.anim.IsIntialized)
                    throw new System.Exception("Error, input animator has not been initialized");

                var num_lods = c.lods.Count;
                if (num_lods > 5)
                    throw new System.Exception("Error, maximum of 5 lod levels are support!");

                if (this.mesh.MeshTypeCountRemaining < num_lods)
                    throw new System.Exception(string.Format("Error, the MeshInstancer has reached a maximum support mesh type count of [{0}] mesh types!", this.mesh.MaxMeshTypes));

                if (!this.mesh.ContrainsController(c.anim))
                    throw new System.Exception("Error, the input GPUSkinnedMesh has animation controllers that have not been added to the MeshInstancer!");

                if (c.lods.Count <= 0 || c.lods[0].skins.Count <= 0)
                    throw new System.Exception("Error, there should always be atleast one skin at LOD0 in GPUSkinnedMeshComponent");

                if (c.LOD_Radius_Ratios == null || c.LOD_Radius_Ratios.Length != instancemesh.NumLODLevels)
                    throw new System.Exception("Error, input LOd radius buffer is invalid");

                int total_num_types = 0;
                int num_skins = c.lods[0].skins.Count;
                for (int lod = 0; lod < c.lods.Count; lod++)
                {
                    if (c.lods[lod].skins != null)
                    {
                        if (c.lods[lod].skins.Count != num_skins)
                            throw new System.Exception("Error, there should be the same number of skins for every existing LOD level on GPUSKinnedMeshComponent");

                        for (int ski = 0; ski < c.lods[lod].skins.Count; ski++)
                        {
                            if (c.lods[lod].skins[ski] != null)
                                total_num_types++; // increment num types count
                        }
                    }

                    if (lod == 0)
                    {
                        for (int ski = 0; ski < c.lods[0].skins.Count; ski++)
                            if (c.lods[0].skins[ski] == null)
                                throw new System.Exception("Error, LOD0 skins cannot be null");
                    }
                }
                if (num_skins <= 0)
                    throw new System.Exception("Error, the input GPUSkinnedMeshComponent had no skinned mesh components!");

                if (total_num_types > this.mesh.MeshTypeCountRemaining)
                    throw new System.Exception(string.Format("Error, max mesh type count: [{0}] will be exceeded if the input GPUSkinnedMeshComponent is added.", this.mesh.MaxMeshTypes));

                // And now we can finnally actually add the mesh types. c.MeshTypes is allocated to NLods x NSkins
                c.MeshTypes = new MeshType[instancemesh.NumLODLevels][];
                for (int i = 0; i < instancemesh.NumLODLevels; i++) c.MeshTypes[i] = new MeshType[num_skins];
                for (int skin_index = 0; skin_index < num_skins; skin_index++)
                {
                    for (int lod = 0; lod < c.lods.Count; lod++)
                    {
                        var skin = c.lods[lod].skins[skin_index];
                        if (skin != null)
                        {
                            // create (or retrieve) mesh types for every single unique skinned mesh
                            var mesh = skin.sharedMesh;
                            var mat = skin.sharedMaterial;
                            var mtype = this.mesh.GetMeshType(mesh, mat);

                            if (ignore_already_added_mesh_types && mtype != null)
                                throw new System.Exception("Error, input mesh already exists! Cannot add it again!");

                            if (mtype == null) // if null, then a new mesh type needs to be created
                            {
                                var mode = override_shadows ? shadow_mode : skin.shadowCastingMode;
                                var rcv_shadow = override_shadows ? receive_shadows : skin.receiveShadows;
                                mtype = this.mesh.AddInstancedMesh(mesh: mesh, mat: mat, mode: mode, receive_shadows: rcv_shadow);
                            }

                            c.MeshTypes[lod][skin_index] = mtype;
                        }
                    }
                }

                // now we just fill null entries in the c.MeshTypes Array
                for (int skin_index = 0; skin_index < num_skins; skin_index++)
                {
                    for (int lod = 1; lod < instancemesh.NumLODLevels; lod++) // lod 0 is guarenteed to be filled for all skins!
                    {
                        if (c.MeshTypes[lod][skin_index] == null)
                        {
                            c.MeshTypes[lod][skin_index] = c.MeshTypes[lod - 1][skin_index]; // set to previous lod
                        }
                    }
                }

                // now tell instancemesh object about the LOD relationships
                for (int skin_index = 0; skin_index < num_skins; skin_index++)
                {
                    this.mesh.AddInstanceLOD(c.MeshTypes[0][skin_index], c.MeshTypes[1][skin_index], c.MeshTypes[2][skin_index], c.MeshTypes[3][skin_index], c.MeshTypes[4][skin_index], c.LOD_Radius_Ratios);
                }

                // Enforce max skin weights
                for (int lod = 0; lod < c.lods.Count; lod++)
                {
                    for (int skin_index = 0; skin_index < num_skins; skin_index++)
                    {
                        // Get quality level from skinned mesh renderers
                        var max_weight = (UnityEngine.SkinWeights)(int)c.lods[lod].skins[skin_index].quality;
                        max_weight = (int)max_weight < 1 ? SkinWeights.FourBones : max_weight;
                        if (c.MeshTypes[lod][skin_index].SkinWeight > max_weight)
                        {
                            c.MeshTypes[lod][skin_index].SetBlendWeights(max_weight);
                        }
                    }
                }

                // return the LOD x skin matrix
                return c.MeshTypes;
            }
        }

        /// <summary>
        /// [Main Thread]  Add a GPUMesh. This will allow for instancing of the desired mesh. This function is somewhat expensive. Try to add all mesh types on start.
        /// </summary>
        /// <param name="c">input GPUMeshComponent</param>
        /// <param name="ignore_already_added_types"> Ignore exceptions for mesh types that have already been added? If true, the existing mesh type will just be retrieved rather than created. </param>
        /// <param name="override_shadows"> If true, the input shadow_mode & receive_shadows will be used, rather than what is defined by the skin. </param>
        /// <returns></returns>
        public MeshType[] AddGPUMeshType(in GPUMeshComponent c, bool ignore_already_added_mesh_types = true, bool override_shadows = false,
            UnityEngine.Rendering.ShadowCastingMode shadow_mode = UnityEngine.Rendering.ShadowCastingMode.Off, bool receive_shadows = false)
        {
            lock (this._instance_lock)
            {
                // do a bunch of validation checks, otherwise, bad errors could happen
                if (!this.Initialized())
                    throw new System.Exception("Error, the MeshInstancer has not been initialized. Please Initialize it before adding mesh types.");
                if (ReferenceEquals(c, null) || ReferenceEquals(c.lods, null))
                    throw new System.Exception("Error, the input GPUMeshComponent is not properly formatted.");
                if (c.lods.Count <= 0)
                    throw new System.Exception("Error, there should always be atleast one skin at LOD0 in GPUMeshComponent");
                if (c.MeshTypes != null && c.MeshTypes.Length > 0)
                    throw new System.Exception("Error, the input GPUMeshComponent has already been added to the MeshInstancer");

                var num_lods = c.lods.Count;
                if (num_lods > 5)
                    throw new System.Exception("Error, maximum of 5 lod levels are support!");

                if (this.mesh.MeshTypeCountRemaining < num_lods)
                    throw new System.Exception(string.Format("Error, the MeshInstancer has reached a maximum support mesh type count of [{0}] mesh types!", this.mesh.MaxMeshTypes));


                if (c.LOD_Radius_Ratios == null || c.LOD_Radius_Ratios.Length != instancemesh.NumLODLevels)
                    throw new System.Exception("Error, input LOd radius buffer is invalid");

                int total_num_types = 0;
                for (int lod = 0; lod < c.lods.Count; lod++)
                {
                    if (c.lods[lod] != null) total_num_types++;
                }
                if (c.lods[0] == null)
                    throw new System.Exception("Error, LOD0 cannot be null");
                if (total_num_types <= 0)
                    throw new System.Exception("Error, the input GPUSkinnedMeshComponent had no skinned mesh components!");

                if (total_num_types > this.mesh.MeshTypeCountRemaining)
                    throw new System.Exception(string.Format("Error, max mesh type count: [{0}] will be exceeded if the input GPUMeshComponent is added.", this.mesh.MaxMeshTypes));

                // And now we can finnally actually add the mesh types. c.MeshTypes is allocated to NLods
                c.MeshTypes = new MeshType[instancemesh.NumLODLevels];
                for (int lod = 0; lod < c.lods.Count; lod++)
                {
                    var mesh_renderer = c.lods[lod];
                    if (mesh_renderer != null)
                    {
                        var mesh_filter = mesh_renderer.GetComponent<MeshFilter>();
                        if (mesh_filter == null)
                            throw new System.Exception(string.Format("Please Add MeshFilter Component to same game object as MeshRenderer for [{0}]", mesh_renderer.name));

                        // create (or retrieve) mesh types for every single unique skinned mesh
                        var mesh = mesh_filter.sharedMesh;
                        var mat = mesh_renderer.sharedMaterial;
                        var mtype = this.mesh.GetMeshType(mesh, mat);

                        if (ignore_already_added_mesh_types && mtype != null)
                            throw new System.Exception("Error, input mesh already exists! Cannot add it again!");

                        if (mtype == null) // if null, then a new mesh type needs to be created
                        {
                            var mode = override_shadows ? shadow_mode : mesh_renderer.shadowCastingMode;
                            var rcv_shadow = override_shadows ? receive_shadows : mesh_renderer.receiveShadows;
                            mtype = this.mesh.AddInstancedMesh(mesh: mesh, mat: mat, mode: mode, receive_shadows: rcv_shadow);
                        }

                        c.MeshTypes[lod] = mtype;
                    }
                }

                // now we just fill null entries in the c.MeshTypes Array
                for (int lod = 1; lod < instancemesh.NumLODLevels; lod++) // lod 0 is guarenteed to be filled for all skins!
                {
                    if (c.MeshTypes[lod] == null)
                    {
                        c.MeshTypes[lod] = c.MeshTypes[lod - 1]; // set to previous lod
                    }
                }

                // now tell instancemesh object about the LOD relationships
                this.mesh.AddInstanceLOD(c.MeshTypes[0], c.MeshTypes[1], c.MeshTypes[2], c.MeshTypes[3], c.MeshTypes[4], c.LOD_Radius_Ratios);

                // return the LOD x skin matrix
                return c.MeshTypes;
            }
        }

        /// <summary>
        /// [Main Thread]  Set all animations controllers
        /// </summary>
        /// <param name="all_controller"></param>
        public void SetAllAnimations(in GPUAnimation.AnimationController[] all_controller)
        {
            lock (this._instance_lock)
            {
                if (!this.Initialized())
                    throw new System.Exception("Error, mesh instancer is not initialized");
                this.mesh.InitializeAnimationsBuffer(all_controller);
            }
        }

        /// <summary>
        /// Set texture anims
        /// </summary>
        /// <param name="tlib"></param>
        public void SetAllTextureAnimations(in TextureAnimationLibrary tlib)
        {
            lock (this._instance_lock)
            {
                if (!this.Initialized())
                    throw new System.Exception("Error, mesh instancer is not initialized");
                this.mesh.InitializeTextureAnimationsBuffer(tlib);
            }
        }

        /// <summary>
        /// [Main Thread]  Delete an existing mesh type. 
        /// </summary>
        /// <param name="type"></param>
        public void DeleteMeshType(MeshType type)
        {
            lock (this._instance_lock)
            {
                this.mesh.DeleteMeshType(type);
            }
        }

        /// <summary>
        /// [Main Thread] Dispose. [Only callable from Unity Main Thread]
        /// </summary>
        public void Dispose()
        {
            lock (this._instance_lock)
            {
                if (mesh != null)
                    mesh.Dispose();
            }
        }

        /// <summary>
        /// Allocate new path on gpu. This function will update the input path struct with a path_id
        /// </summary>
        /// <param name="p"></param>
        /// <param name="path"></param>
        /// <param name="start_index">optional start index in path array if you have all your paths in a single array. Note* the number of points copied is always the global MeshInstancer.PathCount starting at the start_index param</param>
        /// <param name="path_t"> (optional) Path timing </param>
        /// <param name="path_up"> (optional) Path up directions </param>
        public void AllocateNewPath(ref Path p, Vector3[] path, float[] path_t, Vector3[] path_up, int start_index = 0)
        {
            lock (this._instance_lock)
            {
                if (!this.Initialized())
                    throw new System.Exception("Error, mesh instancer is not initialized");
                this.mesh._paths.AllocateNewPath(ref p, path, path_t, path_up, start_index);
            }
        }
        /// <summary>
        /// Delete path from gpu
        /// </summary>
        /// <param name="p"></param>
        public void DeletePath(ref Path p)
        {
            lock (this._instance_lock)
            {
                if (!this.Initialized())
                    throw new System.Exception("Error, mesh instancer is not initialized");
                this.mesh._paths.DeletePath(ref p);
            }
        }
        /// <summary>
        /// Update path on gpu. Note* Updating a path without updating instances that use it will cause desynchronization*
        /// </summary>
        /// <param name="p"></param>
        /// <param name="path"></param>
        /// <param name="start_index"></param>
        public void UpdatePath(ref Path p, Vector3[] path, float[] path_t, Vector3[] path_up, int start_index = 0)
        {
            lock (this._instance_lock)
            {
                if (!this.Initialized())
                    throw new System.Exception("Error, mesh instancer is not initialized");
                this.mesh._paths.UpdatePath(ref p, path, path_t, path_up, start_index);
            }
        }
        public int AllocateNewPathID()
        {
            lock (this._instance_lock)
            {
                if (!this.Initialized())
                    throw new System.Exception("Error, mesh instancer is not initialized");
                return this.mesh._paths.AllocateNewPathID();
            }
        }

        public int GetNewSkeletonID()
        {
            lock (this._instance_lock)
            {
                return this.mesh._skeletons.GetNewSkeletonID();
            }
        }
        public void ReleaseSkeletonID(in int skeleton_id)
        {
            lock (this._instance_lock)
            {
                this.mesh._skeletons.ReleaseSkeletonID(skeleton_id);
            }
        }
        /// <summary>
        /// Attempt to get MeshType object for input (mesh, material). Returns null if not found.
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="material"></param>
        /// <returns></returns>
        public MeshType GetMeshType(in Mesh mesh, in Material material)
        {
            lock (this._instance_lock)
            {
                return this.mesh.GetMeshType(mesh, material);
            }
        }
    }
}
