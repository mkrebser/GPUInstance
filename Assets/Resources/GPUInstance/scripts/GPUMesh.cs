using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GPUAnimation;

namespace GPUInstance
{
    /// <summary>
    /// High-level abstraction that mimicks Mesh behaviour using GPU instances.
    /// </summary>
    public struct GPUMesh
    {
        /// <summary>
        /// root gpu mesh instance. All sub-mesh are parented to this one. To change the animation & transform of the entire skinned mesh.. you only need to update this single instance.
        /// </summary>
        public InstanceData<InstanceProperties> mesh;

        private MeshInstancer m;
        private bool _init;

        public GPUMesh(GPUMeshComponent c, MeshInstancer m, bool has_props = true)
        {
            if (!c.Initialized())
                throw new System.Exception("Error, input GPUMeshComponent must be added to MeshInstancer before creating instances!");

            this.mesh = new InstanceData<InstanceProperties>(c.MeshTypes[0], has_props);
            this.m = m;
            this._init = false;
        }

        public GPUMesh(MeshType c, MeshInstancer m, bool has_props = true)
        {
            this.mesh = new InstanceData<InstanceProperties>(c, has_props);
            this.m = m;
            this._init = false;
        }

        /// <summary>
        /// Initialize mesh.
        /// </summary>
        /// <param name="m"></param>
        public void Initialize()
        {
            if (mesh.groupID <= 0)
                throw new System.Exception("Error, no meshtype has been assigned to skinnedmesh");

            this.m.Initialize(ref mesh); // initialize root mesh
            this.mesh.props_instanceTicks = 0;
            this._init = true;
        }

        public bool Initialized()
        {
            return this._init;
        }

        /// <summary>
        /// Set radius on skinned mesh.
        /// </summary>
        /// <param name="radius"></param>
        public void SetRadius(in float radius)
        {
            this.mesh.radius = radius;
            this.mesh.DirtyFlags = this.mesh.DirtyFlags | DirtyFlag.radius;
        }

        /// <summary>
        /// Updates only the root instance.
        /// </summary>
        public void Update()
        {
            if (!_init)
                throw new System.Exception("Error, skinned mesh is not initialized. Cannot update.");
            this.m.Append(ref mesh);
        }

        /// <summary>
        /// Free all GPU resources held by this object. 
        /// </summary>
        public void Dispose()
        {
            if (!_init)
                throw new System.Exception("Error, skinned mesh has not been initialized. Cannot dispose.");
            this.m.Delete(ref this.mesh);
            this.m = null;
            this.mesh = default(InstanceData<InstanceProperties>);
            this._init = false;
        }
    }
}
