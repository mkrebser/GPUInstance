using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GPUInstance
{
    /// <summary>
    /// Helper class that maintains the number of skeletons and bones being used on the gpu
    /// </summary>
    internal class SkeletonIDMap
    {
        public int NumSkeletonBones { get; private set; }

        System.Collections.Generic.Queue<int> free_id_queue;

        private HashSet<int> _skeletons;

        private int _max_generated_id = 0;

        /// <summary>
        /// number of currently existing skeletons
        /// </summary>
        public int NumSkeletonInstances { get { return _skeletons.Count; } }

        public SkeletonIDMap(int NumSkeletonBones)
        {
            this.NumSkeletonBones = NumSkeletonBones;

            // Enqueue all valid ids
            this.free_id_queue = new System.Collections.Generic.Queue<int>();
            this._skeletons = new HashSet<int>();
        }
        private SkeletonIDMap() { }

        /// <summary>
        /// Gets a new skeleton id. Returns invalid id if no more ids left.
        /// </summary>
        /// <returns></returns>
        public int GetNewSkeletonID()
        {
            int new_id = -1;

            if (free_id_queue.Count == 0)
            {
                this._max_generated_id += this.NumSkeletonBones;
                new_id = this._max_generated_id ; // new id is just the skeleton count
            }
            else
            {
                new_id = this.free_id_queue.Dequeue(); // just return unused id
            }

            if (!_skeletons.Add(new_id))
                throw new System.Exception("Error, the generated skeleton id already exists!");

            return new_id;
        }

        /// <summary>
        /// Release a skeleton id that is no longer needed. You must release all ids that you are done with or they will be leaked.
        /// </summary>
        /// <param name="id"></param>
        public void ReleaseSkeletonID(in int id)
        {
            if (id < 0 || id == instancemesh.NULL_ID || id == instancemesh.INVALID_ID || id % this.NumSkeletonBones != 0)
                throw new System.Exception("Error, incorrect skeleton id!");

            if (!this._skeletons.Remove(id))
                throw new System.Exception("Error, input skeleton id does not exist.");

            this.free_id_queue.Enqueue(id);
        }

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            this.free_id_queue = null;
            this._skeletons = null;
            this._max_generated_id = 0;
            this.NumSkeletonBones = 0;
        }
    }
}