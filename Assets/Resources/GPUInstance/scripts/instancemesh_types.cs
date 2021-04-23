using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GPUInstance
{
    public sealed partial class instancemesh : System.IDisposable, InstanceMeshSet
    {
        /// <summary>
        /// A structure that represents data that can be sent to the gpu
        /// </summary>
        public struct instance_delta : System.IEquatable<instance_delta>, InstanceMeshDelta
        {
            /// <summary>
            /// size in bytes of this struct
            /// </summary>
            public const int kByteStride = 72;

            /// <summary>
            /// position of the instanced object. Please note, this should be the center of the object
            /// as this position is also used as the sphere center for culling!
            /// </summary>
            public Vector3 position { get; set; }
            public Quaternion rotation { get; set; }
            public Vector3 scale { get; set; }
            public int id { get; set; }
            public int groupID { get; set; }
            public int data1 { get; set; }
            public int parentID { get; set; }
            public int propertyID { get; set; }
            public int skeletonID { get; set; }
            public int DirtyFlags { get; set; }
            public int data2 { get; set; }

            public instance_delta(in Vector3 position, in Quaternion rotation, in Vector3 scale, in int id, in int groupID, in int data1, in int parentID, in int propertyID, in int skeletonID, in int DirtyFlags, in int data2)
            {
                this.position = position; this.rotation = rotation; this.scale = scale;
                this.id = id; this.groupID = groupID; this.data1 = data1; this.parentID = parentID; this.propertyID = propertyID;
                this.skeletonID = skeletonID; this.DirtyFlags = DirtyFlags; this.data2 = data2;
            }

            public bool Equals(instance_delta d) { return id.Equals(d.id); }
            public override int GetHashCode()
            {
                return id.GetHashCode();
            }
            public override bool Equals(object obj)
            {
                return obj is instance_delta ? Equals((instance_delta)obj) : false;
            }

            public ushort GetGroupID()
            {
                return (ushort)(this.groupID & 65535);
            }
        }

        /// <summary>
        /// Struct used on GPU instance buffer
        /// </summary>
        public struct instance_data // this struct should only have basic/essential info about the instance
        {
            /// <summary>
            /// size in bytes of this struct
            /// </summary>
            public const int kByteStride = 64; // make sure byte alignment to vec4 (apparently has a big perf difference)

            /// <summary>
            /// position of the instanced object. Please note, this should be the center of the object
            /// as this position is also used as the sphere center for culling!
            /// </summary>
            public Vector3 position { get; set; }
            /// <summary>
            /// rotation is assumed to be normalized. please normalize before setting this value.
            /// </summary>
            public Quaternion rotation { get; set; }
            public Vector3 scale { get; set; }
            public int groupID { get; set; }
            public int data1 { get; set; }
            public int parentID { get; set; }
            public int propertyID { get; set; }
            public int skeletonID { get; set; }
            public int data2 { get; set; }

#if UNITY_EDITOR
            public bool IsBone
            {
                get
                {
                    return Bits.GetBit(data1, 25);
                }
            }
            public float Radius
            {
                get
                {
                    return ((float)(data1 & 65535)) * 0.05f;
                }
            }
            public int static_groupID
            {
                get { return this.groupID & 65535; }
            }
            public int dynamic_groupID
            {
                get { return (this.groupID >> 16) & 65535; }
            }
            public bool invisible { get { return Bits.GetBit(data1, 24); } }
            public int lod { get { return (data1 >> 16) & 255; } }
            public int bone_lod { get { return (data2 >> 24) & 255; } }
            public int bone_type { get { return (data2 & 16776960) >> 8; } }
            public int bone_index { get { return data2 & 255; } }
            public bool culled { get { return Bits.GetBit(data1, 26); } }
            public bool bone_anim_culling { get { return Bits.GetBit(data1, 27); } }
#endif
        }

        public struct instance_properties
        {
            public const int kByteStride = 48; // make sure byte alignment to vec4

            public Vector2 offset;
            public Vector2 tiling;
            public int instance_id;
            public int color;
            public uint instanceTicks;
            public int animationID;
            public int pathID;
            public int extra;
            public uint pathInstanceTicks; // path instance ticks- only ever set in shader
            public int pad2;
        }

        public struct instance_properties_delta : InstanceMeshDelta
        {
            public const int kByteStride = 56;

            public Vector2 offset { get; set; }
            public Vector2 tiling { get; set; }
            public int instance_id { get; set; }
            public int color { get; set; }
            public uint instanceTicks { get; set; }
            public int animationID { get; set; }
            public int pathID { get; set; }
            public int extra { get; set; }
            public int propertyID { get; set; }
            public int DirtyFlags { get; set; }
            public uint pathInstanceTicks { get; set; }
            public int pad2 { get; set; }

            public int id { get { return this.propertyID; } }
            public ushort GetGroupID() { return 1; }

            public instance_properties_delta(in Vector2 offset, in Vector2 tiling, in int instance_id, in int color, in uint instanceTicks, in int animationID, in int pathID, 
                in int extra, in int propertyID, in int DirtyFlags, in uint pathInstanceTicks, in int pad2)
            {
                this.offset = offset; this.tiling = tiling; this.instance_id = instance_id; this.color = color; this.instanceTicks = instanceTicks; this.animationID = animationID; this.pathID = pathID;
                this.extra = extra; this.propertyID = propertyID; this.DirtyFlags = DirtyFlags; this.pathInstanceTicks = pathInstanceTicks; this.pad2 = pad2;
            }
        }
    }

    /// <summary>
    /// Optional info that can be created for any instance
    /// </summary>
    public struct InstanceProperties : InstanceDataProps
    {
        public Vector2 offset { get; set; }
        public Vector2 tiling { get; set; }
        public int instance_id { get; set; }
        public int color { get; set; }
        public uint instanceTicks { get; set; }
        public int animationID { get; set; }
        public int pathID { get; set; }
        public int extra { get; set; }
        public int propertyID { get; set; }
        public int DirtyFlags { get; set; }
        public uint pathInstanceTicks { get; set; }
        public int pad2 { get; set; }


        public ulong pathStartTick { get; set; } // cpu-only field
    }

    /// <summary>
    /// Bones have no properties. Empty struct
    /// </summary>
    public struct BoneProperties : InstanceDataProps
    {
        public Vector2 offset { get { throw new System.NotImplementedException(); }  set { throw new System.NotImplementedException(); } }
        public Vector2 tiling { get { throw new System.NotImplementedException(); } set { throw new System.NotImplementedException(); } }
        public int instance_id { get { throw new System.NotImplementedException(); } set { throw new System.NotImplementedException(); } }
        public int color { get { throw new System.NotImplementedException(); } set { throw new System.NotImplementedException(); } }
        public uint instanceTicks { get { throw new System.NotImplementedException(); } set { throw new System.NotImplementedException(); } }
        public int animationID { get { throw new System.NotImplementedException(); } set { throw new System.NotImplementedException(); } }
        public int pathID { get { throw new System.NotImplementedException(); } set { throw new System.NotImplementedException(); } }
        public int extra { get { throw new System.NotImplementedException(); } set { throw new System.NotImplementedException(); } }
        public int propertyID { get { throw new System.NotImplementedException(); } set { throw new System.NotImplementedException(); } }
        public int DirtyFlags { get { throw new System.NotImplementedException(); } set { throw new System.NotImplementedException(); } }
        public uint pathInstanceTicks { get { throw new System.NotImplementedException(); } set { throw new System.NotImplementedException(); } }
        public int pad2 { get { throw new System.NotImplementedException(); } set { throw new System.NotImplementedException(); } }


        public ulong pathStartTick { get { throw new System.NotImplementedException(); } set { throw new System.NotImplementedException(); } }
    }

    /// <summary>
    /// GPUInstance data for mesh instancing 
    /// </summary>
    public interface IInstanceMeshData
    {
        /// <summary>
        /// Position of the instance
        /// </summary>
        Vector3 position { get; set; }
        /// <summary>
        /// rotation of the instance
        /// </summary>
        Quaternion rotation { get; set; }
        /// <summary>
        /// instance scale
        /// </summary>
        Vector3 scale { get; set; }
        /// <summary>
        /// internal instance id. You should never have to set this.
        /// </summary>
        int id { get; set; }
        /// <summary>
        /// internal instance groupID. You should never have to set this.
        /// </summary>
        ushort groupID { get; set; }
        /// <summary>
        /// | 31-28| 27 bone anim culling (used only on gpu)| 26 runtime is culled flag (only used by gpu) | 25 Is bone flag | 24 Invisible flag |23-16 runtime lod (only used by gpu)| 15- 0 radius|
        /// </summary>
        int data1 { get; set; }
        /// <summary>
        /// ParentID for an instance. This value behaves differently depending on the
        /// function it is used with. See  MeshInstancer.Initialize(T data) and  MeshInstancer.Initialize(T[] data)
        /// -- -- this.parentID is related to InstanceData.id. It speficies that an instance with someInstance.id==this.parentID is the parent of this instance
        /// </summary>
        int parentID { get; set; }
        /// <summary>
        /// Color Highest order bytes -> low RGBA
        /// </summary>
        int props_color { get; set; }
        /// <summary>
        /// Offset
        /// </summary>
        Vector2 props_offset { get; set; }
        /// <summary>
        /// tiling
        /// </summary>
        Vector2 props_tiling { get; set; }
        /// <summary>
        /// used for bones
        /// </summary>
        int propertyID { get; set; }
        /// <summary>
        /// Bit usage: | Animation Time 31-24 | Animation Speed 23-8 | 7 Invisible Flag | 6 Block rotation | 5 block position | 4 block scale | 3 Position Interpolation flag | 2-0 unused
        /// </summary>
        int props_extra { get; set; }
        /// <summary>
        /// AnimationID
        /// </summary>
        int props_animationID { get; set; }
        /// <summary>
        /// ID of the skeleton used by this instance.
        /// </summary>
        int skeletonID { get; set; }
        uint props_instanceTicks { get; set; }
        /// <summary>
        /// Dirty flags
        /// </summary>
        int DirtyFlags { get; set; }
        /// <summary>
        /// path id
        /// </summary>
        int props_pathID { get; set; }
        /// <summary>
        /// not used
        /// </summary>
        int props_pad2 { get; set; }
        /// <summary>
        /// instance ticks for the path
        /// </summary>
        uint props_pathInstanceTicks { get; set; }
        /// <summary>
        /// Use properties for this instance?
        /// </summary>
        bool HasProperties { get; }
        /// <summary>
        /// If this instance is a bone: |31-24 bone anim lod level| 23-8 bone type| 7-0 bone index|. If not a bone, then this field is currently unused.
        /// </summary>
        int data2 { get; set; }
    }

    public interface InstanceDataProps
    {
        public Vector2 offset { get; set; }
        public Vector2 tiling { get; set; }
        public int instance_id { get; set; }
        public int color { get; set; }
        public uint instanceTicks { get; set; }
        public int animationID { get; set; }
        public int pathID { get; set; }
        public int extra { get; set; }
        public int propertyID { get; set; }
        public int DirtyFlags { get; set; }
        public uint pathInstanceTicks { get; set; }
        public int pad2 { get; set; }


        public ulong pathStartTick { get; set; } // cpu-only field
    }

    /// <summary>
    /// Used to determine which fields changed on InstanceData.
    /// </summary>
    public enum DirtyFlags
    {
        // Enum dirty flags for passing around dirty flags without having to pass around some integer

        None = 0,
        Position = 1,
        Rotation = 2,
        Scale = 4,
        GroupID = 16,
        Data1 = 32,
        ParentID = 64,
        propertyID = 4096,
        data2 = 32768,
        SkeletonID = 262144,

        props_InstanceId = 8,
        props_Color = 512,
        props_Offset = 1024,
        props_Tiling = 2048,
        props_Extra = 8192,
        props_PathID = 16384,
        props_AnimationID = 65536,
        props_pathInstanceTicks = 131072,
        props_InstanceTicks = 524288,
        props_pad2 = 1048576,

        All = -1
    }

    /// <summary>
    /// Used to determine which fields changed on InstanceData.
    /// </summary>
    public static class DirtyFlag
    {
        //Integer dirty flags incase casting DirtyFlags enum annoys you as much as it annoys me

        public const int None = (int)DirtyFlags.None;
        public const int Position = (int)DirtyFlags.Position;
        public const int Rotation = (int)DirtyFlags.Rotation;
        public const int Scale = (int)DirtyFlags.Scale;
        public const int GroupID = (int)DirtyFlags.GroupID;
        public const int Data1 = (int)DirtyFlags.Data1;
        public const int ParentID = (int)DirtyFlags.ParentID;
        public const int propertyID = (int)DirtyFlags.propertyID;
        public const int data2 = (int)DirtyFlags.data2;
        public const int SkeletonID = (int)DirtyFlags.SkeletonID;

        public const int props_InstanceId = (int)DirtyFlags.props_InstanceId;
        public const int props_Color = (int)DirtyFlags.props_Color;
        public const int props_Offset = (int)DirtyFlags.props_Offset;
        public const int props_Tiling = (int)DirtyFlags.props_Tiling;
        public const int props_Extra = (int)DirtyFlags.props_Extra;
        public const int props_PathID = (int)DirtyFlags.props_PathID;
        public const int props_AnimationID = (int)DirtyFlags.props_AnimationID;
        public const int props_pathInstanceTicks = (int)DirtyFlags.props_pathInstanceTicks;
        public const int props_InstanceTicks = (int)DirtyFlags.props_InstanceTicks;
        public const int props_pad2 = (int)DirtyFlags.props_pad2;

        public const int radius = (int)DirtyFlags.Data1; // radius is part of data1
        public const int invisible = (int)DirtyFlags.Data1; // part of data1

        public const int props_AnimationSpeed = (int)DirtyFlags.props_Extra; // part of extra. All of these fields will become dirty together- they all reside in the same 'extra' int32 field
        public const int props_IsTextureAnimation = (int)DirtyFlags.props_Extra;
        public const int props_AnimationPlayOnce = (int)DirtyFlags.props_Extra;
        public const int props_AnimationCulling = (int)DirtyFlags.props_Extra;
        public const int props_pathSpeed = (int)DirtyFlags.props_Extra;

        public const int All = (int)DirtyFlags.All;
    }

    /// <summary>
    /// Interface for setting instanceMeshData
    /// </summary>
    public interface InstanceMeshSet
    {
        void Set<T>(in T item) where T : IInstanceMeshData;
        void Delete<T>(in T item) where T : IInstanceMeshData;
    }

    struct BoneWeight
    {
        public int b0;
        public int b1;
        public int b2;
        public int b3;
        public float w0;
        public float w1;
        public float w2;
        public float w3;
        public BoneWeight(in UnityEngine.BoneWeight bw)
        {
            this.b0 = bw.boneIndex0;
            this.b1 = bw.boneIndex1;
            this.b2 = bw.boneIndex2;
            this.b3 = bw.boneIndex3;
            this.w0 = bw.weight0;
            this.w1 = bw.weight1;
            this.w2 = bw.weight2;
            this.w3 = bw.weight3;
        }
    }

    struct GPUInstanceBoneMatrix4x4
    {
        public float m00; public float m01; public float m02; public float m03;
        public float m10; public float m11; public float m12; public float m13;
        public float m20; public float m21; public float m22; public float m23;
        public float m30; public float m31; public float m32; public float m33;

        public const int kByteStride = 16 * sizeof(float);
        public const int kFloatStride = 16;
        public GPUInstanceBoneMatrix4x4(
            float m00, float m01, float m02, float m03,
            float m10, float m11, float m12, float m13,
            float m20, float m21, float m22, float m23,
            float m30, float m31, float m32, float m33)
        {
            this.m00 = m00; this.m01 = m01; this.m02 = m02; this.m03 = m03;
            this.m10 = m10; this.m11 = m11; this.m12 = m12; this.m13 = m13;
            this.m20 = m20; this.m21 = m21; this.m22 = m22; this.m23 = m23;
            this.m30 = m30; this.m31 = m31; this.m32 = m32; this.m33 = m33;
        }
        public GPUInstanceBoneMatrix4x4(in Matrix4x4 m)
        {
            this.m00 = m.m00; this.m01 = m.m01; this.m02 = m.m02; this.m03 = m.m03;
            this.m10 = m.m10; this.m11 = m.m11; this.m12 = m.m12; this.m13 = m.m13;
            this.m20 = m.m20; this.m21 = m.m21; this.m22 = m.m22; this.m23 = m.m23;
            this.m30 = m.m30; this.m31 = m.m31; this.m32 = m.m32; this.m33 = m.m33;
        }

        public static explicit operator Matrix4x4(in GPUInstanceBoneMatrix4x4 m)
        {
            var n = new Matrix4x4();
            n.m00 = m.m00; n.m01 = m.m01; n.m02 = m.m02; n.m03 = m.m03;
            n.m10 = m.m10; n.m11 = m.m11; n.m12 = m.m12; n.m13 = m.m13;
            n.m20 = m.m20; n.m21 = m.m21; n.m22 = m.m22; n.m23 = m.m23;
            n.m30 = m.m30; n.m31 = m.m31; n.m32 = m.m32; n.m33 = m.m33;
            return n;
        }
    }
}