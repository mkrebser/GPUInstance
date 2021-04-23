using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace GPUInstance
{
    public interface InstanceMeshDelta
    {
        public int DirtyFlags { get; set; }
        public int id { get; }
        public ushort GetGroupID();
    }
    /// <summary>
    /// Handles delta buffer for instances
    /// </summary>
    public class InstanceMeshDeltaBuffer<T> where T : struct, InstanceMeshDelta
    {
        // This class handles the Instance Delta compute buffer
        // Why use 'delta' buffers? -So we can update a small amount of the existing instances without having to ship an entire new instance buffer to the GPU every frame! yikes!
        // After the delta buffer is set in the GPU, a compute shader will only update instances that have changes in the GPU instance buffer

        const int kInitialFrameCount = 1;

        private SimpleList<T> _delta_buffer;
        private SimpleList<DeltaIndex> _instance_delta_indices;
        private uint[] _group_instance_count = null;
        private bool _track_group_counts = false;
        private object lock_object = new object();
        private long _frame_count = kInitialFrameCount;
        private int _delta_offset = 0; // used to track where in the buffer updates still need to be pushed to the GPU
        /// <summary>
        /// maximum delta buffer size
        /// </summary>
        public int MaxDeltaCount { get; private set; }
        /// <summary>
        /// number of instances that are in the delta buffer
        /// </summary>
        public int CurrentDeltaCount { get { return System.Math.Min(this._delta_buffer.Count - this._delta_offset, this.MaxDeltaCount); } }
        public int CurrentIndirectIDDeltaCount { get { return this.IndirectIDBuffer.CurrentDeltaCount; } }
        public int IndirectBufferInstanceCount { get { return this.IndirectIDBuffer.InstanceCount; } }
        public int MaxInstanceID { get; private set; }
        /// <summary>
        /// Num instances of particular group
        /// </summary>
        /// <param name="groupID"></param>
        /// <returns></returns>
        public uint NumInstancesForGroup(ushort groupID)
        {
            return this._group_instance_count[groupID];
        }

        /// <summary>
        /// Indirect ID Buffer (It is stored in the delta buffer rather than standalone so that it will be automatically updated when applying changes to the delta buffer) (also to reduce number of lock aquisitions for updates)
        /// </summary>
        private InstanceMeshIndirectIDBuffer IndirectIDBuffer;

        public InstanceMeshDeltaBuffer(int InitialMaxInstanceCount, int MaxDeltaCount, int MaxMeshTypes, bool TrackGroupCounts)
        {
            if (!ReferenceEquals(null, _delta_buffer))
                throw new System.Exception("Error, already initialized!");

            this.MaxDeltaCount = MaxDeltaCount;
            this._delta_buffer = new SimpleList<T>(MaxDeltaCount);
            this._instance_delta_indices = new SimpleList<DeltaIndex>(MaxDeltaCount);
            this.MaxInstanceID = InitialMaxInstanceCount; // Initial buffer size

            this.IndirectIDBuffer = new InstanceMeshIndirectIDBuffer(InitialMaxInstanceCount, MaxDeltaCount);

            if (TrackGroupCounts)
                this._group_instance_count = new uint[MaxMeshTypes];
            this._track_group_counts = TrackGroupCounts;
        }

        void try_update_size(SimpleList<DeltaIndex> l, int new_size)
        {
            if (new_size > 100000000)
                throw new System.Exception("Delta Buffer size too large! 100 million is too many"); // sanity check
            if (new_size >= this._instance_delta_indices.Array.Length)
                this._instance_delta_indices.Resize(new_size * 2);
        }

        public void UpdateInstance(in T delta, in bool instance_deleted, in ushort deleted_instance_group_id = instancemesh.NULL_ID)
        {
            if (delta.id == instancemesh.NULL_ID || delta.id < 0)
                throw new System.Exception("Error, tried to update with invalid id! All instances must have a non NULL positive id.");

            int delta_index = -1;

            try_update_size(this._instance_delta_indices, delta.id); // try to update instance index buffer length
            this.MaxInstanceID = System.Math.Max(delta.id, this.MaxInstanceID); // try to set new max instance id

            if (this._instance_delta_indices.Array[delta.id]._frame == this._frame_count) // check if the instance has already been written in this frame...
            { // if it has, just overwrite
                delta_index = this._instance_delta_indices.Array[delta.id]._index;
                var dirty_flags = this._delta_buffer.Array[delta_index].DirtyFlags | delta.DirtyFlags;
                ushort old_group_id = this._delta_buffer.Array[delta_index].GetGroupID(); // get old id
                this._delta_buffer.Array[delta_index] = delta;
                this._delta_buffer.Array[delta_index].DirtyFlags = dirty_flags;

                // update group count tracking
                if (this._track_group_counts)
                {
                    if (old_group_id != instancemesh.NULL_ID) this._group_instance_count[old_group_id]--; // decrement groupID instance count for old_id
                    if (this._group_instance_count[old_group_id] < 0)
                        throw new System.Exception("Error, deleted more instances of mesh type: " + old_group_id.ToString() + " than actually exist! Make sure you are deleting the intended type!");
                }
            }
            else // if it has not been written this frame...
            { // append it to the delta buffer
                delta_index = this._delta_buffer.Count;
                this._instance_delta_indices.Array[delta.id] = new DeltaIndex(this._frame_count, delta_index);
                this._delta_buffer.Add(delta);
            }

            // update group count tracking
            if (this._track_group_counts)
            {
                if (instance_deleted) // decrement group id if deleted
                {
                    if (deleted_instance_group_id == instancemesh.NULL_ID)
                        throw new System.Exception("Error, type to be deleted is null!");
                    this._group_instance_count[deleted_instance_group_id]--;
                    if (this._group_instance_count[deleted_instance_group_id] < 0)
                        throw new System.Exception("Error, deleted more instances of mesh type: " + deleted_instance_group_id.ToString() + " than actually exist! Make sure you are deleting the intended type!");
                }
                else
                {
                    this._group_instance_count[delta.GetGroupID()]++; // increment group id count
                }
            }

            // Update indirect id buffer
            if (!instance_deleted) this.IndirectIDBuffer.TryAddInstanceID(delta.id, this._frame_count);
            else this.IndirectIDBuffer.RemoveInstanceID(delta.id, this._frame_count);
        }

        /// <summary>
        /// [Main Thread]
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        public int UpdateComputeBuffer(ComputeBuffer buffer, UnityEngine.Rendering.CommandBuffer cmd)
        {
            var delta_count = this.CurrentDeltaCount;

            if (delta_count <= 0) // do nothing if no changes
            {
                this._frame_count++;
                return 0;
            }

            // Set buffer
            cmd.SetBufferData(buffer, this._delta_buffer.Array, this._delta_offset, 0, delta_count);// buffer.SetData(this._delta_buffer.Array, 0, 0, delta_count);

            // Get true delta count
            var true_delta_count = this._delta_buffer.Count - this._delta_offset;

            if (true_delta_count <= MaxDeltaCount) // if delta array is less than max, just clear it
            {
                this._delta_buffer.SetCount(0); // set count to 0- this will leave garbage on the buffer
                this._delta_offset = 0; // reset offset back to zero
            }
            else // otherwise all items should be copied forward
            {
                this._delta_offset += MaxDeltaCount; // more instances are left on the buffer past the max delta count...

            }

            if (this._delta_buffer.Count == 0)
            {
                this._frame_count++;
            }

            return delta_count;
        }

        /// <summary>
        /// [Main Thread] Update indirect id buffer
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        public int UpdateIndirectIDComputeBuffer(ComputeBuffer buffer, UnityEngine.Rendering.CommandBuffer cmd)
        {
            return this.IndirectIDBuffer.UpdateComputeBuffer(buffer, cmd);
        }

        /// <summary>
        /// dispose
        /// </summary>
        public void Dispose()
        {
            this._delta_buffer = null;
            this._instance_delta_indices = null;
            this._group_instance_count = null;
            this._frame_count = kInitialFrameCount;
            this._delta_offset = 0;
            this._track_group_counts = false;
            if (this.IndirectIDBuffer != null)
                this.IndirectIDBuffer.Dispose();
            this.IndirectIDBuffer = null;
        }

        struct DeltaIndex
        {
            public long _frame;
            public int _index;

            public DeltaIndex(in long frame, in int index)
            {
                this._frame = frame;
                this._index = index;
            }
        }

        /// <summary>
        /// Indirect ID buffer manager class. 
        /// </summary>
        internal class InstanceMeshIndirectIDBuffer
        {
            // IndirectID buffer. Why use this?
            // This buffer holds all curently existing mesh instance ids (contiguously/sequentially) in a compute buffer.
            // This way- A compute shader can be dispatched for only the existing instance ids- by executing on the indrect buffer to lookup real instance ids
            // If we did not have the indirect buffer, then a compute shader would need to be dispatched every frame for every possible instance id- Not great for performance when the instance buffer is sparsly populated!

            // A contiguous block of ids is maintained (similar to how the hierarchy buffer does it)
            // When a new instance id is allocated, it is appended to the IndirectIDBuffer
            // When an instance id is deleted, it is removed by swapping with the end of the indirectbuffer (to maintain ids in a contiguous block)

            private enum DeltaAction
            {
                None,
                Add,
                Remove
            }
            internal struct IndirectIDDelta
            {
                public int instance_id; // id to set in the indirect id buffer
                public int indirect_index; // index in the indirect id buffer to set at

                public IndirectIDDelta(in int id, in int index)
                {
                    this.instance_id = id;
                    this.indirect_index = index;
                }
            }
            private struct IndirectIDData
            {
                public long _frame;
                public int instance_id;
                public int _delta_index;

                public bool Initialized { get { return instance_id != instancemesh.NULL_ID; } }

                public IndirectIDData(in int id, in long frame, in int delta_index)
                {
                    this.instance_id = id;
                    this._frame = frame;
                    this._delta_index = delta_index;
                }
            }
            private struct IndirectIDIndex
            {
                public int _index;
                public bool _init;

                public IndirectIDIndex(in int index)
                {
                    this._index = index;
                    this._init = true;
                }
            }

            private SimpleList<IndirectIDData> _indirect_id_buffer;
            private SimpleList<IndirectIDIndex> _indirect_id_map;
            private SimpleList<IndirectIDDelta> _delta_buffer;
            private int _delta_offset = 0;
            private readonly int MaxDeltaCount;

            public int CurrentDeltaCount { get { return System.Math.Min(this.MaxDeltaCount, this._delta_buffer.Count - this._delta_offset); } }

            public int InstanceCount
            {
                get
                {
                    return this._indirect_id_buffer.Count;
                }
            }

            public InstanceMeshIndirectIDBuffer(int InitialMaxInstanceCount, int MaxDeltaCount)
            {
                this._delta_buffer = new SimpleList<IndirectIDDelta>(MaxDeltaCount);
                this.MaxDeltaCount = MaxDeltaCount;
                this._indirect_id_buffer = new SimpleList<IndirectIDData>(InitialMaxInstanceCount);
                this._indirect_id_map = new SimpleList<IndirectIDIndex>(InitialMaxInstanceCount);
            }

            void try_resize_map(in int id)
            {
                if (id >= this._indirect_id_map.Array.Length)
                    this._indirect_id_map.Resize(id * 2 + 1);
            }

            public void TryAddInstanceID(in int id, in long frame)
            {
                if (id == instancemesh.NULL_ID || id < 0 || id == instancemesh.INVALID_ID)
                    throw new System.Exception("Error, trying to add invalid id from indirect id");
                if (frame <= 0)
                    throw new System.Exception("Error, invalid frame used for add indirect id");

                try_resize_map(id);
                var index = this._indirect_id_map.Array[id];

                if (index._init) // if already exists...
                {
                    var data = this._indirect_id_buffer.Array[index._index];

                    if (data.instance_id != id)
                        throw new System.Exception("Error, incorrect id found in indirectID add id");

                    if (data._frame == frame) // only need to overwrite if update occurs in the same frame
                    {
                        if (this._delta_buffer.Array[data._delta_index].indirect_index != index._index)
                            throw new System.Exception("Error, overwritten index should be equal in try add instance id indirect id");
                        this._delta_buffer.Array[data._delta_index] = new IndirectIDDelta(id, index._index);
                    }
                }
                else
                { // if it doesn't exist... Then just add it...
                    var new_index = this._indirect_id_buffer.Count;
                    _indirect_id_map.Array[id] = new IndirectIDIndex(new_index); // add to map

                    bool in_bounds = this._indirect_id_buffer.Array.Length > new_index;

                    if (in_bounds && this._indirect_id_buffer.Array[new_index].instance_id != instancemesh.NULL_ID)
                        throw new System.Exception("Error, trying to overwrite existing value in add indirect id");

                    // Check if the index may have already been updated this frame...
                    if (in_bounds && this._indirect_id_buffer.Array[new_index]._frame == frame)
                    {
                        var delta_index = this._indirect_id_buffer.Array[new_index]._delta_index;
                        var indirect_id_data = new IndirectIDData(id, frame, delta_index);
                        this._indirect_id_buffer.Add(indirect_id_data);
                        this._delta_buffer.Array[delta_index] = new IndirectIDDelta(indirect_id_data.instance_id, new_index);
                    }
                    else
                    {
                        var indirect_id_data = new IndirectIDData(id, frame, this._delta_buffer.Count);
                        this._indirect_id_buffer.Add(indirect_id_data);
                        this._delta_buffer.Add(new IndirectIDDelta(indirect_id_data.instance_id, new_index));
                    }
                }
            }

            public void RemoveInstanceID(in int id, in long frame)
            {
                if (frame <= 0)
                    throw new System.Exception("Error, invalid frame used for remove indirect id");

                void update_delta_buffer(in IndirectIDDelta delta, in long f)
                {
                    // Update end_delta index
                    var data = this._indirect_id_buffer.Array[delta.indirect_index];
                    if (data._frame == f)
                    {
                        if (this._delta_buffer.Array[data._delta_index].indirect_index != delta.indirect_index)
                            throw new System.Exception("Error, corrupted delta index in remove instance id indirect id");
                        this._delta_buffer.Array[data._delta_index] = delta;
                    }
                    else
                    {
                        this._delta_buffer.Add(delta);
                        this._indirect_id_buffer.Array[delta.indirect_index] = new IndirectIDData(data.instance_id, f, this._delta_buffer.Count - 1);
                    }
                }

                if (id == instancemesh.NULL_ID || id < 0 || id == instancemesh.INVALID_ID)
                    throw new System.Exception("Error, trying to remove invalid id from indirect id");

                try_resize_map(id);

                if (this._indirect_id_buffer.Count <= 0)
                    throw new System.Exception("Error, indirect id buffer count corrupted");

                var index = this._indirect_id_map.Array[id];
                if (!index._init)
                    throw new System.Exception("Error, index is not initialized for remove instance id indirect id");

                var end_index = this._indirect_id_buffer.Count - 1;
                var end_index_data = this._indirect_id_buffer.Array[end_index];
                var index_data = this._indirect_id_buffer.Array[index._index];

                if (!index_data.Initialized)
                    throw new System.Exception("Error, expected input indirect instance id to exists for deletion!");
                if (index_data.instance_id != id)
                    throw new System.Exception("Error, incorrect id found in indirectID remove id");

                this._indirect_id_buffer.Array[index._index] = new IndirectIDData(end_index_data.instance_id, index_data._frame, index_data._delta_index); // update id, keep delta index the same
                this._indirect_id_buffer.Array[end_index] = new IndirectIDData(instancemesh.NULL_ID, end_index_data._frame, end_index_data._delta_index); // set new id for end index (keep delta index the same), *Note update ordering is important, (if the end index & remove index are the same)
                this._indirect_id_buffer.SetCount(end_index); // decrement count

                var index_delta = new IndirectIDDelta(end_index_data.instance_id, index._index);
                var end_delta = new IndirectIDDelta(instancemesh.NULL_ID, this._indirect_id_buffer.Count);

                this._indirect_id_map.Array[end_index_data.instance_id] = new IndirectIDIndex(index_delta.indirect_index); // set new indirect index for the end index instance
                this._indirect_id_map.Array[index_data.instance_id] = default(IndirectIDIndex); // reset the item that was removed

                update_delta_buffer(index_delta, frame);
                update_delta_buffer(end_delta, frame); // *Note update ordering is important, (if the end index &remove index are the same)
            }

            public int UpdateComputeBuffer(ComputeBuffer buffer, UnityEngine.Rendering.CommandBuffer cmd)
            {
                var delta_count = this.CurrentDeltaCount;

                if (delta_count <= 0) // do nothing if no changes
                {
                    return 0;
                }

                // Set buffer
                cmd.SetBufferData(buffer, this._delta_buffer.Array, this._delta_offset, 0, delta_count);// buffer.SetData(this._delta_buffer.Array, 0, 0, delta_count);

                // Get true delta count
                var true_delta_count = this._delta_buffer.Count - this._delta_offset;

                if (true_delta_count <= MaxDeltaCount) // if delta array is less than max, just clear it
                {
                    this._delta_buffer.SetCount(0); // set count to 0- this will leave garbage on the buffer
                    this._delta_offset = 0; // reset offset back to zero
                }
                else // otherwise all items should be copied forward
                {
                    this._delta_offset += MaxDeltaCount; // more instances are left on the buffer past the max delta count...

                }

                return delta_count;
            }

            public void Dispose()
            {
                this._indirect_id_buffer = null;
                this._indirect_id_map = null;
                this._delta_buffer = null;
                this._delta_offset = 0;
            }

#if UNITY_EDITOR
            public static void test()
            {
                Debug.Log("Starting InstanceMesh DeltaBuffer Test...");

                var n = 10000;

                var buff = new InstanceMeshIndirectIDBuffer(3, 100);
                HashSet<int> final_set = new HashSet<int>();
                // Add all items
                for (int i = 1; i < n; i++)
                {
                    buff.TryAddInstanceID(i, InstanceMeshDeltaBuffer<instancemesh.instance_delta>.kInitialFrameCount);
                    final_set.Add(i);
                }
                var r = new System.Random(0);
                int[] gpu_index_buff = new int[n];
                for (int frame = InstanceMeshDeltaBuffer<instancemesh.instance_delta>.kInitialFrameCount; frame < 1000; frame++)
                {
                    // Mutate
                    for (int i = 0; i < n; i++)
                    {
                        var id = r.Next(1, n);
                        if (r.Next() % 2 == 0 || !buff._indirect_id_map.Array[id]._init)
                        {
                            buff.TryAddInstanceID(id, frame);
                            final_set.Add(id);
                        }
                        else
                        {
                            buff.RemoveInstanceID(id, frame);
                            final_set.Remove(id);
                        }
                    }

                    // Test assertions
                    if (new HashSet<int>(buff._delta_buffer.Select(x => x.indirect_index)).Count != buff._delta_buffer.Count)
                        throw new System.Exception("Error, delta buffer has duplicates");
                    // Apply delta buffer
                    for (int i = 0; i < buff._delta_buffer.Count; i++)
                    {
                        var delta = buff._delta_buffer.Array[i];
                        gpu_index_buff[delta.indirect_index] = delta.instance_id;
                    }
                    // Compare 'gpu' buffer
                    for (int i = 0; i < buff._indirect_id_buffer.Count; i++)
                        if (gpu_index_buff[i] != buff._indirect_id_buffer.Array[i].instance_id)
                            throw new System.Exception("Error, buffer not built correctly");
                    for (int i = 0; i < buff._indirect_id_map.Array.Length; i++)
                    {
                        if (buff._indirect_id_map.Array[i]._init)
                        {
                            var index = buff._indirect_id_map.Array[i]._index;
                            if (gpu_index_buff[index] != i)
                                throw new System.Exception("Error, incorrect id found");
                        }
                    }

                    buff._delta_buffer.Clear(); // clear delta buffer
                }

                var buff_id_buffer = buff._indirect_id_buffer.Select(x => x.instance_id).ToList();
                buff_id_buffer.Sort();

                var final_id_buffer = final_set.ToList();
                final_id_buffer.Sort();

                var gpu_id_buffer = gpu_index_buff.Take(buff._indirect_id_buffer.Count).ToList();
                gpu_id_buffer.Sort();

                if (!final_id_buffer.SequenceEqual(buff_id_buffer))
                    throw new System.Exception("Failed buffer check");
                if (!final_id_buffer.SequenceEqual(gpu_id_buffer))
                    throw new System.Exception("Failed gpu buffer check");

                Debug.Log("Passed InstanceMesh DeltaBuffer Test");
            }
#endif
        }
    }
}
