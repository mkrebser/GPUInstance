using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GPUInstance
{
    public class GroupBindPosesDeltaBuffer
    {
        /// <summary>
        /// buffer used to ship stuff to the GPU
        /// </summary>
        private SimpleList<float> _delta_buffer;
        /// <summary>
        /// Poses that have been added.
        /// </summary>
        private HashSet<int> _current_poses;

        private Dictionary<int, int> _group_id_to_delta_index;

        private int MaxDeltaPoses = 0;
        private int NumSkeletonBones = 0;
        private int DeltaStride = 0;
        private int _delta_offset = 0;

        private Matrix4x4[] kNullBindPose = null;

        public int CurrentDeltaCount
        {
            get
            {
                if (this._delta_buffer.Count % this.DeltaStride != 0)
                    throw new System.Exception("Error, bind pose delta buffer corrupted");
                return System.Math.Min(this.MaxDeltaPoses, (this._delta_buffer.Count / this.DeltaStride) - this._delta_offset);
            }
        }

        public GroupBindPosesDeltaBuffer(in int DeltaBufferCount, in int float_stride, in int NumSkeletonBones)
        {
            this._delta_buffer = new SimpleList<float>(DeltaBufferCount * float_stride);
            this._current_poses = new HashSet<int>();
            this.MaxDeltaPoses = DeltaBufferCount;
            this.NumSkeletonBones = NumSkeletonBones;
            this.DeltaStride = float_stride;
            this._group_id_to_delta_index = new Dictionary<int, int>();
            this.kNullBindPose = new Matrix4x4[this.NumSkeletonBones];
        }

        void AddToDeltaBuffer(in Matrix4x4[] bind_pose, in int group_id)
        {
            this._delta_buffer.Add((float)group_id + 0.1f); // note cast to float precision
            for (int i = 0; i < this.NumSkeletonBones; i++)
            {
                var m = i < bind_pose.Length ? bind_pose[i] : Matrix4x4.identity;
                this._delta_buffer.Add(m.m00);
                this._delta_buffer.Add(m.m01);
                this._delta_buffer.Add(m.m02);
                this._delta_buffer.Add(m.m03);

                this._delta_buffer.Add(m.m10);
                this._delta_buffer.Add(m.m11);
                this._delta_buffer.Add(m.m12);
                this._delta_buffer.Add(m.m13);

                this._delta_buffer.Add(m.m20);
                this._delta_buffer.Add(m.m21);
                this._delta_buffer.Add(m.m22);
                this._delta_buffer.Add(m.m23);

                this._delta_buffer.Add(m.m30);
                this._delta_buffer.Add(m.m31);
                this._delta_buffer.Add(m.m32);
                this._delta_buffer.Add(m.m33);
            }
        }
        void CopyToDeltaBuffer(in Matrix4x4[] bind_pose, in int group_id, in int delta_index)
        {
            int di = delta_index;
            this._delta_buffer.Array[di] = (float)group_id + 0.1f; // note cast to float precision
            di++;

            for (int i = 0; i < this.NumSkeletonBones; i++, di += 16)
            {
                var m = i < bind_pose.Length ? bind_pose[i] : Matrix4x4.identity;

                this._delta_buffer.Array[di + 0]  = m.m00;
                this._delta_buffer.Array[di + 1]  = m.m01;
                this._delta_buffer.Array[di + 2]  = m.m02;
                this._delta_buffer.Array[di + 3]  = m.m03;

                this._delta_buffer.Array[di + 4]  = m.m10;
                this._delta_buffer.Array[di + 5]  = m.m11;
                this._delta_buffer.Array[di + 6]  = m.m12;
                this._delta_buffer.Array[di + 7]  = m.m13;

                this._delta_buffer.Array[di + 8]  = m.m20;
                this._delta_buffer.Array[di + 9]  = m.m21;
                this._delta_buffer.Array[di + 10] = m.m22;
                this._delta_buffer.Array[di + 11] = m.m23;

                this._delta_buffer.Array[di + 12] = m.m30;
                this._delta_buffer.Array[di + 13] = m.m31;
                this._delta_buffer.Array[di + 14] = m.m32;
                this._delta_buffer.Array[di + 15] = m.m33;
            }
        }

        public void AddPose(in int group_id, in Matrix4x4[] bind_pose)
        {
            if (group_id == instancemesh.NULL_ID)
                throw new System.Exception("Error, input NULL ID for add pose");
            if (bind_pose.Length > this.NumSkeletonBones)
                throw new System.Exception("Error, input bind pose has too many bones");
            if (group_id >= ushort.MaxValue) // id cannot be very large due to float precision (this is okay.. the id corresponds to the number of mesh types.. )
                throw new System.Exception("Error, group id too big");

            if (!this._current_poses.Add(group_id)) // cannot add for tpye that already exists
                throw new System.Exception("Error, tried to add the same group id twice");

            int delta_index = int.MinValue;
            if (this._group_id_to_delta_index.TryGetValue(group_id, out delta_index)) // Check if a bind pose using the input id is already written to the delta buffer
            {
                // If true.. Then just overwrite the old bind pose
                CopyToDeltaBuffer(bind_pose, group_id, delta_index);
            }
            else // otherwise.. append a new bind pose
            {
                // add id->delta index mapping
                this._group_id_to_delta_index.Add(group_id, this._delta_buffer.Count);
                // Add to delta buffer
                AddToDeltaBuffer(bind_pose, group_id);
            }
        }

        public void RemovePose(in int group_id)
        {
            if (!this._current_poses.Remove(group_id))
                throw new System.Exception("Error, failed to remove pose!");
            int delta_index = int.MinValue;
            if (this._group_id_to_delta_index.TryGetValue(group_id, out delta_index)) // check if same bind pose written in same update frame
            {
                if (!this._group_id_to_delta_index.Remove(group_id))
                    throw new System.Exception("Internal Error, failed to remove bind pose from delta map!");
                CopyToDeltaBuffer(this.kNullBindPose, instancemesh.NULL_ID, delta_index); // just set a '0' buffer- it will be ignored by shader
            }
        }

        /// <summary>
        /// Set compute buffer data. Returns number of deltas (in number of complete skeleton bind poses sent to GPU)
        /// </summary>
        /// <param name="cBuffer"></param>
        /// <returns></returns>
        public int UpdateComputeBuffer(ComputeBuffer cBuffer, UnityEngine.Rendering.CommandBuffer cmd)
        {
            if (this._delta_buffer.Count % this.DeltaStride != 0)
                throw new System.Exception("Error, bind pose buffer corrupted");

            var delta_count = this.CurrentDeltaCount;

            if (delta_count <= 0) // do nothing if no changes
                return 0;

            // Set buffer
            cmd.SetBufferData(cBuffer, this._delta_buffer.Array, this._delta_offset * DeltaStride, 0, delta_count * this.DeltaStride); // cBuffer.SetData(this._delta_buffer.Array, 0, 0, delta_count * this.DeltaStride);

            // Get true delta count
            var true_delta_count = (this._delta_buffer.Count / this.DeltaStride) - this._delta_offset;

            if (true_delta_count <= this.MaxDeltaPoses) // if delta array is less than max, just clear it
            {
                this._delta_offset = 0;
                this._delta_buffer.SetCount(0);
                this._group_id_to_delta_index.Clear();
            }
            else // otherwise all items should be copied forward
            {
                // pop left
                this._delta_offset += this.MaxDeltaPoses;
            }

            return delta_count;
        }

        public void Dispose()
        {
            this._delta_buffer = null;
            this._current_poses = null;
            this.DeltaStride = 0;
            this.NumSkeletonBones = 0;
            this.MaxDeltaPoses = 0;
        }
    }
}
