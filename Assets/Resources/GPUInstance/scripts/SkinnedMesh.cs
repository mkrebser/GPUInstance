using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GPUAnimation;

namespace GPUInstance
{
    /// <summary>
    /// High-level abstraction that mimicks Skinned-Mesh behaviour using GPU instances.
    /// </summary>
    public struct SkinnedMesh
    {
        /// <summary>
        /// root gpu skinned mesh instance. All sub-mesh are parented to this one. To change the animation & transform of the entire skinned mesh.. you only need to update this single instance.
        /// </summary>
        public InstanceData<InstanceProperties> mesh;
        /// <summary>
        /// Skinned mesh can be composed of multiple gpu instances... These are all parented to 'mesh'. This way only one instance needs it transform updated.
        /// However... For some properties/other features you may need to update all GPU instances.
        /// </summary>
        public InstanceData<InstanceProperties>[] sub_mesh;
        public Skeleton skeleton;

        private ulong _anim_tick_start;
        private MeshInstancer m;
        private GPUAnimation.Animation _current_anim;
        private bool _init;

        /// <summary>
        /// Number of GPU Instances that this object will use.
        /// </summary>
        public int InstanceCount { get { return skeleton.data.Length + 1; } }

        public SkinnedMesh(MeshType mesh, AnimationController anim, MeshInstancer m)
        {
            this.skeleton = new Skeleton(anim, m);
            this.mesh = new InstanceData<InstanceProperties>(mesh);
            this.m = m;
            this._init = false;
            this._anim_tick_start = 0;
            this._current_anim = null;
            this.sub_mesh = null;
        }

        public SkinnedMesh(GPUSkinnedMeshComponent c, MeshInstancer m, int initial_lod=MeshInstancer.MaxLODLevel)
        {
            if (!c.Initialized())
                throw new System.Exception("Error, input GPUSkinnedMeshComponent must be added to MeshInstancer before creating instances!");

            this.skeleton = new Skeleton(c.anim, m);
            this.m = m;
            this._init = false;
            this._anim_tick_start = 0;
            this._current_anim = null;

            // create parent mesh instance
            this.mesh = new InstanceData<InstanceProperties>(c.MeshTypes[initial_lod][0]);

            // create other additional instances if this skinned mesh needs multiple instances
            if (c.MeshTypes[0].Length > 1)
            {
                this.sub_mesh = new InstanceData<InstanceProperties>[c.MeshTypes[0].Length - 1];
                for (int i = 1; i < c.MeshTypes[0].Length; i++)
                {
                    this.sub_mesh[i - 1] = new InstanceData<InstanceProperties>(c.MeshTypes[initial_lod][i]);
                }
            }
            else
            {
                this.sub_mesh = null;
            }
        }

        /// <summary>
        /// Initialize skinned mesh. (Initializes child AnimationInstance skeleton as well)
        /// </summary>
        /// <param name="m"></param>
        public void Initialize(bool animation_culling = true)
        {
            if (mesh.groupID <= 0)
                throw new System.Exception("Error, no meshtype has been assigned to skinnedmesh");

            this.m.Initialize(ref mesh); // initialize root mesh

            this.mesh.props_AnimationCulling = animation_culling;
            this.mesh.props_animationID = skeleton.Controller.animations[0].GPUAnimationID; // just set to first animation
            this.mesh.props_AnimationSpeed = 1;
            this.mesh.props_instanceTicks = 0;
            this.mesh.skeletonID = m.GetNewSkeletonID(); // get skeleton id

            // Initialize sub mesh
            if (!ReferenceEquals(null, this.sub_mesh))
            {
                for (int i = 0; i < this.sub_mesh.Length; i++)
                {
                    if (this.sub_mesh[i].groupID <= 0)
                        throw new System.Exception("Error, no meshtype has been assigned to skinnedmesh submesh");

                    this.m.Initialize(ref this.sub_mesh[i]);

                    this.sub_mesh[i].props_AnimationCulling = animation_culling;
                    this.sub_mesh[i].props_animationID = skeleton.Controller.animations[0].GPUAnimationID; // just set to first animation
                    this.sub_mesh[i].props_AnimationSpeed = 1;
                    this.sub_mesh[i].props_instanceTicks = 0;
                    this.sub_mesh[i].skeletonID = this.mesh.skeletonID; // get skeleton id

                    this.sub_mesh[i].parentID = this.mesh.id; // parent to root
                }
            }

            this.skeleton.InitializeInstance(m, skeletonID: this.mesh.skeletonID, bone_type: mesh.groupID, radius: 2.0f * this.mesh.radius, property_id: mesh.propertyID); // initialize skeleton
            this.skeleton.SetRootParent(mesh.id); // parent the skeleton to the mesh

            this._init = true;
        }

        public bool Initialized()
        {
            return this._init;
        }

        /// <summary>
        /// Get Position,Rotation,Scale of this skinned mesh- even if it moving along a path.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="p"></param>
        /// <param name="position"></param>
        /// <param name="rotation"></param>
        /// <param name="scale"></param>
        public void CalcTRS(in Path path, in PathArrayHelper p, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = this.mesh.position;
            rotation = this.mesh.rotation;
            scale = this.mesh.scale;

            if (path.path_id > 0)
            {
                Vector3 direction, up;
                p.CalculatePathPositionDirection(path, this.mesh, out position, out direction, out up);
                rotation = Quaternion.LookRotation(direction, up);
            }
        }

        public Matrix4x4 CalcBone2World(in Path path, in PathArrayHelper p, int bone)
        {
            Vector3 position; Quaternion rotation; Vector3 scale;
            CalcTRS(path, p, out position, out rotation, out scale);
            Matrix4x4 mesh2world = Matrix4x4.TRS(position, rotation, scale);
            return this.skeleton.CalculateBone2World(mesh2world, bone, this._current_anim, this._anim_tick_start, this.mesh);
        }

        public void BoneWorldTRS(in Path path, in PathArrayHelper p, int bone, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            var b2w = CalcBone2World(path, p, bone);
            decompose(b2w, out position, out rotation, out scale);
        }

        public Matrix4x4 CalcBone2World(int bone)
        {
            Matrix4x4 mesh2world = Matrix4x4.TRS(mesh.position, mesh.rotation, mesh.scale);
            return this.skeleton.CalculateBone2World(mesh2world, bone, this._current_anim, this._anim_tick_start, this.mesh);
        }

        public void BoneWorldTRS(int bone, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            var b2w = CalcBone2World(bone);
            decompose(b2w, out position, out rotation, out scale);
        }

        void decompose(in Matrix4x4 b2w, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = b2w.GetColumn(3);
            rotation = Quaternion.LookRotation(b2w.GetColumn(2), b2w.GetColumn(1));
            scale = new Vector3(b2w.GetColumn(0).magnitude, b2w.GetColumn(1).magnitude, b2w.GetColumn(2).magnitude);
        }

        /// <summary>
        /// Set radius on skinned mesh.
        /// </summary>
        /// <param name="radius"></param>
        public void SetRadius(in float radius)
        {
            this.mesh.radius = radius;
            this.mesh.DirtyFlags = this.mesh.DirtyFlags | DirtyFlag.radius;

            if (!ReferenceEquals(null, this.sub_mesh))
            {
                for (int i = 0; i < this.sub_mesh.Length; i++)
                {
                    this.sub_mesh[i].radius = radius;
                    this.sub_mesh[i].DirtyFlags = this.sub_mesh[i].DirtyFlags | DirtyFlag.radius;
                }
            }
        }

        /// <summary>
        /// Set the animation
        /// </summary>
        /// <param name="animation"></param>
        /// <param name="bone"></param>
        /// <param name="speed"></param>
        /// <param name="start_time"></param>
        public void SetAnimation(string animation, float speed = 1, float start_time = 0, bool loop = true)
        {
            // This version is safer than SetAnimation with raw animation- animations guarentted to work on this skinned mesh
            var a = skeleton.Controller.namedAnimations[animation];
            SetAnimation(a, speed, start_time, loop);
        }

        /// <summary>
        /// Set the animation
        /// </summary>
        /// <param name="animation"></param>
        /// <param name="bone"></param>
        /// <param name="speed"></param>
        /// <param name="start_time"></param>
        public void SetAnimation(GPUAnimation.Animation animation, float speed = 1, float start_time = 0, bool loop = true)
        {
            //TODO? Add a safe version that check if the animation can even be played by this skinned mesh- (ie, if it doesn't belong then you get spaghetti model)

            if (!_init)
                throw new System.Exception("Error, skinned mesh is not initialized.");

            this.mesh.props_animationID = animation.GPUAnimationID;// animation.boneAnimations[n].id;
            this.mesh.props_instanceTicks = (uint)(start_time * Ticks.TicksPerSecond); // reset ticks
            this.mesh.props_AnimationSpeed = speed;
            this.mesh.props_AnimationPlayOnce = !loop;
            this.mesh.DirtyFlags = this.mesh.DirtyFlags | DirtyFlag.props_AnimationID | DirtyFlag.props_Extra | DirtyFlag.props_InstanceTicks;
            this._current_anim = animation;
            this._anim_tick_start = this.m.Ticks;
        }

        /// <summary>
        /// Update the mesh instance data & update the animation instance skeleton & update gpu skeleton map.  Note* do not call Update() & dispose() in the same frame. It will cause a race condition on the gpu.
        /// </summary>
        public void UpdateAll()
        {
            if (!_init)
                throw new System.Exception("Error, skinned mesh is not initialized. Cannot update.");
            this.m.Append(ref mesh);

            if (!ReferenceEquals(null, this.sub_mesh))
            {
                this.m.AppendMany(this.sub_mesh);
            }

            skeleton.Update();
        }

        /// <summary>
        /// Updates only the root instance.
        /// </summary>
        public void UpdateRoot()
        {
            if (!_init)
                throw new System.Exception("Error, skinned mesh is not initialized. Cannot update.");
            this.m.Append(ref mesh);
        }

        /// <summary>
        /// Update root mesh & any sub mesh
        /// </summary>
        public void UpdateMesh()
        {
            if (!_init)
                throw new System.Exception("Error, skinned mesh is not initialized. Cannot update.");
            this.m.Append(ref mesh);

            if (!ReferenceEquals(null, this.sub_mesh))
            {
                this.m.AppendMany(this.sub_mesh);
            }
        }

        /// <summary>
        /// Free all GPU resources held by this object. 
        /// </summary>
        public void Dispose()
        {
            if (!_init)
                throw new System.Exception("Error, skinned mesh has not been initialized. Cannot dispose.");
            this.skeleton.Dispose();
            this.skeleton = default(Skeleton);
            this.m.Delete(ref this.mesh);

            if (!ReferenceEquals(null, this.sub_mesh))
            {
                this.m.DeleteMany(this.sub_mesh);
            }
            this.sub_mesh = null;

            this.m.ReleaseSkeletonID(this.mesh.skeletonID);
            this.m = null;
            this._current_anim = null;
            this._anim_tick_start = 0;
            this.mesh = default(InstanceData<InstanceProperties>);
            this._init = false;
        }
    }
}
