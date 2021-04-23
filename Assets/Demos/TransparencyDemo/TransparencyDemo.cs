using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GPUInstance;
using System.Linq;

using InstanceData = GPUInstance.InstanceData<GPUInstance.InstanceProperties>;

namespace GPUInstanceTest
{
    public class TransparencyDemo : MonoBehaviour
    {
        public List<Texture2D> Textures;

        MeshInstancer imesher;

        public bool frustum_culling = true;

        // Use this for initialization
        void Start()
        {
            Mesh mesh = BaseMeshLibrary.CreateDefault();
            Material material = new Material(Shader.Find("Instanced/instancemeshdefault_Transparent"));
            material.enableInstancing = true;

            imesher = new MeshInstancer();

            imesher.Initialize(64, 64, override_mesh: BaseMeshLibrary.CreatePlane2Sides(), override_material: BaseMaterialLibrary.CreateDefault(), max_parent_depth: 1, num_skeleton_bones: 1);
            var keys = Texture2MeshType.Textures2MeshType(this.Textures, mesh, material);

            foreach (var key in keys)
                imesher.AddNewMeshType(key.mesh_key, key.material_key);

            imesher.FrustumCamera = frustum_culling ? GameObject.Find("Main Camera").GetComponent<Camera>() : null;

            var udat = keys.Where(x => x.material_key.mainTexture.name == "clear3").First();

            InstanceData d1 = new InstanceData(imesher.GetMeshType(udat.mesh_key, udat.material_key));
            d1.props_color32 = new Color32(0, 0, 255, 90);
            d1.position = new Vector3(0.5f, 0, 0);
            d1.rotation = Quaternion.Euler(-90, 0, 0);
            d1.props_color32 = Color.blue;
            imesher.Initialize(ref d1);
            imesher.Append(ref d1);

            InstanceData d2 = new InstanceData(imesher.GetMeshType(udat.mesh_key, udat.material_key));
            d2.props_color32 = new Color32(0, 0, 255, 90);
            d2.position = new Vector3(0, 0, 0);
            d2.rotation = Quaternion.Euler(-90, 0, 0);
            d2.props_color32 = Color.blue;
            imesher.Initialize(ref d2);
            imesher.Append(ref d2);

            InstanceData d3 = new InstanceData(imesher.GetMeshType(udat.mesh_key, udat.material_key));
            d3.props_color32 = new Color32(0, 0, 255, 90);
            d3.position = new Vector3(-0.5f, 0, 0);
            d3.rotation = Quaternion.Euler(-90, 0, 0);
            d3.props_color32 = Color.blue;
            imesher.Initialize(ref d3);
            imesher.Append(ref d3);
        }

        // Update is called once per frame
        void Update()
        {
            imesher.Update(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (imesher != null)
                imesher.Dispose();
        }
    }
}