#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GPUInstance;

using InstanceData = GPUInstance.InstanceData<GPUInstance.InstanceProperties>;
namespace GPUInstanceTest
{

    public class instance_parent_demo : MonoBehaviour
    {
        InstanceData parent;
        InstanceData child1;
        InstanceData child2;

        public Vector3 position;
        public Vector3 scale = new Vector3(1, 1, 1);

        MeshInstancer imesher;

        public bool frustum_culling = true;

        // Use this for initialization
        private void Start()
        {
            InstanceMeshDeltaBuffer<instancemesh.instance_delta>.InstanceMeshIndirectIDBuffer.test();
            InstanceHierarchyMap.test();

            imesher = new MeshInstancer();

            //initialize
            imesher.Initialize();
            imesher.FrustumCamera = frustum_culling ? GameObject.Find("Main Camera").GetComponent<Camera>() : null;

            parent = new InstanceData(imesher.Default); parent.props_color32 = Color.red;
            child1 = new InstanceData(imesher.Default); child1.props_color32 = Color.green;
            child2 = new InstanceData(imesher.Default); child2.props_color32 = Color.blue;

            imesher.Initialize(ref parent);
            imesher.Initialize(ref child1);
            imesher.Initialize(ref child2);

            parent.parentID = instancemesh.NULL_ID;
            child1.parentID = parent.id;
            child2.parentID = child1.id;

            child1.position = new Vector3(2, 2, 2);
            child2.position = new Vector3(2, 2, 2);

            imesher.Append(ref parent);
            imesher.Append(ref child1);
            imesher.Append(ref child2);

            imesher.Update(0);
        }

        private void Update()
        {
            parent.position = position;
            parent.scale = scale;
            parent.DirtyFlags = parent.DirtyFlags | DirtyFlag.Scale | DirtyFlag.Position;
            imesher.Append(ref parent);

            imesher.Update(Time.deltaTime);
        }

        private void OnDestroy()
        {
            imesher.Dispose();
        }
    }
}
#endif