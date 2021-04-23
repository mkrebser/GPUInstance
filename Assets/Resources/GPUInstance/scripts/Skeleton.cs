using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GPUInstance;

namespace GPUAnimation
{
    /// <summary>
    /// High-level abstraction over many instances that create a hierarchy of instances that resembles a skeleton.
    /// </summary>
    public struct Skeleton
    {
        /// <summary>
        /// Controller being used for this instance
        /// </summary>
        public AnimationController Controller { get; private set; }
        /// <summary>
        /// instance data- one instance per bone. 
        /// </summary>
        public InstanceData<BoneProperties>[] data { get; private set; }
        MeshInstancer m;
        /// <summary>
        /// Animation instance initialized?
        /// </summary>
        public bool Initialized { get { return m != null; } }

        public Skeleton(AnimationController c, MeshInstancer m)
        {
            if (!c.IsIntialized || !m.Initialized())
                throw new System.Exception("Error, input is not initialized.");
            var default_type = m.Default;
            this.m = null;
            this.Controller = c;
            this.data = new InstanceData<BoneProperties>[c.BoneCount];
            //set bone data
            for (int i = 0; i < c.BoneCount; i++)
            {
                this.data[i] = new InstanceData<BoneProperties>(default_type, has_props: false);
                this.data[i].parentID = c.bone_parents[i]; //set local parent
                this.data[i].Invisible = true; //invisible=true so that nothing is rendered (ie, always will be culled and not added to DrawMeshIndirect)
            }
        }

        /// <summary>
        /// Initialize this AnimationInstance
        /// </summary>
        /// <param name="m"></param>
        public void InitializeInstance(MeshInstancer m, int skeletonID=0, int bone_type=0, float radius=1.0f, int property_id=0)
        {
            if (this.m != null)
                throw new System.Exception("Error, this AnimationInstacne was already initialized.");
            if (!m.Initialized())
                throw new System.Exception("Error, mesh instancer must be initialized!");
            if (bone_type > ushort.MaxValue || bone_type < 0 || skeletonID < 0 || property_id <= 0)
                throw new System.Exception("Error, invalid input in AnimationInstance Input");
            m.InitializeSet(data);
            this.m = m;

            var animation = Controller.animations[0];
            for (int i = 0; i < Controller.BoneCount; i++)
            {
                if (!animation.boneAnimations[i].IsInitialize)
                    throw new System.Exception("Error, bone animation not initialized.");
                data[i].BoneIndex = i;
                data[i].BoneType = (ushort)bone_type;
                data[i].BoneLOD = this.Controller.BoneLODLevels[i]; // set to animate at all LOD by default
                data[i].IsBone = true;
                data[i].skeletonID = skeletonID;
                data[i].radius = radius;
                data[i].propertyID = property_id; // use the supplied propertyID to get a property with all the animation data (ie animation clip, anim speed, time, etc..)
            }
        }

        /// <summary>
        /// Set the root bone's parent id
        /// </summary>
        /// <param name="parentID"></param>
        public void SetRootParent(int parentID)
        {
            if (!Initialized)
                throw new System.Exception("Error, Animation Instance not initialized.");
            if (parentID >= 0)
            {
                var root_bone = AnimationController.kRootBoneID;
                if (data[root_bone].parentID != parentID)
                {
                    data[root_bone].parentID = parentID;
                    data[root_bone].DirtyFlags = data[root_bone].DirtyFlags | DirtyFlag.ParentID;
                }
            }
        }

        /// <summary>
        /// Calculate the position of the input bone in world space. NOTE* make sure this function is called AFTER you invoke instancemesh.Update(), otherwise it will be 1 frame behind!
        /// </summary>
        /// <param name="root2World"> parent object2world matrix </param>
        /// <param name="bone"> bone to calculate for </param>
        /// <param name="m"></param>
        /// <returns></returns>
        public Matrix4x4 CalculateBone2World(in Matrix4x4 root2World, int bone, Animation a, ulong tickStart, in InstanceData<InstanceProperties> mesh)
        {
            if (bone < 0 || bone >= this.Controller.BoneCount)
                throw new System.Exception("Error, input an invalid bone index");

            Matrix4x4 p2w = bone == AnimationController.kRootBoneID ? root2World : CalculateBone2World(root2World, this.Controller.bone_parents[bone], a, tickStart, mesh);


            var bAnim = a.boneAnimations[bone];
            var t = calculateAnimationTime(bone, bAnim, tickStart, mesh);

            var pos = bAnim.InterpPosition(t);
            var rot = bAnim.InterpRotation(t);
            var sca = bAnim.InterpScale(t);

            return p2w * Matrix4x4.TRS(pos, rot, sca);
        }

        float calculateAnimationTime(int bone, BoneAnimation bAnim, ulong tickStart, in InstanceData<InstanceProperties> mesh)
        {
            // get some needed properties
            uint clip_tick_len = bAnim.AnimationTickLength;
            uint anim_speed = mesh.props_AnimationSpeedRaw;
            bool loop = !mesh.props_AnimationPlayOnce;

            // adjust elapsed time by animation speed
            ulong elapsed_ticks = ((this.m.Ticks - tickStart) * anim_speed) / 10; // get elapsed ticks (CurrentTick - Tick when anim what set). Than adjust it by the animation speed.
            elapsed_ticks += mesh.props_instanceTicks; // Add the animation time offset

            // calc current tick
            var anim_tick = loop ? elapsed_ticks % clip_tick_len : (elapsed_ticks >= clip_tick_len ? clip_tick_len - 1 : elapsed_ticks % clip_tick_len);
            return (float)anim_tick / (float)clip_tick_len;
        }

        /// <summary>
        /// Play Animation/ship all changes to the GPU so that they can be displayed (bones are invisible so attatched a body to observe the animation).
        /// </summary>
        public void Update()
        {
            if (!Initialized)
                throw new System.Exception("Error, animation not initialized.");
            m.AppendMany(data);
        }

        /// <summary>
        /// Delete all instances on the GPU for this animation instance skeleton.
        /// </summary>
        public void Dispose()
        {
            m.DeleteMany(data);
            this.Controller = null;
            this.data = null;
            this.m = null;
        }

        public override string ToString()
        {
            if (Controller == null)
                return "NULL_CONTROLLER";
            if (!Controller.IsIntialized)
                return "UNINITIALIZED_CONTROLLER";
            if (!Initialized)
                return "UNINITIALIZED_INSTANCE";
            
            var s = "";
            for (int i = 0; i < Controller.BoneCount; i++)
            {
                s += "----Bone: ";
                s += Controller.bone_names[i] + "\n";
                s += data[i].ToString();
            }

            return s;
        }
    }
}
