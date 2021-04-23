using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GPUInstance;
using System.Linq;

using InstanceData = GPUInstance.InstanceData<GPUInstance.InstanceProperties>;

namespace GPUInstanceTest
{
    [System.Serializable]
    public struct PathProps
    {
        public bool loop;
        public bool do_avg;
        public bool smoothing;
        public bool use_constants;
        public bool yaw_only;
        [Range(-2.0f, 2.0f)]
        public float avg_weight;
        [Range(0, 2f)]
        public float speed;
        [Range(0.02f, 10000f)]
        public float completion_time;
        [Range(0, 10.0f)]
        public float path_up_rotation_modifier;

        [System.NonSerialized]
        [HideInInspector]
        public Path _cached_path;

        public bool draw_path;

        public PathProps(bool draw_path=true, bool loop = false, bool do_avg = true, bool smoothing = true, float avg_weight = 0.5f, float speed = 1.0f, float completion_time = 1.0f, bool use_constants = false, bool yaw_only = false, float path_rotation_modifier=1.0f)
        {
            this.loop = loop; this.do_avg = do_avg; this.smoothing = smoothing; this.avg_weight = avg_weight; this.speed = speed; this.completion_time = completion_time; this.use_constants = use_constants; this.yaw_only = yaw_only; this.path_up_rotation_modifier = path_rotation_modifier;
            _cached_path = default(Path);
            this.draw_path = draw_path;
        }

        public bool Update(List<Transform> points, PathArrayHelper p, MeshInstancer m, bool force_update=false)
        {
            if (points.Count != this._cached_path.Length)
                throw new System.Exception("Error, please dont change the path length. Make a new path instead.");

            bool dirty = this._cached_path.loop != this.loop || this._cached_path.PathCompletionTime != this.completion_time || this._cached_path.avg_path != this.do_avg ||
                this._cached_path.AvgWeight != this.avg_weight || this._cached_path.smoothing != this.smoothing || this._cached_path.use_constants != this.use_constants || this._cached_path.yaw_only != this.yaw_only;

            this._cached_path.loop = this.loop; this._cached_path.avg_path = this.do_avg; this._cached_path.smoothing = this.smoothing;
            this._cached_path.AvgWeight = this.avg_weight; this._cached_path.PathCompletionTime = this.completion_time; this._cached_path.yaw_only = this.yaw_only;
            this._cached_path.SetUseConstants(this.use_constants, m);

            if (dirty || force_update)
            {
                var start_index = p.StartIndexOfPath(this._cached_path); // get index of path in paths array
                for (int i = 0; i < points.Count; i++) // set the path points for instances to follow
                    p.path[start_index + i] = points[i].position;
                p.AutoCalcPathUpAndT(this._cached_path, this.path_up_rotation_modifier); // recalc up and T
            }

            return dirty || force_update;
        }
    }

    public class pathdemo : MonoBehaviour
    {
        private MeshInstancer m;
        private PathArrayHelper p;

        public List<Transform> path1 = new List<Transform>();
        public PathProps path1_props = new PathProps(draw_path: true);
        private InstanceData instance1;

        public List<Transform> path2 = new List<Transform>();
        public PathProps path2_props = new PathProps(draw_path: true);
        private InstanceData instance2;

        public List<Transform> path3 = new List<Transform>();
        public PathProps path3_props = new PathProps(draw_path: true);
        private InstanceData instance3;

        // Start is called before the first frame update
        void Start()
        {
            this.m = new MeshInstancer();
            this.m.Initialize(pathCount: Mathf.Max(path1.Count, Mathf.Max(path2.Count, path3.Count)));
            this.p = new PathArrayHelper(this.m);

            InitInstance(ref this.instance1, this.path1, ref this.path1_props, Color.red);
            InitInstance(ref this.instance2, this.path2, ref this.path2_props, Color.green);
            InitInstance(ref this.instance3, this.path3, ref this.path3_props, Color.blue);
        }

        void UpdatePath(ref InstanceData instance, List<Transform> path, ref PathProps path_props)
        {
            var dirty = path_props.Update(path, this.p, this.m);
            if (dirty)  this.p.UpdatePath(ref path_props._cached_path); // send updated path to gpu if path changed

            // optional debug path drawing
            if (path_props.draw_path)
            {
                // Draw path
                Pathing.DebugDrawPath(path_props._cached_path, path: p.path.Array, path_up: p.path_up.Array, path_t: p.path_t.Array, start_index: p.StartIndexOfPath(path_props._cached_path), n_samples: 100);

                // Draw orientation of instance
                Vector3 position, direction, up;
                p.CalculatePathPositionDirection(path_props._cached_path, instance, out position, out direction, out up);
                Debug.DrawLine(position, position + direction * 1.5f, Color.blue);
                Debug.DrawLine(position, position + up * 1.5f, Color.green);
                Debug.DrawLine(position, position + Vector3.Cross(up, direction).normalized * 1.5f, Color.red);
            }

            // update instance if speed was changed or the path changed
            if (instance.props_pathSpeed != path_props.speed || dirty)
            {
                instance.SetPath(path_props._cached_path, this.m, path_props.speed);
                this.m.Append(ref instance);
                path_props.speed = instance.props_pathSpeed;
            }
        }

        void InitInstance(ref InstanceData instance, List<Transform> path, ref PathProps path_props, in Color color)
        {
            instance = new InstanceData(this.m.Default); // make new instance
            instance.props_color32 = color;
            this.m.Initialize(ref instance); // initialize instance

            path_props._cached_path = new Path(path.Count, this.m, loop: path_props.loop, use_constants: path_props.use_constants, avg_path: path_props.do_avg, path_time: path_props.completion_time, avg_weight: path_props.avg_weight, smoothing: path_props.smoothing);
            this.p.InitializePath(ref path_props._cached_path); // init path
            path_props.Update(path, this.p, this.m, force_update: true); // do property copy into the path
            this.p.UpdatePath(ref path_props._cached_path); // send path to GPU
            instance.SetPath(path_props._cached_path, this.m); // assign path to instance

            this.m.Append(ref instance); // append to gpu buffer

            // Add children
            var child = new InstanceData(this.m.Default);
            child.scale = new Vector3(0.25f, 0.25f, 0.25f);
            child.position = new Vector3(0, 0, 0.5f);
            child.props_color32 = Color.blue;
            child.parentID = instance.id;
            this.m.Initialize(ref child);
            this.m.Append(ref child);

            child = new InstanceData(this.m.Default);
            child.scale = new Vector3(0.25f, 0.25f, 0.25f);
            child.position = new Vector3(0, 0.5f, 0);
            child.props_color32 = Color.green;
            child.parentID = instance.id;
            this.m.Initialize(ref child);
            this.m.Append(ref child);

            child = new InstanceData(this.m.Default);
            child.scale = new Vector3(0.25f, 0.25f, 0.25f);
            child.position = new Vector3(0.5f, 0, 0);
            child.props_color32 = Color.red;
            child.parentID = instance.id;
            this.m.Initialize(ref child);
            this.m.Append(ref child);
        }

        // Update is called once per frame
        void Update()
        {
            UpdatePath(ref this.instance1, this.path1, ref this.path1_props);
            UpdatePath(ref this.instance2, this.path2, ref this.path2_props);
            UpdatePath(ref this.instance3, this.path3, ref this.path3_props);

            this.m.Update(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (this.m != null) m.Dispose();
        }
    }
}