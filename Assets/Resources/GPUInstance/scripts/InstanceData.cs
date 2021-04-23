using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GPUInstance
{
    /// <summary>
    /// Interface for creating, modifying, and deleting individual GPU instances.
    /// </summary>
    public struct InstanceData<Props> : IInstanceMeshData
        where Props : InstanceDataProps
    {
        /// <summary>
        /// Position of the instance
        /// </summary>
        public Vector3 position { get; set; }
        /// <summary>
        /// rotation of the instance
        /// </summary>
        public Quaternion rotation { get; set; }
        /// <summary>
        /// instance scale
        /// </summary>
        public Vector3 scale { get; set; }
        /// <summary>
        /// internal instance id. You should never have to set this.
        /// </summary>
        public int id { get; set; }
        /// <summary>
        /// internal instance groupID. You should never have to set this.
        /// </summary>
        public ushort groupID { get; set; }
        /// <summary>
        /// | 31-29 | 28 Disable Bone Animation | 27 bone anim culling (used only on gpu)| 26 runtime is culled flag (only used by gpu) | 25 Is bone flag | 24 Invisible flag |23-16 runtime lod (only used by gpu)| 15- 0 radius|
        /// </summary>
        public int data1 { get; set; }
        /// <summary>
        /// ParentID for an instance. This value behaves differently depending on the
        /// function it is used with. See  MeshInstancer.Initialize(T data) and  MeshInstancer.Initialize(T[] data)
        /// -- -- this.parentID is related to InstanceData.id. It speficies that an instance with someInstance.id==this.parentID is the parent of this instance
        /// </summary>
        public int parentID { get; set; }
        /// <summary>
        /// propertyID for this instance.
        /// </summary>
        public int propertyID { get; set; }
        /// <summary>
        /// Skeleton ID for this instance.
        /// </summary>
        public int skeletonID { get; set; }
        /// <summary>
        /// If this instance is a bone: |31-24 bone anim lod level| 23-8 bone type| 7-0 bone index|. If not a bone, then this field is currently unused.
        /// </summary>
        public int data2 { get; set; }
        /// <summary>
        /// This field functions as a bitmask. See GPUInstance.DirtyFlags. Setting to -1 will update all. Setting to 0 will update none.
        /// </summary>
        public int DirtyFlags { get; set; }

        /// <summary>
        /// Internal flag for safety reasons. Has this object been initialized with MeshInstancer.Initialize?
        /// You should never have to set this.
        /// </summary>
        public bool gpu_initialized { get; set; }
        /// <summary>
        /// The MeshType associated with this instance
        /// </summary>
        public MeshType mesh_type
        {
            get
            {
                return _type;
            }
            set
            {
                _type = value;
                groupID = value != null ? value.groupID : (ushort)instancemesh.NULL_ID;
            }
        }
        MeshType _type;
        /// <summary>
        /// Is the instance invisible? If Invisible=True, then mesh_type=null. If Invisible=False, then mesh_type!=null.
        /// Any other combination will be invalid and cause erroneous behaviour
        /// </summary>
        public bool Invisible
        {
            get
            {
                return Bits.GetBit(this.data1, 24);
            }
            set
            {
                this.data1 = Bits.SetBit(this.data1, 24, value);
            }
        }
        /// <summary>
        /// is this instance a bone for a skinned mesh?
        /// </summary>
        public bool IsBone
        {
            get
            {
                return Bits.GetBit(this.data1, 25);
            }
            set
            {
                this.data1 = Bits.SetBit(this.data1, 25, value);
            }
        }
        /// <summary>
        /// Get/Set bone index. This field should only be used by instances that are bones.
        /// </summary>
        public int BoneIndex
        {
            get
            {
                return this.data2 & 255;
            }
            set
            {
                if (value > 255)
                    throw new System.Exception("Error, input bone index is too big");
                this.data2 = value & 255;
            }
        }
        /// <summary>
        /// Get/Set bone type. The 'type' is the Meshtype (aka groupID) of the skinned mesh the bone is associated with.  This field should only be used by instances that are bones.
        /// </summary>
        public int BoneType
        {
            get
            {
                var val = this.data2 & 16776960; //remove all but middle bytes
                val = val >> 8; //shift middle bytes right one byte
                return val; //return value
            }
            set
            {
                if (value < 0 || value > ushort.MaxValue) throw new System.Exception("Error, bad value");
                this.data2 = this.data2 & ~16776960; //set byte1 and 2 to 0
                this.data2 = this.data2 | (value << 8); //insert value
            }
        }
        /// <summary>
        /// Lowest quality LOD level the bone can animate at. Eg, =0, bone will only animate at highest detail LOD.  This field should only be used by instances that are bones.
        /// </summary>
        public int BoneLOD
        {
            get
            {
                return (this.data2 >> 24) & 255;
            }
            set
            {
                if (value < 0 || value >= instancemesh.NumLODLevels)
                    throw new System.Exception("Error, invalid lod level specified");
                this.data2 = this.data2 & ~(255 << 24); //set left most byte to 0
                this.data2 = this.data2 | (value << 24);
            }
        }
        /// <summary>
        /// Set Radius
        /// </summary>
        public float radius
        {
            get
            {
                return ((float)(data1 & 65535)) * 0.05f;
            }
            set
            {
                if (value > 3275) throw new System.Exception("Error, input radius is too large");
                if (value < 0) throw new System.Exception("Error, negative radius not allowed");
                this.data1 = this.data1 & ~65535;
                this.data1 = this.data1 | (ushort)(value * 20.0f);
            }
        }
        /// <summary>
        /// Are bone animations disabled? -Only really applicable to bones. Can be used to manually pose specific bones
        /// </summary>
        public bool DisabledBoneAnimation
        {
            get
            {
                return Bits.GetBit(this.data1, 28);
            }
            set
            {
                this.data1 = Bits.SetBit(this.data1, 28, value);
            }
        }


        /// <summary>
        /// Does this instance have properties?
        /// </summary>
        public bool HasProperties { get; private set; }
        /// <summary>
        /// properties for instance data
        /// </summary>
        private Props props;
        /// <summary>
        /// Bit usage: | Animation Speed 31-24 | Path Speed 23-8 | 7-5 Not Used | 4 Animation Culling | 3 Not used | 2 is Texture Animation | 1 Animation Play Once | 0 Not used
        /// </summary>
        public int props_extra { get { return this.props.extra; } set { this.props.extra = value; } }
        /// <summary>
        /// AnimationID
        /// </summary>
        public int props_animationID { get { return this.props.animationID; } set { this.props.animationID = value; } }
        /// <summary>
        /// Color Highest order bytes -> low RGBA
        /// </summary>
        public int props_color { get { return this.props.color; } set { this.props.color = value; } }
        /// <summary>
        /// Offset
        /// </summary>
        public Vector2 props_offset { get { return this.props.offset; } set { this.props.offset = value; } }
        /// <summary>
        /// tiling
        /// </summary>
        public Vector2 props_tiling { get { return this.props.tiling; } set { this.props.tiling = value; } }
        /// <summary>
        /// pathID for this instance. Get/set pathID.
        /// </summary>
        public int props_pathID
        {
            get
            {
                return this.props.pathID;
            }
            set
            {
                // use this.SetPath
                throw new System.NotImplementedException(); 
            }
        }
        /// <summary>
        /// ticks for path time
        /// </summary>
        public uint props_pathInstanceTicks { get { return this.props.pathInstanceTicks; } set { throw new System.NotImplementedException(); } }
        /// <summary>
        /// timer for instance
        /// </summary>
        public uint props_instanceTicks { get { return this.props.instanceTicks; } set { this.props.instanceTicks = value; } }
        /// <summary>
        /// not used
        /// </summary>
        public int props_pad2 { get { return this.props.pad2; } set { this.props.pad2 = value; } }

        /// <summary>
        /// Animation play speed Range[0.0...25.5] in 0.1 increments
        /// </summary>
        public float props_AnimationSpeed
        {
            get
            {
                int val = (this.props_extra >> 24) & 255; //get left most byte (some shifters carry over the sign)
                return val * 0.1f; //multiply by 1/10- Used in increments of '10' for synchronization purposes
            }
            set
            {
                this.props_extra = this.props_extra & ~(255 << 24); //set left most byte to 0
                int val = ((int)(value * 10)) << 24; //get value to set
                this.props_extra = this.props_extra | val; //set

            }
        }
        /// <summary>
        /// raw animation speed.. A number from 0...255. A speed multiplier of '2.7' is 27. Speed multiplier of '3.0'=30, etc..
        /// </summary>
        public uint props_AnimationSpeedRaw
        {
            get
            {
                return (uint)((this.props_extra >> 24) & 255);
            }
        }
        /// <summary>
        /// Current time of the animation
        /// </summary>
        public float props_AnimationSeconds
        {
            get
            {
                return this.props_instanceTicks * 0.0001f;
            }
            set
            {
                this.props_instanceTicks = (uint)(value * 10000);
            }
        }
        /// <summary>
        /// if true, a texture animation can be used, rather than a bone animation
        /// </summary>
        public bool props_IsTextureAnimation
        {
            get
            {
                return Bits.GetBit(this.props_extra, 2);
            }
            set
            {
                this.props_extra = Bits.SetBit(this.props_extra, 2, value);
            }
        }
        /// <summary>
        /// if true, animation will stop once it reaches the end
        /// </summary>
        public bool props_AnimationPlayOnce
        {
            get
            {
                return Bits.GetBit(this.props_extra, 1);
            }
            set
            {
                this.props_extra = Bits.SetBit(this.props_extra, 1, value);
            }
        }
        /// <summary>
        /// true= Animation calculation will stop when the instance is not in the camera frustum.
        /// </summary>
        public bool props_AnimationCulling
        {
            get
            {
                return Bits.GetBit(this.props_extra, 4);
            }
            set
            {
                this.props_extra = Bits.SetBit(this.props_extra, 4, value);
            }
        }
        /// <summary>
        /// color ofthe instance
        /// </summary>
        public Color32 props_color32
        {
            get
            {
                return new Color32((byte)(this.props_color >> 24), (byte)(this.props_color >> 16), (byte)(this.props_color >> 8), (byte)(this.props_color));
            }
            set
            {
                this.props_color = (value.r << 24) | (value.g << 16) | (value.b << 8) | value.a;
            }
        }
        /// <summary>
        /// Tick when path was set
        /// </summary>
        public ulong props_pathStartTick { get { return this.props.pathStartTick; } set { throw new System.NotImplementedException(); } }
        /// <summary>
        /// Speed traversing path. Has range (0...1000) and only has 0.1 precision (ie 0.1..0.2..0.3..etc)
        /// </summary>
        public float props_pathSpeed
        {
            get
            {
                var val = this.props.extra & 16776960; //remove all but middle bytes
                val = val >> 8; //shift middle bytes right one byte
                return val * 0.1f; //return value * 0.001f
            }
            set
            {
                if (value < 0 || value > 1000)
                    throw new System.Exception("Error, speed must be betweem 0 and 1000!");
                var val = (int)(value * 10); //multiply by 10
                val = val & 65535; //clamp to have a max of 65535
                this.props.extra = this.props.extra & ~16776960; //set byte1 and 2 to 0
                this.props.extra = this.props.extra | (val << 8); //insert value
            }
        }
        public uint props_pathSpeedRaw
        {
            get
            {
                var val = this.props.extra & 16776960; //remove all but middle bytes
                val = val >> 8; //shift middle bytes right one byte
                return (uint)val; //return value * 0.001f
            }
        }
        /// <summary>
        /// Set the path for this instance.
        /// </summary>
        /// <param name="p">path</param>
        /// <param name="m">mesh instancer</param>
        /// <param name="start_time"> start time (seconds) to start at in the path. </param>
        public void SetPath(in Path p, MeshInstancer m, float speed = 1.0f, in float start_time = 0)
        {
            if (!p.use_constants)
            {
                if (p.PathCompletionTime < 0.0001f)
                    throw new System.Exception("Error, path completion time must be positive, non-zero number");
                if (start_time < 0 || start_time >= p.PathCompletionTime)
                    throw new System.Exception("Error, invalid start time");
            }

            this.props.pathID = p.path_id;
            this.props.pathStartTick = m.Ticks;
            this.props_pathSpeed = speed;
            this.props.pathInstanceTicks = p.use_constants ? 0 : (uint)((start_time / p.PathCompletionTime) * Ticks.TicksPerSecond);
            this.DirtyFlags = this.DirtyFlags | DirtyFlag.props_pathInstanceTicks | DirtyFlag.props_PathID | DirtyFlag.props_Extra;
        }

        /// <summary>
        /// This constructor returns an Instance data that is a white cube and has zeroes transforms.
        /// </summary>
        /// <param name="DEFAULT"> This value is ignored. </param>
        public InstanceData(in MeshType type, bool has_props=true)
        {
            this.position = default(Vector3);
            this.rotation = new Quaternion(0, 0, 0, 1);
            this.scale = new Vector3(1, 1, 1);
            this.id = 0;
            this.groupID = type.groupID;
            this.data1 = 20; // setting to '20' causes radius '1' to be used
            this.data2 = 0;
            this.parentID = -1;
            this.propertyID = 0;
            this.skeletonID = 0;
            this.gpu_initialized = false;
            this._type = type;
            this.DirtyFlags = GPUInstance.DirtyFlag.All;

            this.HasProperties = has_props; // default to using properties (ie, some of the fields in here are only useable if properties==true)
            this.props = default(Props);

            if (this.HasProperties)
            {
                this.props_instanceTicks = 0;
                this.props_color = -1; //-1 = 11111111 11111111 11111111 11111111 or new Color(255,255,255,255)
                this.props_offset = default(Vector2);
                this.props_tiling = new Vector2(1, 1);
                this.props_animationID = 0;
                this.props_extra = 0;
                this.props_AnimationSpeed = 1.0f;
            }
        }
    }
}
