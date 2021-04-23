using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GPUInstance;
using GPUAnimation;

namespace GPUInstanceTest
{
    public class crowddemo : MonoBehaviour
    {
        public Camera FrustumCullingCamera;

        public List<GPUSkinnedMeshComponent> characters;

        private MeshInstancer m;
        private PathArrayHelper p;

        private const int N = 100;

        private SkinnedMesh[,] instances = new SkinnedMesh[N, N];
        private Path[,] paths = new Path[N, N];

        public GameObject floor;
        private float floorWidth { get { return this.floor.transform.localScale.x * 64; } }
        private float floorLength { get { return this.floor.transform.localScale.z * 64; } }
        private Vector3 floor_min { get { return this.floor.transform.position - new Vector3(floorWidth * 0.5f, 0, floorLength * 0.5f); } }
        private Vector3 floor_max { get { return this.floor.transform.position + new Vector3(floorWidth * 0.5f, 0, floorLength * 0.5f); } }

        GameObject[] bones_unity = null;

        public double GlobalTimeSpeed = 1.0;

        void Start()
        {
            // Initialize character mesh list
            int hierarchy_depth, skeleton_bone_count;
            var controllers = GPUSkinnedMeshComponent.PrepareControllers(characters, out hierarchy_depth, out skeleton_bone_count);

            // Initialize GPU Instancer
            this.m = new MeshInstancer();
            this.m.Initialize(max_parent_depth: hierarchy_depth + 2, num_skeleton_bones: skeleton_bone_count, pathCount: 2);
            this.p = new PathArrayHelper(this.m);

            // Add all animations to GPU buffer
            this.m.SetAllAnimations(controllers);

            // Add all character mesh types to GPU Instancer
            foreach (var character in this.characters)
                this.m.AddGPUSkinnedMeshType(character);

            // Do stuff
            for (int i = 0; i < N; i++)
                for (int j = 0; j < N; j++)
                {
                    var mesh = characters[Random.Range(0, characters.Count)];
                    var anim = mesh.anim.namedAnimations["walk"];
                    instances[i, j] = new SkinnedMesh(mesh, this.m);
                    instances[i, j].mesh.position = new Vector3(i, 0, j);
                    instances[i, j].SetRadius(1.75f); // set large enough radius so model doesnt get culled to early
                    instances[i, j].Initialize();

                    instances[i, j].SetAnimation(anim, speed: 1.4f, start_time: Random.Range(0.0f, 1.0f)); // set animation

                    var path = GetNewPath(); // create new path
                    instances[i, j].mesh.SetPath(path, this.m); // assign path to instance
                    paths[i, j] = path;

                    instances[i, j].UpdateAll();
                }

            // For testing purposes.. do bone visualization of [0,0] model
            bones_unity = new GameObject[instances[0, 0].skeleton.data.Length];
            for (int i = 0; i < bones_unity.Length; i++)
            {
                var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.name = "Calculated Bone Transform " + i.ToString();
                bones_unity[i] = obj;
            }
        }

        void Update()
        {
            Ticks.GlobalTimeSpeed = this.GlobalTimeSpeed;
            this.m.FrustumCamera = this.FrustumCullingCamera;
            this.m.Update(Time.deltaTime);

            // visualize bone cpu-gpu synchronization
            for (int i = 0; i < bones_unity.Length; i++)
            {
                Vector3 position; Quaternion rotation; Vector3 scale;
                instances[0, 0].BoneWorldTRS(this.paths[0,0], this.p, i, out position, out rotation, out scale);
                bones_unity[i].transform.position = position;
                bones_unity[i].transform.rotation = rotation;
                bones_unity[i].transform.localScale = new Vector3(0.05f, 0.05f, 0.05f);
            }
        }

        private void OnDestroy()
        {
            if (this.m != null) this.m.Dispose();
        }

        private Vector3 RandomPointOnFloor()
        {
            var min = this.floor_min;
            var max = this.floor_max;
            return new Vector3(Random.Range(min.x, max.x), this.floor.transform.position.y, Random.Range(min.z, max.z));
        }

        private Path GetNewPath()
        {
            // Get 2 random points which will make a path
            var p1 = RandomPointOnFloor();
            var p2 = RandomPointOnFloor();
            while ((p1 - p2).magnitude < 10)
                p2 = RandomPointOnFloor();

            // Create a new path object with desired parameters
            Path p = new Path(path_length: 2, this.m, loop: true, path_time: (p2 - p1).magnitude, yaw_only: true, avg_path: false, smoothing: false);
            // Initialize path- this allocates path arrays & reserves a path gpu id
            this.p.InitializePath(ref p);
            // Copy path into buffers
            var start_index = this.p.StartIndexOfPath(p);
            this.p.path[start_index] = p1;
            this.p.path[start_index + 1] = p2;
            this.p.AutoCalcPathUpAndT(p);
            // send path to GPU
            this.p.UpdatePath(ref p);

            return p;
        }
    }
}
