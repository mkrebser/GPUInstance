using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Unity.Collections;

namespace GPUAnimation
{
    /// <summary>
    /// Controller for animating groups of instance mesh objects
    /// </summary>
    [System.Serializable]
    public class AnimationController : System.IEquatable<AnimationController>
    {
        /// <summary>
        /// Unity Animation Controller
        /// </summary>
        [ReadOnly]
        public UnityEngine.RuntimeAnimatorController unity_controller;
        /// <summary>
        /// Name of this anim controller
        /// </summary>
        [ReadOnly]
        public string name;
        /// <summary>
        /// All of the individual animations for each instance
        /// </summary>
        [HideInInspector]
        public Animation[] animations;
        /// <summary>
        /// each bone may be parented to another bone, here is the parent information. Each index represents a bone.
        /// Each value at an index represents it's parent(where the value corresponds to an index in this array).
        /// A value of -1 means no parent. It should also be said that an index in this array directly correlates to
        /// indices in all animations[0..N].bone_animations arrays.
        /// </summary>
        [ReadOnly]
        public int[] bone_parents;
        /// <summary>
        /// each bone may have a name
        /// </summary>
        [ReadOnly]
        public string[] bone_names;
        /// <summary>
        /// animation bodies that go with the bones
        /// </summary>
        /// <summary>
        /// All unique bone animations
        /// </summary>
        [HideInInspector]
        public BoneAnimation[] bone_animations;
        /// <summary>
        /// The number of bones
        /// </summary>
        public int BoneCount { get { return bone_parents.Length; } }
        /// <summary>
        /// Has this naimation controller been initialized?
        /// </summary>
        public bool IsIntialized { get { return BoneChildren != null; } }
        /// <summary>
        /// How deep is bone parenting hierarchy
        /// </summary>
        public int BoneHierarchyDepth { get; private set; } = int.MinValue;
        /// <summary>
        /// Lowest detail LOD the bone can animate in before being culled
        /// </summary>
        [ReadOnly]
        public int[] BoneLODLevels;

        /// <summary>
        /// Named bones, each key is the name of a bone. each int is the index in the
        /// 'bone_parents[i], bone_names[i], animations[0...N].bone_animations[i]' arrays
        /// </summary>
        [System.NonSerialized]
        public Dictionary<string, int> namedBones;
        /// <summary>
        /// Animation Name -> Object mapping
        /// </summary>
        [System.NonSerialized]
        public Dictionary<string, Animation> namedAnimations;
        /// <summary>
        /// Jagged arrays representing each bone's children. The first item in each child array is self. Child arrays include all children recurssively.
        /// </summary>
        public List<int>[] BoneChildren { get { return _boneChildren; } private set { this._boneChildren = value; } }
        [System.NonSerialized]
        private List<int>[] _boneChildren;
        /// <summary>
        /// Root bone index for skeletons using this controller.
        /// </summary>
        public const int kRootBoneID = 0;
        /// <summary>
        /// Bone ID used to indicate no parent or invalid bone
        /// </summary>
        public const int kNoBoneParentID = -1;

        /// <summary>
        /// Build Extra data structures and do initialization tasks
        /// </summary>
        public void Initialize()
        {
            for (int i = 0; i < this.bone_parents.Length; i++)
                if (bone_parents[i] >= i)
                    throw new System.Exception("Error, AnimController bones are not properly formatted. Parents should come before children in bones array.");
            if (bone_parents[0] != -1)
                throw new System.Exception("Error, expected root bone in animation controller. Animation controller has not been properly formatted.");
            if (this.BoneLODLevels == null)
                throw new System.Exception("Error, no bone LODS found");

            // make namedBones dictionary
            this.namedBones = new Dictionary<string, int>();
            if (this.bone_names != null)
                for (int i = 0; i < this.bone_names.Length; i++)
                    this.namedBones.Add(this.bone_names[i], i);

            // make namedAnimations dictionary
            this.namedAnimations = new Dictionary<string, Animation>();
            if (this.animations != null)
                for (int i = 0; i < this.animations.Length; i++)
                    this.namedAnimations.Add(this.animations[i].name, this.animations[i]);

            //build child refs
            this.BoneChildren = new List<int>[this.BoneCount];
            for (int i = 0; i < this.BoneCount; i++)
            {
                this.BoneChildren[i] = new List<int>(this.BoneCount);
                this.BoneChildren[i].Add(i);
            }

            for (int i = 0; i < this.BoneCount; i++)
            {
                var p = this.bone_parents[i];
                int depth = 1;
                while (p >= 0)
                {
                    this.BoneChildren[p].Add(i);
                    p = this.bone_parents[p];
                    depth++;
                }

                this.BoneHierarchyDepth = System.Math.Max(this.BoneHierarchyDepth, depth);
            }

            // Initialize bone animations
            for (int i = 0; i < this.animations.Length; i++)
                this.animations[i].InitializeBoneAnimationsArray(this); 
        }

        public override bool Equals(object obj)
        {
            return obj is AnimationController ? this.Equals((AnimationController)obj) : false;
        }
        public override int GetHashCode()
        {
            return this.unity_controller == null ? 0 : this.unity_controller.GetHashCode();
        }
        public bool Equals(AnimationController anim)
        {
            return this.unity_controller == anim.unity_controller;
        }
    }

    /// <summary>
    /// Class that represents a specific animation sequence for all bones
    /// </summary>
    [System.Serializable]
    public class Animation
    {
        public int GPUAnimationID { get; private set; } = GPUInstance.instancemesh.NULL_ID;
        /// <summary>
        /// name of the animation sequence
        /// </summary>
        public string name;
        /// <summary>
        /// animation foreach bone. Each animations_indices[i] represents an index in the AnimationController.bone_animations
        /// array. AnimationController.bone_animations[this.animations_indices[0...N]] are the animations
        /// for each bone.
        /// </summary>
        public int[] animations_indices;
        /// <summary>
        /// Instead of looking up the animations indices, this array can be initialized using 
        /// this.InitializeBoneAnimationsArray(AnimationController parent) method to retreive
        /// animations by reference
        /// </summary>
        [System.NonSerialized]
        public BoneAnimation[] boneAnimations;
        /// <summary>
        /// Returns true if boneAnimations array has been built
        /// </summary>
        public bool IsInitialized { get { return boneAnimations != null; } }

        public void InitializeBoneAnimationsArray(AnimationController parent)
        {
            boneAnimations = new BoneAnimation[animations_indices.Length];
            for (int i = 0; i < boneAnimations.Length; i++)
                boneAnimations[i] = parent.bone_animations[animations_indices[i]];
        }

        public string ToString(AnimationController parent)
        {
            var s = name == null ? "NULL_NAME\n" : name + "\n";
            if (animations_indices != null)
            {
                for (int i = 0; i < animations_indices.Length; i++)
                    s += "[" + parent.bone_names[i] + "], group index: " + animations_indices[i].ToString() +
                        "\n" + parent.bone_animations[animations_indices[i]].ToString() + "\n";
            }
            return s;
        }

        internal void SetGPUAnimID(in int id)
        {
            if (!IsInitialized)
                throw new System.Exception("Error, animation is not initialized. Cannot set id");
            this.GPUAnimationID = id;
        }
    }
}