
//instance_delta DirtyFlags bit mask layout
//1 means the field should be updates
//0 means do nothing

#define Dirty_Flag_None 0
#define Dirty_Flag_Position 1
#define Dirty_Flag_Rotation 2
#define Dirty_Flag_Scale 4
#define Dirty_Flag_GroupID 16
#define Dirty_Flag_Data1 32
#define Dirty_Flag_ParentID 64
#define Dirty_Flag_Velocity 128
#define Dirty_Flag_AngularVelocity 256
#define Dirty_Flag_PropertyID 4096
#define Dirty_Flag_Data2 32768
#define Dirty_Flag_SkeletonID 262144

#define Dirty_Flag_props_InstanceID 8
#define Dirty_Flag_props_Color 512
#define Dirty_Flag_props_Offset 1024
#define Dirty_Flag_props_Tiling 2048
#define Dirty_Flag_props_Extra 8192
#define Dirty_Flag_props_PathID 16384
#define Dirty_Flag_props_AnimationID 65536
#define Dirty_Flag_props_PathInstanceTicks 131072
#define Dirty_Flag_props_InstanceTicks 524288
#define Dirty_Flag_props_Pad2 1048576

#define Data1_Flag_Invisible 24
#define Data1_Flag_Is_Bone 25
#define Data1_Flag_Is_Culled 26
#define Data1_Flag_Bone_Anim_Cull 27
#define Data1_Flag_Bone_Anim_Disabled 28

#define Extra_Flag_Anim_Cull 4
#define Extra_Flag_Tex_Anim 2
#define Extra_Flag_Anim_PlayOnce 1

#define NUM_LODS 5


inline bool get_bit(int v, int index)
{
  return (v & (1 << index)) != 0;
}
inline int set_bit(int v, int index)
{
  return v | (1 << index);
}
inline int reset_bit(int v, int index)
{
  return v & ~(1 << index);
}


struct instance_data
{
	//position of the instance
	float3 position;
	//rotation of the instance
	float4 rotation;
	///scale of th instance
	float3 scale;
	// groupID of the instance, this refers to which material and mesh combination is being used
  // |31-16 active group id (used for dynamic lod switching) | 15-0 static group id |
	int group_ids;
	// | 31-28 Not Used| 27 bone anim culling | 26 is culled flag | 25 is bone flag | 24 invisible flag| 23 - 16 Calculated lod level |15-0 radius | 
	int data1;
	//parentID of this instance (this value is an index of another instance data to which this one will be transformed about)
	int parentID;
  // property id for instance
	int propertyID;
  //index pointing to a skeleton in the vertex skeleton map array
  int skeletonID;

  // Bone: |bone anim lod level|23-8 bone type|7-0 bone index|
  int data2;

	//extra notes*
	//extra.31 - 24, Animation Speed, how fast animation will play. range 0...25.5. Stored as bytes from 0...255. Convert to speed by dividing by 10.
  //extra.4 animation culling- should bones/animations be culled based on lod level?
  //extra.2 is texture anim- is this a texture animation? or a bone animation?
  //extra.1 anim play once- should animation play once then stop?

  //Data1.26 is culled- Is this instance culled? (ie, not in camera frustum)
  //Data1.25 is bone flag- is this instancw a 'bone'?
  //Data1.24- invisible- is this instance invisible? If so it wont be rendered
  //Data.23-16- what is the lod level (0...4) of the instance (calculated based on distance to camera)
  //Data.15-0- what is the radius of the instance

  // propertyID (when it is used for bone data, rather than identifying property structs)
  // bone lod level: ie, lowest lod quality bone will animate at (0= will only animate on highest detail)
  // bone type- group id for skinned mesh the bone belongs to
  // bone index- index in full skeleton of the bone
};

struct instance_delta
{
	float3 position;
	float4 rotation;
	float3 scale;
	int index;
	int group_ids;
  int data1;
	int parentID;
	int propertyID;
  int skeletonID;
	int DirtyFlags;
  int data2;
};

struct instance_properties
{
  // (optional) texture offset
  float2 offset;
  // (optional) texture tiling
  float2 tiling;
  // id of instance for this property
  int instance_id;
  // (optional) color of instance
  int color;
  // (optional- if animatiom not used) instance ticks timer for animation
  uint instanceTicks;
  // id of animation
  int animationID;
  // id of path
  int pathID;
  // Bit usage : | Animation Speed 31 - 24 | Path Speed 23 - 8 | 7 - 5 Not Used | 4 Animation Culling | 3 Not used | 2 is Texture Animation | 1 Animation Play Once | 0 Not used
  int extra;
  // (optional- if path not used) instance tick timer used for path
  uint pathInstanceTicks;
  // not used
  int pad2;
};

struct instance_properties_delta
{
  float2 offset;
  float2 tiling;
  int instance_id;
  int color;
  uint instanceTicks;
  int animationID;
  int pathID;
  int extra;
  int propertyID;
  int DirtyFlags;
  uint pathInstanceTicks;
  int pad2;
};

struct hierarchy_delta
{
  // index_data index
  int index;
  // index_data depth
  int depth;
  // instance data id
  int instance_id;
  // if this delta instance is dirty
  int dirty;
};

struct indirect_id_delta
{
  // instance data id
  int id;
  // index_data index
  int index;
};

inline uint get_path_speed(int extra)
{
  int val = extra & 16776960; //remove all but middle bytes
  val = val >> 8; //shift middle bytes right one byte
  return (uint)val; //return value * 0.1f (speed is stored multiplied by 10 and has increments of 0.1..0.2..0.3..etc)
}

inline uint get_animation_speed(int extra)
{
  // animation speed multipliers range from 0...25.5. You must divide by '10' to turn this byte into the floating point speed.
  return (uint)((extra >> 24) & 255); //get left most byte (some shifters carry over the sign)
}

inline int get_bone_index2(int data2)
{
  return data2 & 255;
}

inline int get_bone_type2(int data2)
{
  int val = data2 & 16776960; //remove all but middle bytes
  val = val >> 8; //shift middle bytes right one byte 
  return val; //return value
}

inline int get_bone_lod2(int data2)
{
  return (data2 >> 24) & 255;
}

inline int get_static_group_id(int group_ids)
{
  return group_ids & 65535;
}

inline int get_dynamic_group_id(int group_ids)
{
  return (group_ids >> 16) & 65535;
}

inline int set_dynamic_group_id(int group_ids, int new_dyn_id)
{
  group_ids = group_ids & 65535; // clear old dynamic id
  group_ids = group_ids | (new_dyn_id << 16); // set new dynamic id
  return group_ids;
}

inline int o2w_index(int instance_id)
{
  // The matrices are stored |Obj2World|World2Obj| for each instance (so this buffer will have 2*N number of matrices)
  return instance_id * 2;
}
inline int w2o_index(int instance_id)
{
  // The matrices are stored |Obj2World|World2Obj| for each instance (so this buffer will have 2*N number of matrices)
  return instance_id * 2 + 1;
}

// Pack integer into a float2 (lossless)
inline float2 PackInt(uint val)
{
  uint xi = val / 100000;
  return float2((float)xi, (float)(val % 100000));
}
// Unpack integer from float2 (lossless)
inline uint UnPackInt(float2 val)
{
  return ((uint)val.x) * 100000 + (uint)val.y;
}

inline int group_lod_index(int group_id)
{
  return group_id * NUM_LODS * 2;
}

inline float lod_radius_ratio(uint data)
{
  return ((float)data) * 0.00001; // divide by 100000
}

inline float get_radius(int data1)
{
  return ((float)(data1 & 65535)) * 0.05;
}

inline int get_lod(int data1)
{
  return (data1 >> 16) & 255;
}

inline int set_lod(int data1, int lod)
{
  data1 = data1 & ~(255 << 16); // set lod byte to zero (ie bits 23-16)
  data1 = data1 | (lod << 16); // set lod
  return data1;
}

inline instance_data update_instance_data(instance_data d, instance_delta delta)
{
  int update_flag = delta.DirtyFlags & Dirty_Flag_Position;
  d.position = update_flag > 0 ? delta.position : d.position;

  update_flag = delta.DirtyFlags & Dirty_Flag_Rotation;
  d.rotation = update_flag > 0 ? delta.rotation : d.rotation;

  update_flag = delta.DirtyFlags & Dirty_Flag_Scale;
  d.scale = update_flag > 0 ? delta.scale : d.scale;

  update_flag = delta.DirtyFlags & Dirty_Flag_GroupID;
  d.group_ids = update_flag > 0 ? delta.group_ids : d.group_ids;
  d.group_ids = set_dynamic_group_id(d.group_ids, 0);   //reset dyanmic_group_id on any change

  update_flag = delta.DirtyFlags & Dirty_Flag_Data1;
  d.data1 = update_flag > 0 ? delta.data1 : d.data1;
  d.data1 = reset_bit(d.data1, Data1_Flag_Is_Culled); // reset culled state on any change
  d.data1 = set_lod(d.data1, 0);  // reset lod on any change

  update_flag = delta.DirtyFlags & Dirty_Flag_ParentID;
  d.parentID = update_flag > 0 ? delta.parentID : d.parentID;

  update_flag = delta.DirtyFlags & Dirty_Flag_PropertyID;
  d.propertyID = update_flag > 0 ? delta.propertyID : d.propertyID;

  update_flag = delta.DirtyFlags & Dirty_Flag_SkeletonID;
  d.skeletonID = update_flag > 0 ? delta.skeletonID : d.skeletonID;

  update_flag = delta.DirtyFlags & Dirty_Flag_Data2;
  d.data2 = update_flag > 0 ? delta.data2 : d.data2;

  return d;
}

inline instance_delta instance_delta_zero()
{
  instance_delta d;
  d.position = float3(0, 0, 0);
  d.rotation = float4(0, 0, 0, 1);
  d.scale = float3(1, 1, 1);
  d.group_ids = 0;
  d.data1 = 0;
  d.parentID = 0;
  d.propertyID = 0;
  d.skeletonID = 0;
  d.DirtyFlags = 0;
  d.index = 0;
  d.data2 = 0;
  return d;
}


inline instance_properties update_instance_properties(instance_properties d, instance_properties_delta delta)
{
  int update_flag = delta.DirtyFlags & Dirty_Flag_props_Color;
  d.color = update_flag > 0 ? delta.color : d.color;

  update_flag = delta.DirtyFlags & Dirty_Flag_props_Offset;
  d.offset = update_flag > 0 ? delta.offset : d.offset;

  update_flag = delta.DirtyFlags & Dirty_Flag_props_Tiling;
  d.tiling = update_flag > 0 ? delta.tiling : d.tiling;

  update_flag = delta.DirtyFlags & Dirty_Flag_props_Extra;
  d.extra = update_flag > 0 ? delta.extra : d.extra; // assign new values

  update_flag = delta.DirtyFlags & Dirty_Flag_props_AnimationID;
  d.animationID = update_flag > 0 ? delta.animationID : d.animationID;

  update_flag = delta.DirtyFlags & Dirty_Flag_props_InstanceTicks;
  d.instanceTicks = update_flag > 0 ? delta.instanceTicks : d.instanceTicks;

  update_flag = delta.DirtyFlags & Dirty_Flag_props_InstanceID;
  d.instance_id = update_flag > 0 ? delta.instance_id : d.instance_id;

  update_flag = delta.DirtyFlags & Dirty_Flag_props_PathID;
  d.pathID = update_flag > 0 ? delta.pathID : d.pathID;

  update_flag = delta.DirtyFlags & Dirty_Flag_props_PathInstanceTicks;
  d.pathInstanceTicks = update_flag > 0 ? delta.pathInstanceTicks : d.pathInstanceTicks;

  update_flag = delta.DirtyFlags & Dirty_Flag_props_Pad2;
  d.pad2 = update_flag > 0 ? delta.pad2 : d.pad2;

  return d;
}

inline instance_properties_delta instance_properties_delta_zero()
{
  instance_properties_delta d;
  d.offset = float2(0, 0);
  d.tiling = float2(0, 0);
  d.instance_id = 0;
  d.color = 0;
  d.instanceTicks = 0;
  d.animationID = 0;
  d.pathID = 0;
  d.extra = 0;
  d.propertyID = 0;
  d.DirtyFlags = 0;
  d.pathInstanceTicks = 0;
  d.pad2 = 0;
  return d;
}
