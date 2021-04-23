using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace GPUInstance
{
    /// <summary>
    /// Class that helps to manage the instance data parent & child transform hierarchy
    /// </summary>
    internal class InstanceHierarchyMap
    {
        // This class actually just manages an index map that determines what parent-child depth each gpu instance is at in the hierarchy.
        // This is done so that the correct ordering is used when computing the ObjectToWorld matrices for each instance.
        // (ie, they are computed from parent -> child order (0... to.. Max depth))
        // The computation is done in seperate kernel invocations, 1 for each depth level.

        // A good way to image the index map is as a 2d array. One row for each depth level & enough columns in each row for the maximim instance count.
        // Each element in the array is just an instance id. -Each instance id will only appear once in the entire 2d array.
        // A kernel is invoked for each row of instance ids (all instances in a row have the same depth, therefore cannot be parented to eachother)
        // Furthermore, each row is compacted. They fill from left->right (ie 0...N) so the left half of the row is filled & the right half is empty
        // (the column ordering that each instance id is at has no meaning, all that matters is it be at some depth level)

        internal struct ChildList
        {
            /// <summary>
            /// Use the id->index map for lookups when more than X children
            /// </summary>
            public const int kUseMapLookup = 32;
            private SimpleList<int> children;
            private HashSet<int> children_set;

            public bool Initialized()
            {
                return !ReferenceEquals(null, this.children);
            }

            public ChildList(SimpleList<int> child_array)
            {
                this.children = child_array;
                this.children_set = null;
            }

            public int Count
            {
                get
                {
                    if (ReferenceEquals(null, this.children_set))
                    {
                        return ReferenceEquals(null, this.children) ? 0 : this.children.Count;
                    }
                    else
                    {
                        return this.children_set.Count;
                    }
                }
            }

            public IEnumerable<int> Children
            {
                get
                {
                    if (ReferenceEquals(null, this.children_set))
                    {
                        for (int i = 0; i < this.children.Count; i++)
                            yield return this.children.Array[i];
                    }
                    else
                    {
                        foreach (var child in this.children_set)
                            yield return child;
                    }
                }
            }

            /// <summary>
            /// Get children in reversed order... suitable for deletion
            /// </summary>
            public IEnumerable<int> ChildDeleteIterator
            {
                get
                {
                    if (!ReferenceEquals(null, this.children_set)) // temporarily use children array if set is being used
                    {
                        this.children.Clear();
                        foreach (var child in this.children_set)
                            this.children.Add(child);
                    }

                    int count = this.children.Count;
                    for (int i = count - 1; i > -1; i--)
                        yield return this.children.Array[i];

                    if (!ReferenceEquals(null, this.children_set)) // temporarily use children array if set is being used
                    {
                        this.children.Clear();
                    }
                }
            }

            public SimpleList<int> GetChildList(bool fill=false)
            {
                return this.children;
            }

            public void AddChild(in int instance_id)
            {
                if (ReferenceEquals(null, this.children_set))
                {
                    this.children.Add(instance_id);
                }
                else
                {
                    this.children_set.Add(instance_id);
                }

                if (this.children.Count >= kUseMapLookup && ReferenceEquals(null, this.children_set))
                {
                    // Init child set.. Will use a set from now on...
                    this.children_set = new HashSet<int>();
                    for (int i = 0; i < this.children.Count; i++)
                        this.children_set.Add(this.children.Array[i]);

                    this.children.Clear(); // clear old array
                }
            }
            public bool RemoveChild(in int instance_id)
            {
                if (ReferenceEquals(null, this.children_set))
                {
                    return children.RemoveSwapReverseSearch(instance_id);
                }
                else
                {
                    return this.children_set.Remove(instance_id);
                }
            }
        }

        internal struct hierarchy_data
        {
            public int parent;
            public int index_map_id;
            public int depth;
            public bool initialized;
        }
        struct index_data
        {
            /// <summary>
            /// gpu instance id
            /// </summary>
            public int instance_id;
            /// <summary>
            /// what delta buffer index is being used for this index data
            /// </summary>
            public int delta_buffer_index;
            /// <summary>
            /// If this index is currently in the delta buffer
            /// </summary>
            public bool in_delta_buffer;
            public index_data(in int id, in int delta_index, in bool in_delta) { this.instance_id = id; this.delta_buffer_index = delta_index; this.in_delta_buffer = in_delta; }
        }
        internal struct hierarchy_delta : System.IEquatable<hierarchy_delta>
        {
            /// <summary>
            /// index_data index
            /// </summary>
            public int index;
            /// <summary>
            /// index_data depth
            /// </summary>
            public int depth;
            /// <summary>
            /// instance data id
            /// </summary>
            public int instance_id;
            /// <summary>
            /// if this delta instance is dirty (ie needs to be sent to the gpu). 0 = not dirty. Anything else = dirty.
            /// </summary>
            public int dirty;

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 29;
                    hash = hash * 13 + index.GetHashCode();
                    hash = hash * 13 + depth.GetHashCode();
                    return hash;
                }
            }
            public bool Equals(hierarchy_delta d)
            {
                return d.index == this.index && d.depth == this.depth;
            }
            public bool IsDirty() { return this.dirty != 0; }
        }
        public const int kHierarchyDeltaStride = sizeof(int) * 4;

        hierarchy_data[] _h_map; // hierarchy map, index by instance id
        bool _init = false;

        internal SimpleList<hierarchy_delta> _delta_buffer; // delta buffer (used to puch data to the GPU)

        // hierarchy index data, we have to manage a copy in non-GPU memory because a lot of the operations are sequential- and we reformat them so it can be done in parallel
        // (essentially, all hierchy changes are compounded into a 'delta map' where each instance can change at most 1 time per update)
        // Also, this is a 2d map. It is accessed by an (index, depth) pair. The data stored at each element is the instance data id.
        index_data[] _h_index_data;
        // length of each list in the index data map array. There is a list for every depth level.
        int[] _h_index_data_length;
        index_data GetIndexData(in int index, in int depth) { return _h_index_data[this.MaxInstancesPerDepth * depth + index]; }
        void SetIndexData(in int index, in int depth, in index_data data) { _h_index_data[this.MaxInstancesPerDepth * depth + index] = data; }

        /// <summary>
        /// CHild instance array. TODO: perhaps investigate a fixed array so that every instance with children does not create a newList()
        /// </summary>
        ChildList[] _child_array;
        /// <summary>
        /// Array pool to reduce collection numbers
        /// </summary>
        AsynchPool<SimpleList<int>> _array_pool;

        /// <summary>
        /// Maximum child parent depth
        /// </summary>
        public readonly int MaxDepth;
        public readonly int MaxDeltaCount;
        private readonly int _thread_group_count;

        /// <summary>
        /// Total hierchy index map size
        /// </summary>
        public int NumHierarchyIndices
        {
            get
            {
                //Note* _h_index data length assumed to never change
                return _h_index_data.Length;
            }
        }
        /// <summary>
        /// Number of items currently in the delta buffer
        /// </summary>
        public int CurrentDeltaCount
        {
            get
            {
                return System.Math.Min(this.MaxDeltaCount, this._delta_buffer.Count);
            }
        }
        /// <summary>
        /// Number of instances at a particular depth
        /// </summary>
        /// <param name="depth"></param>
        /// <returns></returns>
        public int NumInstancesAtDepth(in int depth)
        {
            return _h_index_data_length[depth];
        }

        public int MaxInstancesPerDepth { get; private set; }

        public InstanceHierarchyMap(int kInitialMaxInstanceCount, int MaxDeltaCount, int MaxDepth, int thread_group_count)
        {
            if (kInitialMaxInstanceCount % thread_group_count != 0 || MaxDeltaCount % thread_group_count != 0)
                throw new System.Exception("Error, hierarchy instance counts should be divisible by thread group count");

            this.MaxDepth = MaxDepth;
            this.MaxDeltaCount = MaxDeltaCount;
            this.MaxInstancesPerDepth = kInitialMaxInstanceCount;
            this._thread_group_count = thread_group_count;

            this._delta_buffer = new SimpleList<hierarchy_delta>(MaxDeltaCount); // init delta buffer
            this._h_index_data_length = new int[MaxDepth]; // init per-depth counts

            this._h_map = new hierarchy_data[kInitialMaxInstanceCount];
            this._h_index_data = new index_data[kInitialMaxInstanceCount * MaxDepth];
            this._array_pool = new AsynchPool<SimpleList<int>>(kInitialMaxInstanceCount);
            this._child_array = new ChildList[kInitialMaxInstanceCount];

            _init = true;
        }
        private InstanceHierarchyMap() { }

        void try_resize(int required_minimum_id)
        {
            if (required_minimum_id > 100000000)
                throw new System.Exception("100 million entities is too many"); // sanity check

            if (this._h_map.Length > required_minimum_id)
                return; // do nothing if size requirement is fufilled

            var add_amount = System.Math.Min(required_minimum_id, 500000); // maximum increments of 500k instances
            var new_size = required_minimum_id + add_amount; // the new size will be larger than the needed size
            new_size = new_size + (_thread_group_count - (new_size % _thread_group_count)); // make divisible by num thread groups

            // New items to update
            var new_h_map = new hierarchy_data[new_size];
            var new_index_map = new index_data[new_size * this.MaxDepth];
            var new_child_array = new ChildList[new_size];
            var new_array_pool = new AsynchPool<SimpleList<int>>(new_size);
            var new_instance_per_depth = new_size;

            // copy hierarchy map directly
            for (int i = 0; i < this._h_map.Length; i++)
                new_h_map[i] = this._h_map[i];
            // copy children array directly
            for (int i = 0; i < this._child_array.Length; i++)
                new_child_array[i] = this._child_array[i];
            while (this._array_pool.Count > 0) // move pool to new pool
            {
                new_array_pool.Add(this._array_pool.Get());
            }

            // copy indices
            for (int depth = 0; depth < this.MaxDepth; depth++)
            {
                for (int index = 0; index < this._h_index_data_length[depth]; index++)
                {
                    new_index_map[depth * new_instance_per_depth + index] = this._h_index_data[depth * this.MaxInstancesPerDepth + index];
                }
            }

            // assign
            this._h_map = new_h_map;
            this._h_index_data = new_index_map;
            this._child_array = new_child_array;
            this._array_pool = new_array_pool;
            this.MaxInstancesPerDepth = new_instance_per_depth;
        }

        /// <summary>
        /// Set parent in the hierarchy. Can set to any parent id including null and invalid.
        /// </summary>
        /// <param name="data"></param>
        internal void SetParent(in instancemesh.instance_delta data)
        {
            if ((data.DirtyFlags & DirtyFlag.ParentID) != 0) // Check if parentID is changed (if it hasn't, then nothing will happen)
            {
                if (!_init)
                    throw new System.Exception("Error, hierarchy not initialized");

                set_parent(data.id, data.parentID);
            }
        }

        /// <summary>
        /// Set parent in the hierarchy. Can set to any parent id including null and invalid.
        /// </summary>
        /// <param name="child"></param>
        /// <param name="parent"></param>
        internal void SetParent(in int instance_id, in int new_parent)
        {
            if (!_init)
                throw new System.Exception("Error, hierarchy not initialized");

            set_parent(instance_id, new_parent);
        }

        /// <summary>
        /// Set parent in the hierarchy. Can set to any parent id including null and invalid.
        /// </summary>
        /// <param name="child"></param>
        /// <param name="parent"></param>
        void set_parent(in int instance_id, in int new_parent)
        {
            if (instance_id == instancemesh.NULL_ID || instance_id == instancemesh.INVALID_ID || instance_id < 0)
                throw new System.Exception("Error, invalid child id used!");
            if (new_parent < 0 && new_parent != instancemesh.INVALID_ID)
                throw new System.Exception("Error, invalid new parent id used");

            bool set_no_parent = new_parent == instancemesh.NULL_ID || new_parent == instancemesh.INVALID_ID;

            try_resize(instance_id); // attempt resize
            if (instance_id >= this._h_map.Length || new_parent >= this._h_map.Length)
                throw new System.Exception("Error, input hierarchy id is too big.");

            var hdata = this._h_map[instance_id];

            // Throw exception if parent is the same. It doesn't really matter if it is.. It's just bad practice on the users part.
            if (hdata.initialized && hdata.parent == new_parent)
                throw new System.Exception("Error, tried to change the parent to the same parent. wHy ThOuGh?");
            // Throw exception if tried to parent to uninitialized instance data
            if (!set_no_parent && !this._h_map[new_parent].initialized)
                throw new System.Exception("Error, tried to parent a child to an unitialized parent! Make sure the parent instance exists before attaching to it!");

            var new_depth = set_no_parent ? 0 : this._h_map[new_parent].depth + 1; // depth of new parent relationship
            var old_depth = hdata.depth; // depth of old relationship

            if (new_depth >= MaxDepth)
                throw new System.Exception("Error, tried to set a parent child relationship that exceeds maximum allowed hierarchy depth!");

            if (new_depth != old_depth || !hdata.initialized) // If the new depth is different or data must be initialized.. Then the index change will need to be shipped to the gpu
            {
                // If the instance data exists in the index map...  THen remove its old index data
                if (hdata.initialized && _h_index_data_length[old_depth] > 0) // only remove if there is more than 0 elements
                {
                    remove_depth(old_depth, hdata.index_map_id, instance_id);
                }

                { // Add a new index to the map at the new depth level
                    hdata.index_map_id = append_depth(new_depth, instance_id);
                    hdata.initialized = true;
                }
            }

            var old_parent = hdata.parent;
            if (old_parent != instancemesh.NULL_ID && old_parent != instancemesh.INVALID_ID) // if old parent exists...
            {
                remove_child(instance_id, old_parent); // Remove the instance from its old parent
            }
            if (new_parent != instancemesh.NULL_ID && new_parent != instancemesh.INVALID_ID) // if new parent exists...
            {
                add_child(instance_id, new_parent); // Add the instance to its new parent
            }

            // set hierarchy data
            hdata.parent = new_parent;
            hdata.depth = new_depth;
            this._h_map[instance_id] = hdata;

            // Update child depths
            if (new_depth != old_depth)
            {
                update_children_depth(instance_id, new_depth); // update children depths
            }
        }

        /// <summary>
        /// Remove an instance from the hierarchy
        /// </summary>
        /// <param name="child"></param>
        internal void Delete(in int instance_id)
        {
            if (!_init)
                throw new System.Exception("Error, hierarchy not initialized");
            if (instance_id == instancemesh.NULL_ID || instance_id == instancemesh.INVALID_ID || instance_id < 0)
                throw new System.Exception("Error, invalid child id used!");

            if (instance_id >= this._h_map.Length)
                throw new System.Exception("Error, input hierarchy id is too big or doesn't exist.");

            var hdata = this._h_map[instance_id];
            var old_depth = hdata.depth;

            if (!hdata.initialized)
                throw new System.Exception("Error, tried to unparent an uninitialized instance!");

            if (_h_index_data_length[old_depth] < 1)
                throw new System.Exception("Error, there should be elements in the heirarchy to delete!");

            // De-parent all children if there are any
            if (_child_array[instance_id].Initialized())
            {
                var children = _child_array[instance_id];
                foreach (var child_instance_id in children.ChildDeleteIterator)
                {
                    this.set_parent(child_instance_id, instancemesh.NULL_ID);
                }
                if (_child_array[instance_id].Initialized())
                    throw new System.Exception("Error, child array should have been cleared for deleting instance in hierarchy!");
            }

            var old_parent = hdata.parent;
            if (old_parent != instancemesh.NULL_ID && old_parent != instancemesh.INVALID_ID) // if old parent exists...
            {
                remove_child(instance_id, old_parent); // Remove the instance from its old parent
            }

            // Remove this instance from the index map
            remove_depth(old_depth, hdata.index_map_id, instance_id);

            // reset hierarchy data
            hdata = default(hierarchy_data);
            this._h_map[instance_id] = hdata;
        }

        void remove_depth(in int depth, in int index, in int instance_id)
        {
            // this function remove an instance id from the index map at the desired index & depth
            // it is removed by swapping it with the end (of the index map) and then clearing the end
            // after that, the index map changes are pushed onto the delta buffer

            var len = _h_index_data_length[depth];
            var end_index_data = GetIndexData(len - 1, depth); // Get index data at the end in a temp variable
            var hdata_index_data = GetIndexData(index, depth); // Get index data for hdata

            if (hdata_index_data.instance_id != instance_id || end_index_data.instance_id == instancemesh.NULL_ID || end_index_data.instance_id == instancemesh.INVALID_ID || end_index_data.instance_id < 0)
                throw new System.Exception("Error, corrupted instance hierarchy index data");
            if (index >= len)
                throw new System.Exception("Error, corrupted instance hierarchy index data len");

            SetIndexData(index, depth, new index_data(end_index_data.instance_id, hdata_index_data.delta_buffer_index, hdata_index_data.in_delta_buffer)); // Move data at the end to the position of hdata
            SetIndexData(len - 1, depth, new index_data(instancemesh.NULL_ID, end_index_data.delta_buffer_index, end_index_data.in_delta_buffer)); // Set the value at the end to NULL, *Note update ordering is important, (if the end index & remove index are the same)

            // Update the swapped hierarchy data
            var end_index_hdata = this._h_map[end_index_data.instance_id];
            end_index_hdata.index_map_id = index;
            this._h_map[end_index_data.instance_id] = end_index_hdata;

            update_delta_buffer(index, depth);
            update_delta_buffer(len - 1, depth); // update delta buffer *Note update ordering is important, (if the end index & remove index are the same)

            _h_index_data_length[depth]--; // Decrement length for old depth level
        }
        int append_depth(in int new_depth, in int instance_id)
        {
            var len = _h_index_data_length[new_depth];
            var end_index = GetIndexData(len, new_depth); // Get index data at the end in a temp variable

            if (end_index.instance_id > 0)
                throw new System.Exception("Error, corrupted instance hierarchy index data");

            SetIndexData(len, new_depth, new index_data(instance_id, end_index.delta_buffer_index, end_index.in_delta_buffer)); // set new index data
            update_delta_buffer(len, new_depth); // update delta buffer for the index that changed

            _h_index_data_length[new_depth]++; // increment list length at new depth

            return len;
        }

        /// <summary>
        /// Add a change to the delta buffer
        /// </summary>
        /// <param name="index"></param>
        /// <param name="depth"></param>
        void update_delta_buffer(in int index, in int depth)
        {
            var i_data = GetIndexData(index, depth);
            if (i_data.in_delta_buffer) // If this item already exists in the delta buffer...
            {
                var delta = _delta_buffer.Array[i_data.delta_buffer_index];
                if (!delta.IsDirty())
                    throw new System.Exception("Error, hierarchy delta should be initialized.");
                if (delta.index != index || delta.depth != depth) // make sure a wrong overwrite isnt happening
                    throw new System.Exception("Error, tried to overwrite a different delta index in hierachy delta buffer");
                delta.instance_id = i_data.instance_id;
                _delta_buffer.Array[i_data.delta_buffer_index] = delta;
            }
            else
            {
                var delta = default(hierarchy_delta); // make a new delta
                delta.index = index;
                delta.depth = depth;
                delta.instance_id = i_data.instance_id;
                delta.dirty = 1;
                SetIndexData(index, depth, new index_data(i_data.instance_id, _delta_buffer.Count, in_delta: true)); // Update delta index for the index data  
                _delta_buffer.Add(delta);
            }
        }

        /// <summary>
        /// Update a compute buffer. Only MaxDeltaCount will be updated.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="test_buffer"></param>
        internal int UpdateBuffer(ComputeBuffer buffer, UnityEngine.Rendering.CommandBuffer cmd)
        {
            if (!_init)
                throw new System.Exception("Error, hierarchy update buffer not initialized!");

            int count = 0;

            if (buffer != null)
            {
                if (_delta_buffer.Count <= 0) // do nothing if empty
                    return count;

                count = CurrentDeltaCount;

                cmd.SetBufferData(buffer, this._delta_buffer.Array, 0, 0, this.CurrentDeltaCount);  //  buffer.SetData(_delta_buffer.Array, 0, 0, this.CurrentDeltaCount);

                if (_delta_buffer.Count <= MaxDeltaCount) // if delta array is less than max, just clear it
                {
                    ResetDirty(0, _delta_buffer.Count); // reset delta buffer
                    _delta_buffer.Clear();
                }
                else // otherwise all items should be copied forward
                {
                    ResetDirty(0, this.MaxDeltaCount); // reset delta buffer
                    this._delta_buffer.PopLeft(this.MaxDeltaCount);
                }
            }
            return count;
        }

        void ResetDirty(int start_index, int stop_index)
        {
            for (int i = start_index; i < stop_index; i++)
            {
                var delta = _delta_buffer.Array[i];
                if (!delta.IsDirty())
                    throw new System.Exception("Error, hierarchy reset dirty delta should be initialized!");
                var i_data = GetIndexData(delta.index, delta.depth);
                SetIndexData(delta.index, delta.depth, new index_data(i_data.instance_id, 0, in_delta: false));
            }
        }

        void add_child(in int instance_id, in int parent)
        {
            // this function manipulates the children array by adding the input instance as a child of the input parent
            if (!_child_array[parent].Initialized())
            {
                this._child_array[parent] = new ChildList(_array_pool.Get());
            }

            this._child_array[parent].AddChild(instance_id);
        }
        void remove_child(in int instance_id, in int parent)
        {
            var childList = this._child_array[parent];
            // This function manipulates the children array by removing the input instance from the input parent
            if (!childList.Initialized())
                throw new System.Exception("Error, tried to remove a child from a parent instance with no children in mesh instance hierarchy!");

            if (!childList.RemoveChild(instance_id))
                throw new System.Exception("Error tried to remove a child from a parent, but the parent did not have the same child instance to remove in entity hierarchy!");

            if (childList.Count == 0)
            {
                var childArray = childList.GetChildList();
                if (childArray.Array.Length < 16)
                {
                    this._array_pool.Add(childArray); // add array back to pool if it has a small capacity
                }
                this._child_array[parent] = default(ChildList); // set array to null since the parent no longer has children
            }
        }
        void update_children_depth(in int instance_id, in int instance_depth)
        {
            // This function recurssivly updates the depth of all children for the input instance id
            if (_child_array[instance_id].Initialized())
            {
                if (instance_depth + 1 >= this.MaxDepth)
                    throw new System.Exception("Error, encountered invalid depth for child in instance hierarchy");

                var new_depth = instance_depth + 1;

                var children = this._child_array[instance_id];
                foreach (var child_instance_id in children.Children)
                {
                    if (child_instance_id == instancemesh.NULL_ID || child_instance_id == instancemesh.INVALID_ID)
                        throw new System.Exception("Error, child instance id for depth update is corrupted!");

                    var hdata = this._h_map[child_instance_id];
                    if (hdata.depth == new_depth)
                        throw new System.Exception("Error, invalid update for children depth. The depth should be different");
                    if (!hdata.initialized || _h_index_data_length[hdata.depth] == 0)
                        throw new System.Exception("Error, invalid update state for children depth update routine.");

                    remove_depth(hdata.depth, hdata.index_map_id, child_instance_id); // update depth for child on the delta buffer
                    hdata.index_map_id = append_depth(new_depth, child_instance_id);

                    // Update hierarchy data for child
                    hdata.depth = new_depth;
                    this._h_map[child_instance_id] = hdata;

                    update_children_depth(child_instance_id, new_depth); // Try update child of child
                }
            }
        }

        /// <summary>
        /// Get children of instance id
        /// </summary>
        /// <param name="instance_id"></param>
        /// <param name="children"></param>
        /// <param name="recursive"></param>
        internal void GetChildren(in int instance_id, List<int> children, bool recursive = false)
        {
                this.get_children(instance_id, children, recursive);
        }
        void get_children(in int instance_id, List<int> children, bool recursive = false, int count = 0)
        {
            if (count > this.MaxDepth)
                throw new System.Exception("Error, exceeded max depth limit on hierarchy get_children function");

            if (_child_array[instance_id].Initialized())
            {
                var instance_children = this._child_array[instance_id];
                foreach (var child_instance_id in instance_children.Children)
                {
                    children.Add(child_instance_id);

                    if (recursive)
                    {
                        get_children(child_instance_id, children, recursive, count + 1);
                    }
                }
            }
        }

        /// <summary>
        /// Get parents of instance id
        /// </summary>
        /// <param name="instance_id"></param>
        /// <param name="parents"></param>
        /// <param name="recursive"></param>
        internal void GetParents(in int instance_id, List<int> parents, bool recursive = false, int count = 0)
        {
            this.get_parents(instance_id, parents, recursive, count);
        }
        void get_parents(in int instance_id, List<int> parents, bool recursive = false, int count = 0)
        {
            if (instance_id == instancemesh.NULL_ID || instance_id == instancemesh.INVALID_ID || instance_id < 0 || instance_id >= this.MaxInstancesPerDepth || !this._h_map[instance_id].initialized)
                throw new System.Exception("Error, input id does not exist");

            var hdata = this._h_map[instance_id];
            if (hdata.parent != instancemesh.NULL_ID && hdata.parent != instancemesh.INVALID_ID)
            {
                parents.Add(hdata.parent);

                if (recursive)
                {
                    if (count > this.MaxDepth)
                        throw new System.Exception("Error, infinite loop encountered in instance hierarchy GetParents()");

                    this.get_parents(hdata.parent, parents, recursive, count + 1);
                }
            }
        }

        /// <summary>
        /// Get hierarchy data for instace
        /// </summary>
        /// <param name="instance_id"></param>
        /// <returns></returns>
        internal hierarchy_data GetHierarchyData(in int instance_id)
        {
            return this._h_map[instance_id];
        }

#if UNITY_EDITOR
        internal static void test()
        {
            for (int i = 0; i < 100; i++)
                InstanceHierarchyMap.test(i);
            for (int i = 0; i < 100; i++)
                InstanceHierarchyMap.test(i, i + 1, i + 1, i + 1);

            for (int i = 0; i < 5; i++)
            {
                Debug.Log("Doing 1k unity transform test " + (i+1).ToString() + " / 5");
                test2(i);
            }

            Debug.Log("Doing 1k unity transform test many child" + (1).ToString() + " / 1");
            test2(1, true);
        }

        static void test(int seed, int inst_count = 100, int delta_count = 50, int depth = 5)
        {
            var r = new System.Random(seed);

            List<List<System.ValueTuple<int, int>>> MakeHierarchy(int nLevels, int nInstances)
            {
                var h = new List<List<System.ValueTuple<int, int>>>(nLevels);
                var inst_per_level = nInstances / nLevels;
                for (int i = 0; i < nLevels; i++)
                {
                    h.Add(new List<(int, int)>());
                    for (int j = 0; j < inst_per_level; j++)
                    {
                        if (j == inst_per_level - 1 && i == nLevels - 1)
                            break;

                        int id = i * inst_per_level + j + 1;
                        var parent_id = i == 0 ? instancemesh.NULL_ID : h[i - 1][r.Next(0, inst_per_level)].Item1;
                        parent_id = parent_id == id ? instancemesh.NULL_ID : parent_id;
                        h[i].Add((id, parent_id));
                    }
                }
                return h;
            }

            // Make & init hierarchy map
            int n = inst_count; int d = delta_count; int l = depth;
            var map = new InstanceHierarchyMap(1, d, l, 1);
            var hierarchy = MakeHierarchy(l, n);
            int count = 0;
            foreach (var list in hierarchy)
                foreach (var p in list)
                { map.SetParent(p.Item1, p.Item2); count++; }
            var hierarchy2 = MakeHierarchy(l, n);
            for (int i = 0; i < hierarchy2.Count; i++)
                for (int j = 0; j < hierarchy2[i].Count; j++)
                {
                    var p = hierarchy2[i][j];
                    if (p.Item2 != hierarchy[i][j].Item2)
                        map.SetParent(p.Item1, p.Item2);
                }

            // do some tests
            var index_map = new index_data[map.NumHierarchyIndices];
            var set = new HashSet<hierarchy_delta>(map._delta_buffer);
            if (set.Count != map._delta_buffer.Count)
                throw new System.Exception("Error, duplicate items on delta buffer");
            for (int i = 0; i < map._delta_buffer.Count; i++)
            {
                var delta = map._delta_buffer.Array[i];
                index_map[delta.depth * map.MaxInstancesPerDepth + delta.index] = new index_data(delta.instance_id, i, true);
            }
            for (int i = 0; i < map._h_index_data.Length; i++)
                if (index_map[i].delta_buffer_index != map._h_index_data[i].delta_buffer_index || index_map[i].instance_id != map._h_index_data[i].instance_id)
                    throw new System.Exception("Errrrror");
            var s1 = map._h_index_data_length.Sum();
            var s2 = hierarchy.Sum(x => x.Count);
            if (s1 != s2)
                throw new System.Exception("Error bad sum!");

            // clear delta & update
            map.ResetDirty(0, map._delta_buffer.Count);
            map._delta_buffer.Clear();
            if (map._h_index_data.Any(x => x.in_delta_buffer) || map._delta_buffer.Any(x => x.IsDirty()))
                throw new System.Exception("Error, failed to reset");
            for (int i = 0; i < map._h_index_data.Length; i++)
                index_map[i] = map._h_index_data[i];

            var hierarchy3 = MakeHierarchy(l, n);
            for (int i = 0; i < hierarchy3.Count; i++)
                for (int j = 0; j < hierarchy3[i].Count; j++)
                {
                    var p = hierarchy3[i][j];
                    if (p.Item2 != hierarchy2[i][j].Item2)
                        map.SetParent(p.Item1, p.Item2);
                }

            // do some tests
            set = new HashSet<hierarchy_delta>(map._delta_buffer);
            if (set.Count != map._delta_buffer.Count)
                throw new System.Exception("Error, duplicate items on delta buffer");
            for (int i = 0; i < map._delta_buffer.Count; i++)
            {
                var delta = map._delta_buffer.Array[i];
                index_map[delta.depth * map.MaxInstancesPerDepth + delta.index] = new index_data(delta.instance_id, i, true);
            }
            for (int i = 0; i < map._h_index_data.Length; i++)
                if (index_map[i].delta_buffer_index != map._h_index_data[i].delta_buffer_index || index_map[i].instance_id != map._h_index_data[i].instance_id)
                    throw new System.Exception("Errrrror");
            s1 = map._h_index_data_length.Sum();
            s2 = hierarchy.Sum(x => x.Count);
            if (s1 != s2)
                throw new System.Exception("Error bad sum!");

            // Reset & update
            map.ResetDirty(0, map._delta_buffer.Count);
            map._delta_buffer.Clear();
            if (map._h_index_data.Any(x => x.in_delta_buffer) || map._delta_buffer.Any(x => x.IsDirty()))
                throw new System.Exception("Error, failed to reset");
            for (int i = 0; i < map._h_index_data.Length; i++)
                index_map[i] = map._h_index_data[i];

        }

        static void test2(int seed, bool many_child_test=false)
        {
            int inst_count = 1000;
            int max_depth = 32;

            var r = new System.Random(seed);

            int depth_of_transform(Transform t, Transform target = null)
            {
                if (t == target) throw new System.Exception("Error, t should not be equal to target");
                int depth = 0;
                Transform current = t;
                while (current.parent != target)
                {
                    if (depth > max_depth) // sanity check for infinite loop
                        throw new System.Exception("Test error, passed max depth somehow.");

                    depth++;
                    current = current.parent;
                }
                return depth;
            }

            int child_depth_of_transform(Transform t)
            { // find child depth of input transform
                int max_child_depth = 0;
                var children = t.GetComponentsInChildren<Transform>();
                if (!ReferenceEquals(null, children))
                {
                    for (int i = 0; i < children.Length; i++)
                    {
                        if (children[i] != t) // dont invoke on self
                        {
                            max_child_depth = System.Math.Max(max_child_depth, depth_of_transform(children[i], t));
                        }
                    }
                }
                else
                {
                    return depth_of_transform(t);
                }
                return max_child_depth;
            }

            List<Transform> make_objs()
            {
                List<Transform> objs = new List<Transform>(inst_count);
                for (int i = 0; i < inst_count; i++)
                {
                    objs.Add(new GameObject((i+1).ToString()).transform);
                }
                return objs;
            }

            void mutate(List<Transform> objs, InstanceHierarchyMap map, bool init)
            {
                if (init)
                {
                    // Initialize all positions to null first
                    for (int i = 0; i < objs.Count; i++)
                    {
                        var current = objs[i]; // get current transform
                        map.SetParent(System.Int32.Parse(current.name), instancemesh.NULL_ID);
                    }
                }

                for (int i = objs.Count - 1; i >= 0; i--)
                {
                    if (!init)
                    {
                        var current = objs[i]; // get current transform
                        var rand_delete = r.Next(0, map.MaxInstancesPerDepth) == 1;
                        if (rand_delete)
                        {
                            // First, deparent all children of the object to be deleted
                            var children_arr = current.GetComponentsInChildren<Transform>();
                            var children = children_arr == null ? new List<Transform>() : children_arr.Where(x => x != current).ToList();
                            foreach (var c in children)
                                if (c.parent == current)
                                    c.SetParent(null);

                            map.Delete(System.Int32.Parse(current.name));
                            objs.RemoveAt(i);
                            MonoBehaviour.DestroyImmediate(current.gameObject);
                        }
                    }
                }

                for (int i = 0; i < objs.Count; i++)
                {
                    var index = many_child_test ? r.Next(0, 10) : r.Next(0, objs.Count);
                    var new_parent = objs[index];
                    var current = objs[i]; // get current transform
                    new_parent = current == new_parent ? null : new_parent; // set null if same
                    bool same_parent = current.parent == new_parent; // determine if the parent is different
                    var new_depth = child_depth_of_transform(current) + 1 + (new_parent == null ? 0 : depth_of_transform(new_parent)) + 1;
                    var new_parent_is_child_of_self = new_parent == null ? false : new_parent.IsChildOf(current);
                    if (!same_parent && !new_parent_is_child_of_self && new_depth < max_depth)
                    {
                        current.SetParent(new_parent);
                        map.SetParent(System.Int32.Parse(current.name), new_parent == null ? instancemesh.NULL_ID : System.Int32.Parse(new_parent.name));
                    }
                }
            }

            void assert_valid(List<Transform> objs, InstanceHierarchyMap map)
            {
                if (map._h_index_data_length.Sum() != objs.Count)
                    throw new System.Exception("Error, bad object count");
                List<int> children = new List<int>();
                List<int> parents = new List<int>();
                Dictionary<int, Transform> objs_dict = objs.ToDictionary(k => System.Int32.Parse(k.name), v => v);
                var live_objects = new HashSet<Transform>(objs);
                HashSet<int> all_ids_checked = new HashSet<int>();
                for (int depth = 0; depth < map.MaxDepth; depth++)
                {
                    for (int index = 0; index < map.MaxInstancesPerDepth; index++)
                    {
                        children.Clear();
                        parents.Clear();

                        var data = map.GetIndexData(index, depth);
                        if (index < map._h_index_data_length[depth]) // if checking a valid transform
                        {
                            map.GetChildren(data.instance_id, children, true);
                            map.GetParents(data.instance_id, parents, true);
                            var hdata = map.GetHierarchyData(data.instance_id);

                            var t = objs_dict[data.instance_id];
                            var t_children_array = t.GetComponentsInChildren<Transform>();
                            var t_parent_array = t.GetComponentsInParent<Transform>();
                            var t_children = t_children_array == null ? new List<int>() : new List<int>(t_children_array.Where(x => x != t).Select(x => System.Int32.Parse(x.name)));
                            var t_parents = t_parent_array == null ? new List<int>() : new List<int>(t_parent_array.Where(x => x != t).Select(x => System.Int32.Parse(x.name)));
                            var t_parent_id = t.parent == null ? instancemesh.NULL_ID : System.Int32.Parse(t.parent.name);

                            if (t_parent_id != hdata.parent)
                                throw new System.Exception("Error, invalid parent found");
                            if (hdata.depth != depth_of_transform(t))
                                throw new System.Exception("Error, depths differ");
                            children.Sort(); parents.Sort(); t_children.Sort(); t_parents.Sort();
                            if (!parents.SequenceEqual(t_parents))
                                throw new System.Exception("Error, parent differ");
                            if (!children.SequenceEqual(t_children))
                            {
                                List<int> diff = new List<int>(t_children.Except(children));
                                throw new System.Exception("Error, element has different children");
                            }

                            if (all_ids_checked.Contains(data.instance_id))
                                throw new System.Exception("Checked an instance id twice. It should only exist at one point in the map!");

                            all_ids_checked.Add(data.instance_id);
                        }
                        else // otherwise it should be null
                        {
                            if (data.instance_id != 0 || data.in_delta_buffer != false || data.delta_buffer_index != 0)
                                throw new System.Exception("Error, map past length should be empty");
                        }
                    }
                }

                var obj_id_list = objs_dict.Select(x => x.Key).ToList();
                var data_id_list = all_ids_checked.ToList();
                obj_id_list.Sort();
                data_id_list.Sort();

                if (!obj_id_list.SequenceEqual(obj_id_list))
                    throw new System.Exception("Error, object list have different ids than what was checked in the hierarchy");
            }

            var transforms = make_objs();
            var hierarchy = new InstanceHierarchyMap(1, inst_count / 2 + 1, max_depth, 1);
            var index_map = new index_data[hierarchy.NumHierarchyIndices]; // simulated gpu map- we will push indices here using the delta buffer
            var index_map_inst_count = hierarchy.MaxInstancesPerDepth;

            void do_test_iter(bool init)
            {
                mutate(transforms, hierarchy, init: init); // make changes
                mutate(transforms, hierarchy, init: false); // make changes

                if (index_map.Length != hierarchy.NumHierarchyIndices) // resize index map
                {
                    var new_index_map = new index_data[hierarchy.NumHierarchyIndices];
                    for (int idepth = 0; idepth < hierarchy.MaxDepth; idepth++)
                    {
                        for (int iindex = 0; iindex < index_map_inst_count; iindex++)
                        {
                            new_index_map[idepth * hierarchy.MaxInstancesPerDepth + iindex] = index_map[idepth * index_map_inst_count + iindex];
                        }
                    }

                    index_map_inst_count = hierarchy.MaxInstancesPerDepth;
                    index_map = new_index_map;
                }

                // do some tests
                var set = new HashSet<hierarchy_delta>(hierarchy._delta_buffer);
                if (set.Count != hierarchy._delta_buffer.Count)
                    throw new System.Exception("Error, duplicate items on delta buffer");
                for (int j = 0; j < hierarchy._delta_buffer.Count; j++)
                {
                    var delta = hierarchy._delta_buffer.Array[j];
                    index_map[delta.depth * hierarchy.MaxInstancesPerDepth + delta.index] = new index_data(delta.instance_id, j, true);
                }
                for (int j = 0; j < hierarchy._h_index_data.Length; j++)
                    if (index_map[j].instance_id != hierarchy._h_index_data[j].instance_id)
                        throw new System.Exception("Error, the simulated gpu index map does not equal the hierarchy index map!");

                // clear delta
                hierarchy.ResetDirty(0, hierarchy._delta_buffer.Count);
                hierarchy._delta_buffer.Clear();
                if (hierarchy._h_index_data.Any(x => x.in_delta_buffer) || hierarchy._delta_buffer.Any(x => x.IsDirty()))
                    throw new System.Exception("Error, failed to reset");

                assert_valid(transforms, hierarchy); // assert state is valid
            }

            do_test_iter(init: true);

            for (int i = 0; i < 10; i++)
            {
                do_test_iter(init: false);
            }

            foreach (var obj in transforms)
                MonoBehaviour.Destroy(obj.gameObject);
        }
#endif
    }
}
