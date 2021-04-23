using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GPUInstance;

using InstanceData = GPUInstance.InstanceData<GPUInstance.InstanceProperties>;

namespace GPUInstanceTest
{
    public class instancemeshdemo : MonoBehaviour
    {
        public List<Texture2D> Textures;

        public GameObject InstanceMeshGameObject;

        MeshInstancer imesher;

        public int instanceCountsq;
        public Camera FrustumCullingCamera;

        public Vector3 rotation;
        public Vector3 position;
        public Vector3 scale = new Vector3(1, 1, 1);
        public float VelocityRange = 0.1f;

        InstanceData[] buffer;
        InstanceData parent;

        PathArrayHelper p;

        // Use this for initialization
        void Awake()
        {
            //initialize
            buffer = new InstanceData[instanceCountsq * instanceCountsq];
            imesher = new MeshInstancer();
            imesher.Initialize(max_parent_depth: 2, pathCount: 4);

            this.p = new PathArrayHelper(this.imesher);

            // load all the textures
            var keys = Texture2MeshType.Textures2MeshType(this.Textures, imesher.Default.shared_mesh, imesher.Default.shared_material);

            // create all the mesh types that we can use
            foreach (var key in keys)
                imesher.AddNewMeshType(key.mesh_key, key.material_key);

            //make parent object
            parent = new InstanceData(imesher.Default);
            parent.position = position;
            parent.scale = scale;
            parent.rotation = Quaternion.Euler(rotation);
            buffer[0] = parent;

            float v = this.VelocityRange;
            var rangemax = keys.Count;
            for (int i = 0; i < instanceCountsq; i++)
            {
                for (int j = 0; j < instanceCountsq; j++)
                {
                    if (i == 0 && j == 0) continue; // skip parent

                    var tkey = Random.Range(1, rangemax); // get random texture
                    var newt = new InstanceData(imesher.GetMeshType(keys[tkey].mesh_key, keys[tkey].material_key)); // make instance data

                    newt.position = new Vector3(i, 0, j); // assign position of instance
                    newt.parentID = 0; // parent to instance at index 0
                    newt.props_color32 = new Color32((byte)Random.Range(0, 256), (byte)Random.Range(0, 256), (byte)Random.Range(0, 256), (byte)255); // set random color

                    // create & assign path
                    Path path = new Path(path_length: 4, m: this.imesher, use_constants: true);
                    this.p.InitializePath(ref path);
                    this.p.SetPathConstants(path, new Vector3(Random.Range(-10.0f, 10.0f), Random.Range(-10.0f, 10.0f), Random.Range(-10.0f, 10.0f)), new Vector3(Random.Range(-v, v), Random.Range(-v, v), Random.Range(-v, v)), newt.rotation, newt.position);
                    this.p.UpdatePath(ref path);
                    newt.SetPath(path, this.imesher);

                    buffer[i * instanceCountsq + j] = newt; // assign in buffer
                }
            }

            imesher.InitializeSet(buffer);
            imesher.AppendMany(buffer);
            parent = buffer[0];
        }

        void Update()
        {
            imesher.FrustumCamera = FrustumCullingCamera;

            // Update parent instance
            parent.position = position;
            parent.scale = scale;
            parent.rotation = Quaternion.Euler(rotation);
            parent.DirtyFlags = DirtyFlag.Position | DirtyFlag.Rotation | DirtyFlag.Scale;
            imesher.Append(ref parent);

            // do gpu instance update
            imesher.Update(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (imesher != null) imesher.Dispose();
        }
    }
}