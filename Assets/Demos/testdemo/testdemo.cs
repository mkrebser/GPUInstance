using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GPUInstance;
using GPUAnimation;

namespace GPUInstanceTest
{
    public class testdemo : MonoBehaviour
    {
        public Camera FrustumCullingCamera;

        private MeshInstancer m;
        private PathArrayHelper p;

        [ReadOnly]
        public int NumInstancesAllocated;
        [ReadOnly]
        public int NumSkeletonAllocated;
        [ReadOnly]
        public int NumPathsAllocated;
        [ReadOnly]
        public int NumPropertiesAllocated;
        [ReadOnly]
        public int GroupInstanceSum;

        public GPUSkinnedMeshComponent model1;
        public Vector3 Model1Scale = new Vector3(3,3,3);
        public GPUSkinnedMeshComponent model2;

        private System.Random r = new System.Random(0);

        private SkinnedMesh[,] skins = new SkinnedMesh[N, N];
        private Path[,] paths = new Path[N, N];
        const int N = 40;

        public float y_offset = 0.0f;

        // Start is called before the first frame update
        void Start()
        {
            model1.anim.Initialize();
            model2.anim.Initialize();

            int hierarchy_depth = System.Math.Max(model1.anim.BoneHierarchyDepth, model2.anim.BoneHierarchyDepth) + 2; // maximum allowed hierarchy depth
            int bone_count = System.Math.Max(model1.anim.BoneCount, model2.anim.BoneCount); // maximum allowed skeleton size in number of bones

            this.m = new MeshInstancer();
            this.m.Initialize(num_skeleton_bones: bone_count, max_parent_depth: hierarchy_depth, deltaBufferCount: 3, PropertyBufferMaxDeltaCount: 3, PathDeltaBufferSize: 3); // initialize meshinstancer (intentially picking terrible small delta buffer sizes for testing purposes)
            this.p = new PathArrayHelper(this.m);

            this.m.SetAllAnimations(new AnimationController[] { model1.anim, model2.anim }); // add all animations

            this.m.AddGPUSkinnedMeshType(model1); // add each mesh type
            this.m.AddGPUSkinnedMeshType(model2);
        }

        bool adding = true;
        float time_seconds = 0;
        const float period = 10.0f;
        void update()
        {
            // randomly add/remove skinned mesh every frame
            for (int i = 0; i < 20; i++)
            {
                var x = r.Next() % N;
                var y = r.Next() % N;

                bool remove = !adding;
                var gpu_skinned_mesh = r.Next() % 2 == 1 ? model1 : model2;
                GPUAnimation.Animation anim = gpu_skinned_mesh.anim.animations[r.Next(1, gpu_skinned_mesh.anim.animations.Length)];
                var mscale = gpu_skinned_mesh == model1 ? Model1Scale : Vector3.one;

                if (!this.skins[x,y].Initialized() && !remove)
                {
                    var skin = new SkinnedMesh(gpu_skinned_mesh, this.m);
                    skin.SetRadius(1.75f);
                    skin.Initialize();
                    skin.mesh.position = new Vector3(x, this.y_offset, y);
                    skin.mesh.scale = mscale;
                    skin.SetAnimation(anim, speed: 0.1f+ (float)r.NextDouble() * 2.0f, start_time: (float)r.NextDouble());

                    // create & assign path
                    Path path = new Path(path_length: 4, m: this.m, use_constants: true);
                    this.p.InitializePath(ref path);
                    this.p.SetPathConstants(path, new Vector3(0,1,0), new Vector3(0,0,1), skin.mesh.rotation, skin.mesh.position);
                    this.p.UpdatePath(ref path);
                    skin.mesh.SetPath(path, this.m);

                    skin.UpdateAll();
                    skins[x, y] = skin;
                    paths[x, y] = path;
                }
                else if (this.skins[x,y].Initialized() && remove)
                {
                    skins[x, y].Dispose();
                    this.p.DeletePath(ref paths[x, y]);
                }
            }

            // reset if adding
            time_seconds += Time.deltaTime;
            if (time_seconds > period)
            {
                adding = !adding;
                time_seconds = 0;
            }
        }

        // Update is called once per frame
        void Update()
        {
            this.update();

            this.NumInstancesAllocated = this.m.NumInstancesAllocated;
            this.NumSkeletonAllocated = this.m.NumSkeletonsAllocated;
            this.NumPathsAllocated = this.m.NumPathsAllocated;
            this.NumPropertiesAllocated = this.m.NumPropertiesAllocated;

            this.GroupInstanceSum = 0;
            this.GroupInstanceSum += this.m.NumInstancesAllocatedForMeshType(model1.MeshTypes[0][0]);
            this.GroupInstanceSum += this.m.NumInstancesAllocatedForMeshType(model2.MeshTypes[0][0]);

            this.m.FrustumCamera = FrustumCullingCamera;

            this.m.Update(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (this.m != null) 
                this.m.Dispose();
        }
    }
}
