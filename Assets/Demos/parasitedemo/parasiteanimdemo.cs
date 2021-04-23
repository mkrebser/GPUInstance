using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using InstanceData = GPUInstance.InstanceData<GPUInstance.InstanceProperties>;
namespace GPUInstanceTest
{
    public class parasiteanimdemo : MonoBehaviour
    {
        public GPUInstance.GPUSkinnedMeshComponent skinned_mesh;
        public int NInstancesSqrd = 10;
        public Camera FrustumCullingCamera = null;

        private GPUInstance.MeshInstancer m;
        private GPUInstance.SkinnedMesh[,] instances;

        private GameObject[] bones_unity = null;

        // Start is called before the first frame update
        void Start()
        {
            // Initialize animation controller
            skinned_mesh.anim.Initialize();
            int NumBonesInSkeleton = skinned_mesh.anim.BoneCount;
            int MaxHierarchyDepth = skinned_mesh.anim.BoneHierarchyDepth + 2; // hierarchy for each bone depth + one for mesh and an additional for attaching to bones

            // Create and initialize mesh instancer
            m = new GPUInstance.MeshInstancer();
            m.Initialize(num_skeleton_bones: NumBonesInSkeleton, max_parent_depth: MaxHierarchyDepth);

            // Set animations buffer
            m.SetAllAnimations(new GPUAnimation.AnimationController[1] { skinned_mesh.anim });

            // Create a mesh type for the parasite model
            m.AddGPUSkinnedMeshType(this.skinned_mesh, override_shadows: true, shadow_mode: UnityEngine.Rendering.ShadowCastingMode.Off, receive_shadows: false); // spawn using lowest detail model (you can use any but this will be faster- this way the initial frame doesnt render with all high-detail models)

            // Create instances
            instances = new GPUInstance.SkinnedMesh[NInstancesSqrd, NInstancesSqrd];
            for (int i = 0; i < NInstancesSqrd; i++)
                for (int j = 0; j < NInstancesSqrd; j++)
                {
                    instances[i, j] = new GPUInstance.SkinnedMesh(this.skinned_mesh, this.m); // make new skinned mesh
                    instances[i, j].mesh.position = new Vector3(i * 1.5f, 0, j * 1.5f); // set position
                    instances[i, j].SetRadius(1.75f); // assign radius large enough so that the model doesn't get culled too early
                    instances[i, j].Initialize();
                    instances[i, j].SetAnimation(skinned_mesh.anim.animations[4], speed: Random.Range(0.1f, 3.0f), start_time: Random.Range(0.0f, 1.0f));
                    instances[i, j].UpdateAll();
                }

            //// visualize bones on the 0,0 model
            //var points = new InstanceData[instances[0, 0].skeleton.data.Length];
            //for (int i = 0; i < points.Length; i++)
            //{
            //    points[i] = new InstanceData(m.mesh.Default);
            //    points[i].parentID = instances[0, 0].skeleton.data[i].id;
            //    points[i].scale = new Vector3(0.03f, 0.03f, 0.03f);
            //    points[i].props_color32 = Color.red;
            //    m.Initialize(ref points[i]);
            //    m.Append(ref points[i]);
            //}

            bones_unity = new GameObject[instances[0, 0].skeleton.data.Length];
            for (int i = 0; i < bones_unity.Length; i++)
            {
                var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.name = "Calculated Bone Transform " + i.ToString();
                bones_unity[i] = obj;
            }
        }

        int f = 1;
        // Update is called once per frame
        void Update()
        {
            // Assign frustum culling camera
            m.FrustumCamera = FrustumCullingCamera;
            // Run update
            m.Update(Time.deltaTime);

            if (f % 1000 == 0)
            {
                instances[0, 0].SetAnimation(skinned_mesh.anim.animations[4], speed: Random.Range(0.1f, 3.0f), start_time: Random.Range(0.0f, 1.0f));
                instances[0, 0].UpdateRoot();
            }

            // visualize bone cpu-gpu synchronization
            for (int i = 0; i < bones_unity.Length; i++)
            {
                Vector3 position; Quaternion rotation; Vector3 scale;
                instances[0, 0].BoneWorldTRS(i, out position, out rotation, out scale);
                bones_unity[i].transform.position = position;
                bones_unity[i].transform.rotation = rotation;
                bones_unity[i].transform.localScale = new Vector3(0.05f, 0.05f, 0.05f);
            }

            f++;
        }

        private void OnDestroy()
        {
            m.Dispose();
        }
    }
}