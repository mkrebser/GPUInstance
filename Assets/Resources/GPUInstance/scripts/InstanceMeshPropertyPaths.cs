using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;

namespace GPUInstance
{
    public class InstanceMeshPropertyPaths
    {
        const int kInitialFrameCount = 1;
        const int kPathPointFloatStride = 5; // 3 for vec3 position,  1 for t_val, 1 for up

        /// <summary>
        /// PathID generator
        /// </summary>
        private instancemesh.InstanceIDGenerator PathIDs = null;

        /// <summary>
        /// buffer used to ship stuff to the GPU
        /// </summary>
        private SimpleList<float> _delta_buffer = null;
        /// <summary>
        /// used so integer precision can be used for id for the delta buffer
        /// </summary>
        private SimpleList<int> _path_id_delta_buffer = null;
        /// <summary>
        /// tracks what frame and index in delta buffer each path used to push updates to gpu
        /// </summary>
        private SimpleList<DeltaIndex> _path_delta_indices = null;
        private long _frame_count = kInitialFrameCount;

        private float[] _path_t_temp_buffer = null;
        private Vector3[] _path_up_temp_buffer = null;
        private Vector3[] _path_tmp_temp_buffer = null;
        private float[] _path_tmp2_temp_buffer = null;

        private Vector3[] kNullPath = null;

        /// <summary>
        /// How many steps to each path
        /// </summary>
        public int PathCount { get; private set; } = 0;
        public int PathFloatStride { get { return this.PathCount * kPathPointFloatStride + (int)PathArgs.NumPathArgs; } }

        public int CurrentDeltaCount
        {
            get
            {
                return System.Math.Min(this.MaxDeltaCount, this._path_id_delta_buffer.Count - this._delta_offset);
            }
        }
        private int MaxDeltaCount = 0;
        private int _delta_offset = 0;

        /// <summary>
        /// Number of paths that have been allocated
        /// </summary>
        public int NumPathAllocated { get; private set; }

        public InstanceMeshPropertyPaths(in int PathCount, in int MaxDeltaCount)
        {
            if (PathCount < 2 || PathCount > 64)
                throw new System.Exception("Error invalid path size specified. Path must be between 2 and 64 points!");
            this.PathIDs = new instancemesh.InstanceIDGenerator();
            this.PathCount = PathCount;
            this.MaxDeltaCount = MaxDeltaCount;
            this._delta_buffer = new SimpleList<float>();
            this._path_id_delta_buffer = new SimpleList<int>();
            this._path_delta_indices = new SimpleList<DeltaIndex>();
            this._path_t_temp_buffer = new float[PathCount];
            this._path_up_temp_buffer = new Vector3[PathCount];
            this.kNullPath = new Vector3[PathCount];
            this._path_tmp_temp_buffer = new Vector3[PathCount];
            this._path_tmp2_temp_buffer = new float[PathCount];
        }

        void try_update_size(SimpleList<DeltaIndex> l, int new_size)
        {
            if (new_size > 100000000)
                throw new System.Exception("Delta Buffer size too large! 100 million is too many"); // sanity check
            if (new_size >= this._path_delta_indices.Array.Length)
                this._path_delta_indices.Resize(new_size * 2);
        }

        void AddPathToDeltaBuffer(in Path p, Vector3[] path, float[] path_t, Vector3[] path_up, int start_index = 0)
        {
            float default_up = Pathing.PackDirection(Vector3.up);
            bool use_default_up = ReferenceEquals(null, path_up);
            bool use_default_t = ReferenceEquals(null, path_t);

            // Add args
            int delta_start_index = this._delta_buffer.Count;
            for (int i = 0; i < (int)PathArgs.NumPathArgs; i++)
                this._delta_buffer.Add(0);
            this._delta_buffer.Array[delta_start_index + (int)PathArgs.Flags] = p.Flags2Float();
            this._delta_buffer.Array[delta_start_index + (int)PathArgs.CompletionTime] = p.RawPathCompletionTime;
            this._delta_buffer.Array[delta_start_index + (int)PathArgs.AvgWeight] = p.AvgWeight;
            this._delta_buffer.Array[delta_start_index + (int)PathArgs.Length] = p.LengthFloat;

            // Add path
            int plen = (int)p.Length;
            for (int i = 0; i < this.PathCount; i++)
            {
                if (i < plen)
                {
                    int index = start_index + i;
                    this._delta_buffer.Add(path[index].x);
                    this._delta_buffer.Add(path[index].y);
                    this._delta_buffer.Add(path[index].z);
                    this._delta_buffer.Add(use_default_t ? 0.0f : path_t[index]);
                    this._delta_buffer.Add(use_default_up ? default_up : Pathing.PackDirection(path_up[index]));
                }
                else
                { // fill excess space
                    this._delta_buffer.Add(0);
                    this._delta_buffer.Add(0);
                    this._delta_buffer.Add(0);
                    this._delta_buffer.Add(1.0f); // add 1.0f because path should be ended
                    this._delta_buffer.Add(default_up);
                }
            }
        }
        void CopyPathToDeltaBuffer(in Path p, Vector3[] path, float[] path_t, int delta_start_index, Vector3[] path_up, int start_index = 0)
        {
            float default_up = Pathing.PackDirection(Vector3.up);
            bool use_default_up = ReferenceEquals(null, path_up);
            bool use_default_t = ReferenceEquals(null, path_t);

            // copy args
            delta_start_index *= this.PathFloatStride;
            this._delta_buffer.Array[delta_start_index + (int)PathArgs.Flags] = p.Flags2Float();
            this._delta_buffer.Array[delta_start_index + (int)PathArgs.CompletionTime] = p.RawPathCompletionTime;
            this._delta_buffer.Array[delta_start_index + (int)PathArgs.AvgWeight] = p.AvgWeight;
            this._delta_buffer.Array[delta_start_index + (int)PathArgs.Length] = p.LengthFloat;
            delta_start_index += (int)PathArgs.NumPathArgs;

            // Copy path
            int plen = p.Length;
            for (int i = 0, di = delta_start_index; i < this.PathCount; i++, di += kPathPointFloatStride)
            {
                if (i < plen)
                {
                    int index = i + start_index;
                    this._delta_buffer.Array[di] = path[index].x;
                    this._delta_buffer.Array[di + 1] = path[index].y;
                    this._delta_buffer.Array[di + 2] = path[index].z;
                    this._delta_buffer.Array[di + 3] = use_default_t ? 0.0f : path_t[index];
                    this._delta_buffer.Array[di + 4] = use_default_up ? default_up : Pathing.PackDirection(path_up[index]);
                }
                else
                { // fill excess space
                    this._delta_buffer.Array[di + 0] = 0;
                    this._delta_buffer.Array[di + 1] = 0;
                    this._delta_buffer.Array[di + 2] = 0;
                    this._delta_buffer.Array[di + 3] = 1.0f; // add 1.0f because path should be ended
                    this._delta_buffer.Array[di + 4] = default_up;
                }
            }
        }

        public void AllocateNewPath(ref Path p, Vector3[] path, float[] path_t, Vector3[] path_up, int start_index = 0)
        {
            if (!p.use_constants && (ReferenceEquals(null, path_t) || ReferenceEquals(null, path_up)))
                throw new System.Exception("Error, required parameters path_up and path_t");

            p.path_id = this.PathIDs.GetNewID();
            update_path(p, path, path_t, path_up, start_index, do_init_check: false);
            this.NumPathAllocated++;
        }

        public int AllocateNewPathID()
        {
            int id = this.PathIDs.GetNewID();
            this.NumPathAllocated++;

            try_update_size(this._path_delta_indices, id);
            this._path_delta_indices.Array[id] = new DeltaIndex(long.MaxValue, id - 1); // just assign frame value that isnt the same as the current

            return id;
        }

        public void DeletePath(ref Path p)
        {
            if (p.path_id < 0 || p.path_id == instancemesh.NULL_ID || p.path_id > this.PathIDs.MaxAllocatedID)
                throw new System.Exception("Error, input an invalid path id");

            this.update_path(p, kNullPath, null, null);
            this.PathIDs.ReleaseID(p.path_id);
            this.NumPathAllocated--;
            p.path_id = instancemesh.NULL_ID;
        }

        public void UpdatePath(ref Path p, Vector3[] path, float[] path_t, Vector3[] path_up, int start_index = 0)
        {
            if (!p.use_constants && (ReferenceEquals(null, path_t) || ReferenceEquals(null, path_up)))
                throw new System.Exception("Error, required parameters path_up and path_t");
            if (p.path_id < 0 || p.path_id == instancemesh.NULL_ID || p.path_id > this.PathIDs.MaxAllocatedID)
                throw new System.Exception("Error, input an invalid path id");

            update_path(p, path, path_t, path_up, start_index);
        }

        private void update_path(in Path p, Vector3[] path, float[] path_t, Vector3[] path_up, int start_index = 0, bool do_init_check = true)
        {
            if (p.path_id == instancemesh.NULL_ID || p.path_id < 0)
                throw new System.Exception("Error, tried to update with invalid id! All paths must have a non NULL positive id.");

            if (do_init_check && !this._path_delta_indices.Array[p.path_id].Initialized())
                throw new System.Exception("Error, input path id does not exist!");

            int delta_index = -1;

            try_update_size(this._path_delta_indices, p.path_id); // try to update path index buffer length

            if (this._path_delta_indices.Array[p.path_id]._frame == this._frame_count) // check if the path has already been written in this frame...
            { // if it has, just overwrite
                delta_index = this._path_delta_indices.Array[p.path_id]._index;

                if (this._path_id_delta_buffer.Array[delta_index] != p.path_id)
                    throw new System.Exception("Error, ids misaligned in path delta buffer");

                CopyPathToDeltaBuffer(p, path, path_t, delta_index, path_up, start_index);
            }
            else // if it has not been written this frame...
            { // append it to the delta buffer
                delta_index = this._path_id_delta_buffer.Count;
                this._path_delta_indices.Array[p.path_id] = new DeltaIndex(this._frame_count, delta_index);
                this._path_id_delta_buffer.Add(p.path_id);
                AddPathToDeltaBuffer(p, path, path_t, path_up, start_index);
            }
        }

        /// <summary>
        /// [Main Thread]
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        public int UpdateComputeBuffers(ComputeBuffer buffer_delta, ComputeBuffer buffer_delta_ids, UnityEngine.Rendering.CommandBuffer cmd)
        {

            if (this._delta_buffer.Count != this._path_id_delta_buffer.Count * this.PathFloatStride || this._delta_buffer.Count % this.PathFloatStride != 0)
                throw new System.Exception("Error, path update buffer corrupted!");

            var delta_count = this.CurrentDeltaCount;
            if (delta_count <= 0) // do nothing if no changes
            {
                this._frame_count++;
                return 0;
            }

            // Set buffer
            cmd.SetBufferData(buffer_delta_ids, this._path_id_delta_buffer.Array, this._delta_offset, 0, delta_count);
            cmd.SetBufferData(buffer_delta, this._delta_buffer.Array, this._delta_offset * this.PathFloatStride, 0, delta_count * this.PathFloatStride);

            // Get true delta count
            var true_delta_count = this._path_id_delta_buffer.Count - this._delta_offset;

            if (true_delta_count <= MaxDeltaCount) // if delta array is less than max, just clear it
            {
                this._path_id_delta_buffer.SetCount(0); // set count to 0- this will leave garbage on the buffer
                this._delta_buffer.SetCount(0);
                this._delta_offset = 0; // reset offset back to zero
            }
            else // otherwise all items should be copied forward
            {
                this._delta_offset += MaxDeltaCount; // more instances are left on the buffer past the max delta count...
            }

            if (this._path_id_delta_buffer.Count == 0)
            {
                this._frame_count++;
            }

            return delta_count;

        }

        public void Dispose()
        {
            this.PathIDs = null;
            this.PathCount = 0;
            this._delta_buffer = null;
            this._path_id_delta_buffer = null;
            this._path_delta_indices = null;
            this._delta_offset = 0;
            this.MaxDeltaCount = 0;
            this._path_t_temp_buffer = null;
            this._path_tmp_temp_buffer = null;
            this._path_tmp2_temp_buffer = null;
            this.kNullPath = null;
        }

        public enum PathArgs
        {
            Flags = 0,
            CompletionTime = 1,
            AvgWeight = 2,
            Length = 3,
            NumPathArgs = 4
        }

        struct DeltaIndex
        {
            public long _frame;
            public int _index;

            public bool Initialized() { return this._frame > 0; }

            public DeltaIndex(in long frame, in int index)
            {
                this._frame = frame;
                this._index = index;
            }
        }
    }
}
