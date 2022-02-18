using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GPUInstance
{
    [System.Serializable]
    public class GPUMeshComponent : MonoBehaviour
    {
        [Tooltip("List of mesh renderers. Must have between 1-5.")]
        public List<MeshRenderer> lods = new List<MeshRenderer>();

        /// <summary>
        /// ratio = radius/camera distance. eg, An object with radius=2,distance=20 has ratio = 2/20=0.1
        /// </summary>
        [Tooltip("ratio = radius/camera distance. eg, An object with radius=2,distance=20 has ratio = 2/20=0.1")]
        public float[] LOD_Radius_Ratios = new float[instancemesh.NumLODLevels] { 0.1f, 0.08f, 0.06f, 0.045f, 0.03f };

        /// <summary>
        /// List of active mesh types for this component. Indexed by [lod].
        /// </summary>
        [HideInInspector]
        [System.NonSerialized]
        public MeshType[] MeshTypes;


        public bool Initialized() { return !ReferenceEquals(null, this.lods); }
    }
}