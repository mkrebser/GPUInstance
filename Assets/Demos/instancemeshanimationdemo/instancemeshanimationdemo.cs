using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GPUInstance;
using GPUAnimation;

using InstanceData = GPUInstance.InstanceData<GPUInstance.InstanceProperties>;

public class instancemeshanimationdemo : MonoBehaviour
{
    public GPUInstance.GPUSkinnedMeshComponent skinned_mesh;
    public int NInstancesSqrd = 10;
    public Camera FrustumCullingCamera;

    private GPUInstance.MeshInstancer m;
    private GPUInstance.SkinnedMesh[,] instances;

    public bool do_mesh_type_stress_test = true;

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
        var mesh_type = m.AddGPUSkinnedMeshType(this.skinned_mesh, override_shadows: true, shadow_mode: UnityEngine.Rendering.ShadowCastingMode.Off, receive_shadows: false)[0][0];
        mesh_type.SetBlendWeights(SkinWeights.OneBone); // force one bone for this model

        // for testing purposes... optional mesh type stress-test. 
        List<MeshType> mesh_types = new List<MeshType>(); mesh_types.Add(mesh_type);
        if (do_mesh_type_stress_test)
        {
            // Max out mesh types (ie max draw calls xd)
            while (this.m.NumMeshTypesAllocated < MeshInstancer.MaxMeshTypes-1)
            {
                var new_mtype = m.AddNewMeshType(mesh_type.shared_mesh, Instantiate(mesh_type.shared_material));
                mesh_types.Add(new_mtype);
            }
            
            // then delete them all
            for (int i = 0; i < mesh_types.Count; i++)
            {
                this.m.DeleteMeshType(mesh_types[i]);
            }
            mesh_types.Clear();

            // now add them all back for funzies
            this.skinned_mesh.MeshTypes = null; // just set this back to null xd
            mesh_type = m.AddGPUSkinnedMeshType(this.skinned_mesh, override_shadows: true, shadow_mode: UnityEngine.Rendering.ShadowCastingMode.Off, receive_shadows: false)[0][0];
            mesh_type.SetBlendWeights(SkinWeights.OneBone); // force one bone for this model

            mesh_types.Add(mesh_type);
            while (this.m.NumMeshTypesAllocated < MeshInstancer.MaxMeshTypes - 1)
            {
                var new_mtype = m.AddNewMeshType(mesh_type.shared_mesh, Instantiate(mesh_type.shared_material));
                mesh_types.Add(new_mtype);
            }
        }

        // Create instances
        instances = new GPUInstance.SkinnedMesh[NInstancesSqrd, NInstancesSqrd];
        for (int i = 0; i < NInstancesSqrd; i++)
            for (int j = 0; j < NInstancesSqrd; j++)
            {
                var mtype = mesh_types[Random.Range(0, mesh_types.Count)]; // pick mesh type at random
                instances[i, j] = new GPUInstance.SkinnedMesh(mtype, skinned_mesh.anim, m);
                instances[i, j].mesh.position = new Vector3(i*0.5f, 0, j*0.5f);
                instances[i, j].Initialize();
                instances[i, j].SetAnimation(skinned_mesh.anim.namedAnimations["Run"], speed: Random.Range(0.1f, 3.0f), start_time: Random.Range(0.0f, 1.0f));
                instances[i, j].UpdateAll();
            }

        //// visualize bones on the 0,0 model
        var points = new InstanceData[instances[0, 0].skeleton.data.Length];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new InstanceData(m.Default);
            points[i].parentID = instances[0, 0].skeleton.data[i].id;
            points[i].scale = new Vector3(0.03f, 0.03f, 0.03f);
            points[i].props_color32 = Color.red;
            m.Initialize(ref points[i]);
            m.Append(ref points[i]);
        }
    }

    // Update is called once per frame
    void Update()
    {
        m.FrustumCamera = this.FrustumCullingCamera;

        // Note* on culling- If it seems like things are being culled one frame too long after coming into the camera frustum-
        // make sure that the camera frustum (eg camera.transform) is being updated BEFORE MeshInstancer.Update (otherwise it will be one frame behind)
        m.Update(Time.deltaTime);
    }

    private void OnDestroy()
    {
        m.Dispose();
    }
}
