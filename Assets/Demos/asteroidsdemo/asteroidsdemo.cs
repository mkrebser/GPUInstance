using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GPUInstance;
using GPUAnimation;

namespace GPUInstanceTest
{
    public class asteroidsdemo : MonoBehaviour
    {
        public Camera FrustumCullingCamera;

        public List<GPUMeshComponent> asteroids;

        private MeshInstancer m;
        private PathArrayHelper p;

        [Range(10.0f, 10000.0f)]
        public float Radius = 3000;
        [Range(0.5f, 20.0f)]
        public float ScaleRange = 3;
        [Range(1, 500000)]
        public int N = 1000;

        private GPUMesh[] instances;
        private Path[] paths;

        public double GlobalTimeSpeed = 1.0;

        void Start()
        {
            this.instances = new GPUMesh[this.N];
            this.paths = new Path[this.N];

            // Initialize GPU Instancer
            this.m = new MeshInstancer();
            this.m.Initialize(pathCount: 4);
            this.p = new PathArrayHelper(this.m);

            // Add all mesh types to GPU Instancer
            foreach (var a in this.asteroids)
                this.m.AddGPUMeshType(a);

            // Do stuff
            for (int i = 0; i < N; i++)
            {
                var mesh = asteroids[Random.Range(0, asteroids.Count)].MeshTypes[0];
                instances[i] = new GPUMesh(mesh, this.m);
                var size = Random.Range(1f, ScaleRange);
                instances[i].mesh.scale = Vector3.one * size;
                instances[i].SetRadius(4f * size); // set large enough radius so model doesnt get culled to early
                instances[i].Initialize();

                var path = GetNewPath(); // create new path
                instances[i].mesh.SetPath(path, this.m); // assign path to instance
                paths[i] = path;

                instances[i].Update();
            }
        }

        void Update()
        {
            Ticks.GlobalTimeSpeed = this.GlobalTimeSpeed;
            this.m.FrustumCamera = this.FrustumCullingCamera;
            this.m.Update(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (this.m != null) this.m.Dispose();
        }

        private Path GetNewPath()
        {
            // Create a new path object with desired parameters
            Path p = new Path(path_length: 4, this.m, use_constants: true);
            // Initialize path- this allocates path arrays & reserves a path gpu id
            this.p.InitializePath(ref p);
            // Were just using constants for the path (eg constant velocity)
            float rotate_mult = Random.Range(0, 1.0f);
            var ang_velocity = new Vector3(Random.Range(-1f, 1f) * rotate_mult, Random.Range(-1f, 1f) * rotate_mult, Random.Range(-1f, 1f) * rotate_mult);
            var postion = (Random.rotation * Vector3.forward) * Random.Range(-Radius, Radius);
            this.p.SetPathConstants(p, ang_velocity, Vector3.zero, Random.rotation, postion);
            // send path to GPU
            this.p.UpdatePath(ref p);

            return p;
        }
    }
}
