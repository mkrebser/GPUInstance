using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;

namespace GPUInstance
{
    /// <summary>
    /// Struct used to define how a GPU instance should move. It can follow a fixed path or use constant motion. Note* all motion occurs in local instance space- not world space.
    /// </summary>
    public struct Path
    {
        public int path_id;
        private int flags;
        /// <summary>
        /// Will path loop?
        /// </summary>
        public bool loop { get { return Bits.GetBit(this.flags, 0); } set { this.flags = Bits.SetBit(this.flags, 0, value); } }
        /// <summary>
        /// If true, path[0] = angular velocity, path[1] = velocity. path[2]=starting quaternion rotation* use path_t for 'w' component, path[3]=starting position. Note* constant motion paths should always have length=4
        /// </summary>
        public bool use_constants { get { return Bits.GetBit(this.flags, 1); } private set { this.flags = Bits.SetBit(this.flags, 1, value); } }
        /// <summary>
        /// Apply path averaging?
        /// </summary>
        public bool avg_path { get { return Bits.GetBit(this.flags, 2); } set { this.flags = Bits.SetBit(this.flags, 2, value); } }
        /// <summary>
        /// Apply path smoothing?
        /// </summary>
        public bool smoothing { get { return Bits.GetBit(this.flags, 3); } set { this.flags = Bits.SetBit(this.flags, 3, value); } }
        /// <summary>
        /// Force path direction to only rotate on global y-axis. Ie, direction.y forced to '0' in 'forward' transform direction.
        /// </summary>
        public bool yaw_only { get { return Bits.GetBit(this.flags, 4); } set { this.flags = Bits.SetBit(this.flags, 4, value); } }
        public bool look_at_camera { get { return Bits.GetBit(this.flags, 5); } set { this.flags = Bits.SetBit(this.flags, 5, value); } }
        /// <summary>
        /// seconds to finish entire path. Or* if use_constants=True, then this should be sent to the current Tick count in seconds.
        /// </summary>
        public float PathCompletionTime
        {
            get
            {
                return this._completion_time * 0.01f;
            }
            set
            {
                if (value < 0.02f || value > 10000f)
                    throw new System.Exception("Error invalid path completion time specified. Try a shorter or longer completion time please.");
                this._completion_time = value * 100;
            }
        }
        /// <summary>
        /// num ticks to finish path
        /// </summary>
        public uint PathCompletionTicks
        {
            get
            {
                const uint mult = Ticks.TicksPerSecond / 100;
                return ((uint)this._completion_time) * mult;
            }
        }
        public float RawPathCompletionTime
        {
            get
            {
                return this._completion_time;
            }
        }
        private float _completion_time; // store completion time multiplied by 100- again floating point issues etc

        /// <summary>
        /// Average weighting factor. Points using weighted addition are calculate by averaging a point with its immediate left & right neighbors as follows: (1-weight)*0.5*Point(-1) + weight*Point(0) + (1-weight)*0.5*Point(1).
        /// A weight of '0.3333' will just cause the points to be averaged. A weight of '1' will cause no averaging at all. A weight of '0' will only consider the neighboring points.
        /// </summary>
        public float AvgWeight { get; set; }
        /// <summary>
        /// Length (in points) of this particular path
        /// </summary>
        public float LengthFloat
        {
            get
            {
                return this._len;
            }
        }
        /// <summary>
        /// length in points of this path.
        /// </summary>
        public int Length { get { return (int)this._len; } }
        public void SetLength(in int length) { this._len = length + 0.1f; }
        float _len;

        /// <summary>
        /// set 'use_constants' field
        /// </summary>
        /// <param name="val"></param>
        /// <param name="m"></param>
        public void SetUseConstants(in bool val, in MeshInstancer m)
        {
            this.use_constants = val;

            if (val) // use completion time as start seconds for constant motion
            {
                this._completion_time = (float)(m.Ticks * Ticks.SecondsPerTick);

                if (this.Length != 4 && this.use_constants)
                    throw new System.Exception("Error, must have 4 points for use_constants=True");
            }
        }

        public float Flags2Float()
        {
            return (float)this.flags + 0.1f;
        }

        public bool Initialized() { return this.path_id > -1 && this.path_id != instancemesh.NULL_ID; }

        /// <summary>
        /// Create a new path object
        /// </summary>
        public Path(in int path_length, in MeshInstancer m=null, in bool loop = false, in bool use_constants = false, in bool avg_path = false, in float path_time = 1.0f, in float avg_weight = 0.5f, in bool smoothing = false, in bool yaw_only = false, bool look_at_camera = false)
        {
            this.flags = 0;
            this._completion_time = path_time * 100;
            this.AvgWeight = avg_weight;
            this.path_id = instancemesh.NULL_ID;
            this._len = path_length + 0.1f;

            this.loop = loop;
            this.use_constants = use_constants;
            this.avg_path = avg_path;
            this.smoothing = smoothing;
            this.yaw_only = yaw_only;
            this.look_at_camera = look_at_camera;

            if (use_constants && !ReferenceEquals(null, m))
            {
                this._completion_time = (float)(m.Ticks * Ticks.SecondsPerTick);
            }
            if (path_length != 4 && use_constants)
                throw new System.Exception("Error, must have 4 points for use_constants=True");
        }
    }

    public static class Pathing
    {
        /// <summary>
        /// Interpolate along an array of 3D points.
        /// </summary>
        /// <param name="points">Array of points. </param>
        /// <param name="index">Index of point that will be interpolated around. </param>
        /// <param name="t"> Interoplation parameter 't' for the desired point and the next point in the array. </param>
        /// <param name="position"> output interpolated position. </param>
        /// <param name="direction"> output interpolated direction. </param>
        /// <param name="loop"> Does this array of points create a loop? </param>
        /// <param name="average"> Do point neighborhood averaging? </param>
        /// <param name="avg_weight"> Weighting factor used for point averaging. </param>
        /// <param name="smoothing"> Enable catmull rom spline for the array of points? </param>
        /// <param name="start_index"> Start index in the input array of points to use. </param>
        /// <param name="count"> Count of points to use. A value of '-1' will just default to the input array length. </param>
        public static void InterpolatePoints(in Vector3[] points, in int index, in float t, out Vector3 position, out Vector3 direction, in bool loop = false, bool average = true, in float avg_weight = 0.5f, in bool smoothing = true, in int start_index = 0, int count = -1)
        {
            // This function mimics GPU behaviour- do not modify 
            // reference guide for catmull rom spline https://www.mvps.org/directx/articles/catmull/

            count = count < 0 ? points.Length : count;

            if (count < 2)
                throw new System.Exception("Error, must use atleast 2 points!");

            int index_p0 = index - 1 < 0 ? (loop ? count - 1 : index) : index - 1; // indices of 4 points needed for catmull rom
            int index_p1 = index;
            int index_p2 = index + 1 >= count ? (loop ? 0 : index) : index + 1;
            int index_p3 = index_p2 + 1 >= count ? (loop ? 0 : index_p2) : index_p2 + 1;

            int index_p0m1 = index_p0 - 1 < 0 ? (loop ? count - 1 : index_p0) : index_p0 - 1; // extra indices used for the weighted sum
            int index_p3p1 = index_p3 + 1 >= count ? (loop ? 0 : index_p3) : index_p3 + 1;

            float avg_weight2 = (1 - avg_weight) * 0.5f; // 'Averaging', if enabled, does a weighted sum of the desired point and its left & right neighbors.
            bool move_pt = average && (loop || (index != 0));
            bool move_next_pt = average && (loop || index_p2 < count - 1);
            Vector3 p0 = average ? (points[start_index + index_p0m1] + points[start_index + index_p1]) * avg_weight2 + points[start_index + index_p0] * avg_weight : points[start_index + index_p0];
            Vector3 p1 = average && move_pt ? (points[start_index + index_p0] + points[start_index + index_p2]) * avg_weight2 + points[start_index + index_p1] * avg_weight : points[start_index + index_p1];
            Vector3 p2 = average && move_next_pt ? (points[start_index + index_p1] + points[start_index + index_p3]) * avg_weight2 + points[start_index + index_p2] * avg_weight : points[start_index + index_p2];
            Vector3 p3 = average ? (points[start_index + index_p2] + points[start_index + index_p3p1]) * avg_weight2 + points[start_index + index_p3] * avg_weight : points[start_index + index_p3];

            Vector3 a0 = 2 * p1;
            Vector3 a1 = p2 - p0;
            Vector3 a2 = 2 * p0 - 5 * p1 + 4 * p2 - p3;
            Vector3 a3 = 3 * p1 - 3 * p2 + p3 - p0;

            float t_2 = t * t;
            float t_3 = t_2 * t;

            position = smoothing ? 0.5f * (a0 + a1 * t + a2 * t_2 + a3 * t_3) : p1 * (1 - t) + p2 * t; // if smoothing-> do catmull rom, otherwise do simple interpolation
            direction = (smoothing ? 0.5f * (a1 + 2 * a2 * t + 3 * a3 * t_2) : p2 - p1).normalized; // if smoothing-> do derivative of catmull rom, otherwise just subtract
        }
        public static void InterpolateUp(in int index, in float t, in bool loop, out Vector3 up, in Vector3[] path_up, int start_index = 0, int count = -1)
        {
            // This function mimics GPU behaviour- do not modify 
            count = count < 0 ? path_up.Length : count;
            if (count < 2)
                throw new System.Exception("Error, must use atleast 2 points!");

            int index_p0 = index;
            int index_p1 = index + 1 >= count ? (loop ? 0 : count - 1) : index + 1;

            Vector3 p0 = Pathing.UnpackDirection(Pathing.PackDirection(path_up[start_index + index_p0])); // directions are packed into a single float... need to unpack & pack to simulate what happens on gpu
            Vector3 p1 = Pathing.UnpackDirection(Pathing.PackDirection(path_up[start_index + index_p1]));

            up = Vector3.Normalize((1 - t) * p0 + t * p1);
        }

        /// <summary>
        /// Calculate position & direction of a path using some points & path object at a interpolation time 't'
        /// </summary>
        /// <param name="p"></param>
        /// <param name="path"></param>
        /// <param name="t"></param>
        /// <param name="position"></param>
        /// <param name="direction"></param>
        /// <param name="start_index"></param>
        /// <param name="path_t"></param>
        public static void CalculatePathPositionDirection(in Path p, in Vector3[] path, float t, out Vector3 position, out Vector3 direction, out Vector3 up, Vector3[] path_up, in float[] path_t, in int start_index = 0)
        {
            // This function mimics GPU behaviour- do not modify 

            if (p.use_constants)
            {
                throw new System.Exception("Error, function cannot compute constant motion!"); // needs a start tick
            }
            if (p.look_at_camera)
            {
                throw new System.Exception("Error, function cannot compute look at camera without access to camera");
            }

            t = t <= 0 ? 0.0000001f : (t >= 1 ? 0.9999999f : t);

            int index; float ti;
            CalcIndexAndT(p, t, path_t, out index, out ti, start_index);
            int len = p.Length;

            InterpolatePoints(points: path, index: index, t: ti, out position, out direction, loop: p.loop, average: p.avg_path, avg_weight: p.AvgWeight, smoothing: p.smoothing, start_index: start_index, count: len);

            up = ReferenceEquals(null, path_up) ? Vector3.up : path_up[index];
            InterpolateUp(index: index, t: ti, loop: p.loop, out up, path_up: path_up, start_index: start_index, count: len);

            // calc directions
            direction = p.yaw_only ? new Vector3(direction.x, 0, direction.z).normalized : direction;
            up = p.yaw_only ? Vector3.up : up;
            Vector3 xaxis = Vector3.Normalize(Vector3.Cross(up, direction));
            up = Vector3.Cross(direction, xaxis).normalized;
        }

        /// <summary>
        /// Calculate the position & direction of an instance on a path
        /// </summary>
        /// <param name="p">path object</param>
        /// <param name="path">actual path of points</param>
        /// <param name="instance">instance to query</param>
        /// <param name="m">mesh instancer object for the instance</param>
        /// <param name="position">output position</param>
        /// <param name="direction">output direction</param>
        /// <param name="start_index">start index in the input path array</param>
        /// <param name="path_t">pre-calculated path_t array to save time</param>
        /// <param name="path_up"> path up directions </param>
        public static void CalculatePathPositionDirection(in Path p, in Vector3[] path, in InstanceData<InstanceProperties> instance, in MeshInstancer m, out Vector3 position, out Vector3 direction, out Vector3 up, Vector3[] path_up, in float[] path_t, in int start_index = 0)
        {
            // This function mimics GPU behaviour- do not modify 

            if (p.use_constants)
            {
                CalcConstantPathPositionAndDirection(p, path, instance.props_pathStartTick, m.Ticks, out position, out direction, out up, path_up, path_t, start_index);
                return;
            }
            if (p.look_at_camera && ReferenceEquals(null, m.FrustumCamera))
            {
                throw new System.Exception("Error, the look at camera is null!");
            }

            float t = CalculatePathTime(p, instance.props_pathInstanceTicks, instance.props_pathStartTick, m.Ticks, instance.props_pathSpeedRaw);
            t = t <= 0 ? 0.0000001f : (t >= 1 ? 0.9999999f : t);

            int index; float ti;
            CalcIndexAndT(p, t, path_t, out index, out ti, start_index);
            int len = p.Length;

            InterpolatePoints(points: path, index: index, t: ti, out position, out direction, loop: p.loop, average: p.avg_path, avg_weight: p.AvgWeight, smoothing: p.smoothing, start_index: start_index, count: len);

            up = ReferenceEquals(null, path_up) ? Vector3.up : path_up[index];
            InterpolateUp(index: index, t: ti, loop: p.loop, out up, path_up: path_up, start_index: start_index, count: len);

            direction = p.look_at_camera ? -m.FrustumCamera.transform.forward : direction;
            up = p.look_at_camera ? m.FrustumCamera.transform.up : up;

            // calc directions
            direction = p.yaw_only ? new Vector3(direction.x, 0, direction.z).normalized : direction;
            up = p.yaw_only ? Vector3.up : up;
            Vector3 xaxis = Vector3.Normalize(Vector3.Cross(up, direction));
            up = Vector3.Cross(direction, xaxis).normalized;
        }

        static void CalcConstantPathPositionAndDirection(in Path p, in Vector3[] path, in ulong instance_ticks, in ulong Ticks, out Vector3 position, out Vector3 direction, out Vector3 up, Vector3[] path_up, in float[] path_t, in int start_index = 0)
        {
            var velocity = path[start_index + 1];
            var ang_velocity = path[start_index];
            var start_position = path[start_index + 3];
            var start_rotation = new Quaternion(path[start_index + 2].x, path[start_index + 2].y, path[start_index + 2].z, path_t[start_index + 2]);

            var dt = (float)(Ticks * GPUInstance.Ticks.SecondsPerTick) - (float)(instance_ticks * GPUInstance.Ticks.SecondsPerTick);

            position = start_position + dt * velocity;
            var rotation = ApplyAngularVelocity(start_rotation, ang_velocity, dt);

            direction = rotation * Vector3.forward;
            up = rotation * Vector3.up;
        }

        /// <summary>
        /// Calculate specific index & segment interpolation paramater 't' for a path given a whole-path interpolation parameter 't'.
        /// </summary>
        public static void CalcIndexAndT(in Path p, in float t_global, in float[] path_t, out int index, out float t, in int start_index = 0)
        {
            // This function mimics GPU behaviour- do not modify 
            int plen = (int)p.Length; // path length assumed to always be >= 2
            index = 0;
            for (int i = 0; i < plen; i++) // uniform global for loop, PATH_COUNT assumed always>= 2
            {
                index = path_t[i + start_index] >= t_global ? index : i;
            }
            float next_t_val = index + 1 >= plen ? 1.0f : path_t[start_index + index + 1];
            t = (t_global - path_t[start_index + index]) / (next_t_val - path_t[start_index + index]);
        }

        /// <summary>
        /// Retrieve point from a path with averaging
        /// </summary>
        public static Vector3 GetPoint(in Path p, in Vector3[] path, in int local_index, in int start_index)
        {
            int count = p.Length;
            if (p.avg_path)
            {
                int index_p0 = local_index - 1 < 0 ? (p.loop ? count - 1 : local_index) : local_index - 1; // indices of 4 points needed for catmull rom
                int index_p1 = local_index;
                int index_p2 = local_index + 1 >= count ? (p.loop ? 0 : local_index) : local_index + 1;
                float w2 = (1 - p.AvgWeight) * 0.5f;

                bool move_pt = p.loop || (local_index != 0) || local_index != count - 1;

                return move_pt ? (path[start_index + index_p0] * w2 + path[start_index + index_p1] * p.AvgWeight + path[start_index + index_p2] * w2) : path[start_index + local_index];
            }
            else
            {
                return path[start_index + local_index];
            }
        }

        static float WeightedSumAndDist(in Path p, in Vector3[] path, Vector3[] tmp, float[] tmp2, in int start_index)
        {
            // This function will calculate the total distance in the path.. Putting cumulative path distances in 'tmp2'
            // It will also put averaged path points into 'tmp'

            int plen = p.Length;
            float total_distance = 0;
            float w2 = (1 - p.AvgWeight) * 0.5f;
            float w = p.AvgWeight;

            tmp2[0] = 0;

            if (p.avg_path)
            {
                if (plen == 2)
                    throw new System.Exception("Error, cannot average paths that only have 2 points!");

                tmp[0] = p.loop ? (path[start_index + plen - 1] + path[start_index + 1]) * w2 + path[start_index + 0] * w : path[start_index + 0];
                for (int i = 1; i < plen - 1; i++)
                {
                    tmp[i] = (path[start_index + i - 1] + path[start_index + i + 1]) * w2 + path[start_index + i] * w;

                    total_distance += (tmp[i] - tmp[i - 1]).magnitude;
                    tmp2[i] = total_distance;
                }
                tmp[plen - 1] = p.loop ? (path[start_index + plen - 2] + path[start_index + 0]) * w2 + path[start_index + plen - 1] * w : path[start_index + plen - 1];

                total_distance += (tmp[plen - 1] - tmp[plen - 2]).magnitude;
                tmp2[plen - 1] = total_distance;
            }
            else
            {
                tmp[0] = path[start_index + 0];
                for (int i = 1; i < plen; i++)
                {
                    tmp[i] = path[start_index + i];
                    total_distance += (tmp[i] - tmp[i - 1]).magnitude;
                    tmp2[i] = total_distance;
                }
            }

            if (p.loop)
                total_distance += (tmp[0] - tmp[plen - 1]).magnitude;

            return total_distance;
        }

        /// <summary>
        /// Calculate path_t array. Result is written to the input array
        /// </summary>
        /// <param name="p"></param>
        /// <param name="path"></param>
        /// <param name="path_t"> Each element in path_t represents that amount of time it takes (0...1) to reach that point in the path. This means that path_t is a cumulative distribution. </param>
        /// <param name="start_index"></param>
        /// <param name="tmp"> temporary buffer needed for computation. Should have length == Path.Length</param>
        /// <param name="tmp2"> temporary buffer needed for compuatation. Should have length == Path.length</param>
        /// <param name="path_t_start_index"></param>
        public static void CalcPathT(in Path p, in Vector3[] path, float[] path_t, Vector3[] tmp, float[] tmp2, in int start_index = 0, in int path_t_start_index = 0)
        {
            // This array is required because we can't just index into the path point array.. Each point has varying distances to the rest...
            // And it is too restrictive to enfore equidistant points (which would allow for just indexing into point arrays & none of this extra calculation)

            int plen = p.Length;
            if (p.use_constants) // just return... not used!
            {
                return;
            }

            float total_distance = WeightedSumAndDist(p, path, tmp, tmp2, start_index);
            float dist_inv = 1.0f / total_distance;

            // calculate cumulative contribution of each point to the total distance
            for (int i = 0; i < plen; i++)
            {
                path_t[path_t_start_index + i] = tmp2[i] * dist_inv;

                if (float.IsNaN(path_t[start_index + i]) || float.IsInfinity(path_t[start_index + i]))
                    throw new System.Exception("Error, path time was calculated to be invalid. Please you a well defined path");
            }
        }

        /// <summary>
        /// Calculate whole-path interpolation parameter 't' for an instance
        /// </summary>
        /// <param name="p"></param>
        /// <param name="pathInstanceTicks"></param>
        /// <param name="path_start_tick"></param>
        /// <param name="Ticks"></param>
        /// <param name="anim_speed"></param>
        /// <returns></returns>
        public static float CalculatePathTime(in Path p, in uint pathInstanceTicks, in ulong path_start_tick, in ulong Ticks, in uint anim_speed)
        {
            // This function mimics GPU behaviour- do not modify 

            // get some needed properties
            uint clip_tick_len = p.PathCompletionTicks;
            bool loop = p.loop;

            // adjust elapsed time by animation speed
            ulong elapsed_ticks = ((Ticks - path_start_tick) * anim_speed) / 10; // get elapsed ticks (CurrentTick - Tick when anim what set). Than adjust it by the animation speed.
            elapsed_ticks += pathInstanceTicks; // Add the animation time offset

            // calc current tick
            var anim_tick = loop ? elapsed_ticks % clip_tick_len : (elapsed_ticks >= clip_tick_len ? clip_tick_len - 1 : elapsed_ticks % clip_tick_len);
            return (float)anim_tick / (float)clip_tick_len;
        }

        static readonly Vector3 almost_up = new Vector3(0.05f, 0.95f, 0).normalized;
        /// <summary>
        /// Automatically calculate the 'up' direction for every point in a path. Specifically this function automatically corrects paths that have segments that are colinear with Vector3.up.
        /// </summary>
        /// <param name="p">Input path object. </param>
        /// <param name="path"> Path of 3D points. </param>
        /// <param name="path_up"> Array that the result will be written to. </param>
        /// <param name="start_index"> start index in the 'path' and 'path_up' arrays to write/read from. </param>
        public static void CalcPathUp(in Path p, in Vector3[] path, Vector3[] path_up, int start_index = 0)
        {
            Vector3 up = Vector3.up;

            bool is_colinear_up(in Vector3 testdir, in float e = 0.001f)
            {
                return Mathf.Abs(testdir.x) < e && Mathf.Abs(testdir.z) < e;
            }
            bool update_path_up(in int index, in Vector3 dir, in Vector3 prev_dir, in bool prev_colinear_up, in Vector3 up_direction)
            {
                if (is_colinear_up(dir))
                {
                    if (prev_colinear_up) // if the previous was also colinear.. just accept whatever up direction it used
                    {
                        path_up[start_index + index] = path_up[start_index + index - 1];
                    }
                    else // otherwise.. base the 'up' direction on the previous segment direction
                    {
                        path_up[start_index + index] = new Vector3(-prev_dir.x, 0, -prev_dir.z).normalized;
                    }

                    return true; // return true if colinear
                }
                else
                {
                    path_up[start_index + index] = up_direction;
                    return false;
                }
            }

            // first is always just the input reference up value
            path_up[start_index + 0] = is_colinear_up(path[start_index + 1] - path[start_index + 0]) ? almost_up : up;

            // Handle all but first and last 
            int plen = p.Length;
            bool previous_colinear_up = false;
            Vector3 previous_direction = path[start_index + 1] - path[start_index + 0];
            for (int i = 1; i < plen - 1; i++)
            {
                var direction = (path[start_index + i + 1] - path[start_index + i]).normalized;
                previous_colinear_up = update_path_up(i, direction, previous_direction, previous_colinear_up, up);
                previous_direction = direction;
            }

            // Handle last item
            if (p.loop)
            {
                Vector3 direction = path[start_index + 0] - path[start_index + plen - 1];
                update_path_up(plen - 1, direction, previous_direction, previous_colinear_up, up);
            }
            else
            {
                path_up[start_index + plen - 1] = path_up[start_index + plen - 2]; // just copy previous if not looping
            }
        }

        /// <summary>
        /// Calculate path_t and path_up automatically.
        /// </summary>
        /// <param name="p"> input path object to use </param>
        /// <param name="path"> path to use </param>
        /// <param name="path_up"> array for path_up result </param>
        /// <param name="path_t"> array for path_t result </param>
        /// <param name="tmp"> temp array needed for calculation </param>
        /// <param name="tmp2"> temp array needed for calculation </param>
        /// <param name="angle_up_modifier"> modifier for increasing how much an instance will roll while following a path. </param>
        /// <param name="start_index"> start index in path, path_up, and path_t arrays </param>
        public static void CalcPathUpAndT(in Path p, in Vector3[] path, Vector3[] path_up, float[] path_t, Vector3[] tmp, float[] tmp2, float angle_up_modifier = 0.0f, int start_index = 0)
        {
            bool is_colinear_up(in Vector3 testdir, in float e = 0.001f)
            {
                return Mathf.Abs(testdir.x) < e && Mathf.Abs(testdir.z) < e;
            }

            int plen = p.Length;
            if (p.use_constants)
            {
                throw new System.Exception("Error, path uses constants!");
            }

            float total_distance = WeightedSumAndDist(p, path, tmp, tmp2, start_index);
            float dist_inv = 1.0f / total_distance;

            // calculate cumulative contribution of each point to the total distance
            for (int i = 0; i < plen; i++)
            {
                Vector3 up = Vector3.up;
                var dir_fwd = i >= plen - 1 ? (p.loop ? tmp[0] - tmp[i] : tmp[i] - tmp[i - 1]) : tmp[i + 1] - tmp[i]; // fwd direction

                if (angle_up_modifier > 0.0001f)
                {
                    // Get backward directions for a point
                    var dir_bck = i - 1 < 0 ? (p.loop ? tmp[plen - 1] - tmp[i] : tmp[i] - tmp[i + 1]) : tmp[i - 1] - tmp[i];

                    var fwd2d = new Vector2(dir_fwd.x, dir_fwd.z); // convert to 2d (we are only concerned with rotations about y-axis)
                    var bck2d = new Vector2(dir_bck.x, dir_bck.z);

                    var angle = Mathf.Acos(Vector2.Dot(fwd2d, bck2d) / (fwd2d.magnitude * bck2d.magnitude)); // get rotation of the directions about y-axis
                    var mid = (fwd2d.normalized + bck2d.normalized).normalized; // get middle point between them

                    const float inv_pi = 1.0f / Mathf.PI;
                    up = Vector3.Lerp(Vector3.up, new Vector3(mid.x, 0, mid.y), Mathf.Clamp01((float.IsNaN(angle) ? 0 : 1.0f - angle * inv_pi) * angle_up_modifier)).normalized; // lerp between up & mid by 'angle' to create rolling while turning effect
                }

                path_t[start_index + i] = tmp2[i] * dist_inv;
                path_up[start_index + i] = is_colinear_up(dir_fwd) ? (i <= 0 ? almost_up : path_up[start_index + i - 1]) : up;

                if (float.IsNaN(path_t[start_index + i]) || float.IsInfinity(path_t[start_index + i]))
                    throw new System.Exception("Error, path time was calculated to be invalid. Please you a well defined path");
            }
        }

        public static void DebugDrawPath(in Path p, in Vector3[] path, in Vector3[] path_up = null, in float[] path_t = null, in int start_index = 0, int n_samples = 100, float line_scale = 0.25f)
        {
            // Draw the entire path
            Vector3 position, direction, up;
            for (int i = 0; i < n_samples; i++)
            {
                float t = i / (float)n_samples;
                Pathing.CalculatePathPositionDirection(p, path, t, out position, out direction, out up, path_up, path_t, start_index);
                Debug.DrawLine(position, position + direction * line_scale, Color.blue);
                Debug.DrawLine(position, position + up * line_scale, Color.green);
                Debug.DrawLine(position, position + Vector3.Cross(up, direction).normalized * line_scale, Color.red);
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        struct floatint
        {
            [FieldOffset(0)]
            public int data_int;
            [FieldOffset(0)]
            public float data_float;
        }

        /// <summary>
        /// Pack float direction into single float. Input vector assumed to be normalized. It wont work properly at all if input vector is not normalized!
        /// </summary>
        /// <param name="v"></param>
        /// <returns></returns>
        public static float PackDirection(in Vector3 v)
        {
            floatint f2i;
            f2i.data_float = 0;
            f2i.data_int = ((int)(v.x * 255) << 16) | ((int)(v.y * 255) << 8) | ((int)(v.z * 255) << 0);
            f2i.data_int = Bits.SetBit(f2i.data_int, 24, v.x < 0);
            f2i.data_int = Bits.SetBit(f2i.data_int, 25, v.y < 0);
            f2i.data_int = Bits.SetBit(f2i.data_int, 26, v.z < 0);

            return f2i.data_float;
        }
        public static Vector3 UnpackDirection(in float f)
        {
            floatint f2i;
            f2i.data_int = 0;
            f2i.data_float = f;

            Vector3 v;
            v.x = ((f2i.data_int >> 16) & 255) * 0.00392156862f * (Bits.GetBit(f2i.data_int, 24) ? -1 : 1);
            v.y = ((f2i.data_int >> 8) & 255) * 0.00392156862f * (Bits.GetBit(f2i.data_int, 25) ? -1 : 1);
            v.z = ((f2i.data_int >> 0) & 255) * 0.00392156862f * (Bits.GetBit(f2i.data_int, 26) ? -1 : 1);

            return v;
        }

        // Math helper methods to replicate gpu calculation for angular velocity
        static bool fequal(float value, float compare)
        {
            return Mathf.Abs(value - compare) < 0.00001f;
        }
        static Vector4 mulv4(in Vector4 a, in Vector4 b)
        {
            return new Vector4(a.x * b.x, a.y * b.y, a.z * b.z, a.w * b.w);
        }
        static void sincos(in Vector3 v, out Vector3 s, out Vector3 c)
        {
            s = new Vector3(Mathf.Sin(v.x), Mathf.Sin(v.y), Mathf.Sin(v.z));
            c = new Vector3(Mathf.Cos(v.x), Mathf.Cos(v.y), Mathf.Cos(v.z));
        }
        static Vector4 MulQuaternion(in Vector4 a, in Vector4 b)
        {
            var a_wwww = new Vector4(a.w, a.w, a.w, a.w);
            var a_xyzx = new Vector4(a.x, a.y, a.z, a.x);
            var b_wwwx = new Vector4(b.w, b.w, b.w, b.x);
            var a_yzxy = new Vector4(a.y, a.z, a.x, a.y);
            var b_zxyy = new Vector4(b.z, b.x, b.y, b.y);
            var a_zxyz = new Vector4(a.z, a.x, a.y, a.z);
            var b_yzxz = new Vector4(b.y, b.z, b.x, b.z);
            return mulv4(a_wwww, b) + mulv4((mulv4(a_xyzx, b_wwwx) + mulv4(a_yzxy, b_zxyy)), new Vector4(1.0f, 1.0f, 1.0f, -1.0f)) - mulv4(a_zxyz, b_yzxz);
        }
        static Vector4 QuaternionFromEuler(in Vector3 xyz) // note* zxy ordering
        {
            Vector3 s, c;    
            sincos(0.5f * xyz, out s, out c);
            var c_yxxy = new Vector4(c.y, c.x, c.x, c.y);
            var c_zzyz = new Vector4(c.z, c.z, c.y, c.z);
            var s_yxxy = new Vector4(s.y, s.x, s.x, s.y);
            var s_zzyz = new Vector4(s.z, s.z, s.y, s.z);
            return mulv4(mulv4(new Vector4(s.x, s.y, s.z, c.x), c_yxxy), c_zzyz) + mulv4(mulv4(mulv4(s_yxxy, s_zzyz), new Vector4(c.x, c.y, c.z, s.x)), new Vector4(1.0f, -1.0f, -1.0f, 1.0f));
        }
        static Quaternion ApplyAngularVelocity(Quaternion q, Vector3 w, float dt) // note* zxy ordering
        {
            if (fequal(dt, 0)) //this is okay because for all threads dt will be '0' or not be '0' (they will all always take the same code path)
                return q;
            Vector4 qw = QuaternionFromEuler(w * dt);
            var result = MulQuaternion(new Vector4(q.x, q.y, q.z, q.w), qw);
            return new Quaternion(result.x, result.y, result.z, result.w);
        }

#if UNITY_EDITOR
        public static void test(MeshInstancer imesher, bool smoothing, bool loop, float avg_weight, bool avg)
        {
            //Vector3[] points = new Vector3[] { new Vector3(0, 0, 0), new Vector3(-1, 0, 0), new Vector3(-1, -1, 0), new Vector3(-2, -1, 0), new Vector3(-2, -2, 0), new Vector3(-3, -2, 0), new Vector3(-3, -3, 0), new Vector3(-4, -3, 0) };
            Vector3[] points1 = new Vector3[] { new Vector3(0, 0, 0), new Vector3(-1, 0, 0), new Vector3(-1, 0, -1), new Vector3(-2, 0, -1), new Vector3(-2, 0, -2), new Vector3(-3, 0, -2), new Vector3(-5, 0, -2), new Vector3(-6, 0, -3) };
            Vector3[] points2 = new Vector3[20];

            var r = new System.Random(0);
            points2[0] = new Vector3(3, 0, 3);
            for (int i = 1; i < points2.Length; i++)
            {
                points2[i] = points2[i - 1] + new Vector3((float)r.NextDouble(), (float)r.NextDouble() * -0.3f, (float)r.NextDouble());
            }

            Vector3[] points3 = new Vector3[] { new Vector3(1, 0, 1), new Vector3(1, 0, 2) };

            T[] concat<T>(T[] a, T[] b)
            {
                T[] c = new T[a.Length + b.Length];
                a.CopyTo(c, 0);
                b.CopyTo(c, a.Length);
                return c;
            }

            int start_index = 6;
            Vector3[] pad = new Vector3[start_index];
            points1 = concat(pad, points1);
            points2 = concat(pad, points2);
            points3 = concat(pad, points3);

            void do_test(Vector3[] points)
            {
                int n = points.Length * 25;
                Path p = new Path(points.Length - pad.Length, null, avg_path: avg, avg_weight: avg_weight, smoothing: smoothing, loop: loop);
                InstanceData<InstanceProperties> ip = new InstanceData<InstanceProperties>(imesher.Default);
                ip.SetPath(p, imesher, 0);

                float[] path_t = new float[points.Length];
                Vector3[] path_up = new Vector3[points.Length];
                float[] tmp2 = new float[points.Length];
                Pathing.CalcPathT(p, points, path_t, path_up, tmp2);
                Pathing.CalcPathUp(p, points, path_up);

                p.smoothing = false;
                p.avg_path = false;
                for (int i = 0; i < n; i++)
                {
                    Vector3 pos;
                    Vector3 dir;
                    Vector3 up;
                    Pathing.CalculatePathPositionDirection(p: p, path: points, i / (float)n, out pos, out dir, out up, path_up, path_t, start_index: start_index);
                    Debug.DrawLine(pos, pos + dir.normalized * 0.05f, Color.white);
                }
                p.smoothing = smoothing;
                p.avg_path = avg;
                for (int i = 0; i < n; i++)
                {
                    Vector3 pos;
                    Vector3 dir;
                    Vector3 up;
                    Pathing.CalculatePathPositionDirection(p: p, path: points, i / (float)n, out pos, out dir, out up, path_up, path_t, start_index: start_index);
                    Debug.DrawLine(pos, pos + dir.normalized * 0.05f, Color.green);
                }
            }

            do_test(points1);
            do_test(points2);
            do_test(points3);
        }
#endif
    }

    /// <summary>
    /// Helper class for managing the creation & modifications of path arrays! This object is not thread safe!
    /// </summary>
    public class PathArrayHelper
    {
        /// <summary>
        /// path array. Contains every allocated path array- all concatenated into a single array.
        /// </summary>
        public SimpleList<Vector3> path;
        /// <summary>
        /// path up array.  Contains every allocated path up direction array- all concatenated into a single array.
        /// </summary>
        public SimpleList<Vector3> path_up;
        /// <summary>
        /// path time array.  Contains every allocated path time array- all concatenated into a single array.
        /// </summary>
        public SimpleList<float> path_t;

        private AsynchPoolNoNew<Vector3[]> _tmp_pool;
        private AsynchPoolNoNew<float[]> _tmp2_pool;

        private MeshInstancer m;

        public PathArrayHelper(in MeshInstancer m, int InitialPathCapacity = 128)
        {
            if (!m.Initialized())
                throw new System.Exception("Error, mesh instancer should be initialized");
            if (InitialPathCapacity < 1)
                throw new System.Exception("Invalid input. Capacity must be positive");

            this.m = m;

            this._tmp_pool = new AsynchPoolNoNew<Vector3[]>(() => { return new Vector3[this.m.PathCount]; }, maxSize: 32);
            this._tmp2_pool = new AsynchPoolNoNew<float[]>(() => { return new float[this.m.PathCount]; }, maxSize: 32);

            this.path = new SimpleList<Vector3>(InitialPathCapacity * m.PathCount);
            this.path_up = new SimpleList<Vector3>(InitialPathCapacity * m.PathCount);
            this.path_t = new SimpleList<float>(InitialPathCapacity * m.PathCount);
        }

        void init_path(in Path p)
        {
            int start = p.path_id * this.m.PathCount;
            int stop = start + this.m.PathCount;

            if (stop > this.path.Capacity) // increase array size if needed
            {
                this.path.Resize(stop * 2);
                this.path_up.Resize(stop * 2);
                this.path_t.Resize(stop * 2);
            }

            float inv_len = 1.0f / p.Length;
            for (int i = start; i < stop; i++) // clear desired path arrays
            {
                this.path.Array[i] = default(Vector3);
                this.path_up.Array[i] = Vector3.up; // default to Vector3.up
                this.path_t.Array[i] = inv_len * (i - this.m.PathCount); // default to linear time
            }
        }

        /// <summary>
        /// Initialize path. Allocates space on path arrays and retrieves a new path ID. This will not actually send a path to the GPU. Use UpdatePath() after this to send the path to GPU
        /// </summary>
        /// <param name="p"></param>
        public void InitializePath(ref Path p)
        {
            if (p.Length <= 0 || p.Length > m.PathCount || p.Initialized())
                throw new System.Exception("Error, input path object is invalid!");

            p.path_id = m.AllocateNewPathID();
            init_path(p);
        }

        /// <summary>
        /// Start index of the input path for PathArrayHelper.path, PathArrayHelper.path_t, and PathArrayHelper.path_up
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        public int StartIndexOfPath(in Path p)
        {
            if (p.path_id <= 0)
                throw new System.Exception("Error, invalid path object");
            return p.path_id * this.m.PathCount;
        }

        /// <summary>
        /// Auto calculate path up and time. See Pathing.CalcPathUpAndT
        /// </summary>
        /// <param name="p"></param>
        /// <param name="up_angle_modifier"></param>
        public void AutoCalcPathUpAndT(in Path p, float up_angle_modifier = 0.0f)
        {
            var tmp = this._tmp_pool.Get();
            var tmp2 = this._tmp2_pool.Get();
            Pathing.CalcPathUpAndT(p, this.path.Array, this.path_up.Array, this.path_t.Array, tmp, tmp2, up_angle_modifier, p.path_id * this.m.PathCount);
            this._tmp_pool.Add(tmp);
            this._tmp2_pool.Add(tmp2);
        }

        /// <summary>
        ///  Auto calc just path time based on path segment length. See Pathing.CalcPathT
        /// </summary>
        /// <param name="p"></param>
        public void AutoCalcPathT(in Path p)
        {
            var tmp = this._tmp_pool.Get();
            var tmp2 = this._tmp2_pool.Get();
            int start_index = StartIndexOfPath(p);
            Pathing.CalcPathT(p, this.path.Array, this.path_t.Array, tmp, tmp2, start_index, start_index);
            this._tmp_pool.Add(tmp);
            this._tmp2_pool.Add(tmp2);
        }

        /// <summary>
        /// See Pathing.CalcPathUp
        /// </summary>
        /// <param name="p"></param>
        public void AutoCalcPathUp(in Path p)
        {
            int start_index = StartIndexOfPath(p);
            Pathing.CalcPathUp(p, this.path.Array, this.path_up.Array, start_index);
        }

        /// <summary>
        /// Will send the input path to the GPU using the data from PathArrayHelper.path, PathArrayHelper.path_t, and PathArrayHelper.path_up
        /// </summary>
        /// <param name="p"></param>
        public void UpdatePath(ref Path p)
        {
            if (p.look_at_camera && ReferenceEquals(null, this.m.FrustumCamera))
            {
                throw new System.Exception("Error, tried to look at a null camera!");
            }

            this.m.UpdatePath(ref p, path: this.path.Array, path_t: this.path_t.Array, path_up: this.path_up.Array, start_index: p.path_id * this.m.PathCount);
        }

        /// <summary>
        /// Deletes input path
        /// </summary>
        /// <param name="p"></param>
        public void DeletePath(ref Path p)
        {
            this.m.DeletePath(ref p);
            init_path(p); // clear path array data
        }

        /// <summary>
        /// Set angular velocity and velocity for a path that is defined using constant motion
        /// </summary>
        /// <param name="p"></param>
        /// <param name="anglular_velocity"></param>
        /// <param name="velocity"></param>
        public void SetPathConstants(in Path p, in Vector3 anglular_velocity, in Vector3 velocity, in Quaternion starting_rotation, in Vector3 starting_position)
        {
            if (!p.use_constants)
                throw new System.Exception("Error, input path must toggle use_constants flag!");
            var start_index = StartIndexOfPath(p);
            this.path[start_index] = anglular_velocity;
            this.path[start_index + 1] = velocity;
            this.path[start_index + 2] = new Vector3(starting_rotation.x, starting_rotation.y, starting_rotation.z);
            this.path_t[start_index + 2] = starting_rotation.w;
            this.path[start_index + 3] = starting_position;
        }

        /// <summary>
        /// Set a path that is just a non-moving point
        /// </summary>
        /// <param name="p"></param>
        /// <param name="position"></param>
        /// <param name="up"></param>
        public void SetPathStaticPosition(in Path p, in Vector3 position, in Vector3 up)
        {
            if (!p.look_at_camera)
                throw new System.Exception("Error, static position path can only be used for non-moving billboards"); // **The direction of a path with consecutive identical points is undefined

            var start_index = StartIndexOfPath(p);
            this.path[start_index + 0] = position;
            this.path_up[start_index + 0] = up;
            this.path_t[start_index + 0] = 0.0f;
            this.path[start_index + 1] = position;
            this.path_up[start_index + 1] = up;
            this.path_t[start_index + 1] = 1.0f;
        }

        /// <summary>
        /// Helper function to invoke Pathing.CalculatePathPositionDirection
        /// </summary>
        public void CalculatePathPositionDirection(in Path p, in InstanceData<InstanceProperties> instance, out Vector3 position, out Vector3 direction, out Vector3 up)
        {
            Pathing.CalculatePathPositionDirection(p, this.path.Array, instance, this.m, out position, out direction, out up, this.path_up.Array, this.path_t.Array, StartIndexOfPath(p));
        }
    }
}