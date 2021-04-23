using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GPUAnimation;

namespace GPUInstance
{
    [System.Serializable]
    public class GPUSkinnedMeshComponent : MonoBehaviour
    {
        /// <summary>
        /// Skinned mesh for each lod level
        /// </summary>
        [Tooltip("Skinned mesh list for each lod level")]
        public List<GPUSkinnedMesh> lods = new List<GPUSkinnedMesh>();
        /// <summary>
        /// Anim controller for the skinned mesh
        /// </summary>
        public GPUAnimation.AnimationController anim = null;

        [System.Serializable]
        public class GPUSkinnedMesh
        {
            public List<SkinnedMeshRenderer> skins = new List<SkinnedMeshRenderer>();
        }

        /// <summary>
        /// ratio = radius/camera distance. eg, An object with radius=2,distance=20 has ratio = 2/20=0.1
        /// </summary>
        [Tooltip("ratio = radius/camera distance. eg, An object with radius=2,distance=20 has ratio = 2/20=0.1")]
        public float[] LOD_Radius_Ratios = new float[instancemesh.NumLODLevels] {0.1f, 0.08f, 0.06f, 0.045f, 0.03f };

        /// <summary>
        /// List of active mesh types for this component. Indexed by [lod][skin index]. *Skin index ordering for multi-mesh skins is determined by how you order the mesh renderer gameobjects in unity hierarchy.
        /// </summary>
        [HideInInspector]
        [System.NonSerialized]
        public MeshType[][] MeshTypes;

        /// <summary>
        /// Initialize all controllers. Additionally, duplicate controllers are discarded.
        /// </summary>
        /// <param name="list"> Input list of all gpu animation controllers. </param>
        /// <param name="max_skeleton_depth"> output maximum hierarchy depth of all animation controllers. </param>
        /// <param name="max_skeleton_bone_count"> output maximum skeleton bone count of all controllers. </param>
        /// <returns> Condensed & Initialized list of all animation controllers. </returns>
        public static AnimationController[] PrepareControllers(List<GPUSkinnedMeshComponent> list, out int max_skeleton_depth, out int max_skeleton_bone_count)
        {
            max_skeleton_depth = 0;
            max_skeleton_bone_count = 0;

            if (ReferenceEquals(null, list))
            {
                return null;
            }

            // Instantiate each incase the input list has prefabs
            GameObject game_obj_controllers = new GameObject("GPU SKinned Mesh Components");
            foreach (var c in list)
            {
                var obj = Instantiate(c);
                obj.transform.SetParent(game_obj_controllers.transform);
                obj.gameObject.SetActive(false);
            }

            // Remove duplicate controllers
            Dictionary<AnimationController, AnimationController> controllers_set = new Dictionary<AnimationController, AnimationController>();
            for (int i = 0; i < list.Count; i++)
            {
                if (controllers_set.ContainsKey(list[i].anim))
                {
                    list[i].anim = controllers_set[list[i].anim]; // overwrite with already added (but equivalent) animation controller. This way, animations arent duplicated on the GPU.
                }
                else
                {
                    controllers_set.Add(list[i].anim, list[i].anim); // add unique controller
                }
            }

            // Copy into output array
            var controllers = new GPUAnimation.AnimationController[controllers_set.Count];
            int count = 0;
            foreach (var controller in controllers_set.Values)
            {
                controllers[count] = controller;
                count++;
            }

            // Initialize & find max values
            for (int i = 0; i < controllers.Length; i++)
            {
                if (!controllers[i].IsIntialized)
                    controllers[i].Initialize();
                max_skeleton_depth = System.Math.Max(max_skeleton_depth, controllers[i].BoneHierarchyDepth);
                max_skeleton_bone_count = System.Math.Max(max_skeleton_bone_count, controllers[i].BoneCount);
            }

            return controllers;
        }

        public bool Initialized() { return !ReferenceEquals(null, this.MeshTypes); }
    }
}
