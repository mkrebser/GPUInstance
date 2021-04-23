using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace GPUAnimation
{
    public class GPUSkeletonLODComponent : MonoBehaviour
    {
        public SkeletonLODDescription[] SkeletonLOD = new SkeletonLODDescription[GPUInstance.instancemesh.NumLODLevels] 
        {
            new SkeletonLODDescription(0), new SkeletonLODDescription(1), new SkeletonLODDescription(2), new SkeletonLODDescription(3), new SkeletonLODDescription(4)
        };

        [System.Serializable]
        public struct SkeletonLODDescription
        {
            [Tooltip("The lowest detail LOD a bone can be animated at. Eg, LOD=1 would make a bone only animate at LODS '0' and '1'. For LOD=2, it would animate at LODS '0','1','2'.")]
            [ReadOnly]
            public int Lowest_Detail_LOD_For_Animation;
            [Tooltip("List of bones for this LOD level. This will affect all children of the input bones as well.")]
            public List<Transform> Bones;

            public SkeletonLODDescription(int lod) { this.Lowest_Detail_LOD_For_Animation = lod; this.Bones = new List<Transform>(); }
        }

        public int[] VerifyAndRetrieveBoneLODS(Transform[] skeleton, out string error)
        {
            HashSet<Transform> skeleton_set = new HashSet<Transform>(skeleton);

            const int kNumLODS = GPUInstance.instancemesh.NumLODLevels;
            if (this.SkeletonLOD == null || this.SkeletonLOD.Length != kNumLODS)
            {
                error = string.Format("Error, the SkeletonLOD array from GPUSkeletonLODComponent MUST have a length equal to the number of LODS: [{0}]", kNumLODS);
                return null;
            }

            foreach (var ld in SkeletonLOD)
            {
                if (ld.Bones != null)
                {
                    foreach (var bone in ld.Bones)
                    {
                        if (bone == null) // just ignore null
                            continue;

                        if (!skeleton_set.Contains(bone))
                        {
                            error = string.Format("Error, the bone [{0}] does not belong to the skeleton!", bone.name);
                            return null;
                        }
                    }
                }
            }

            // Returns hashset with all parent bones & self
            HashSet<Transform> RetrieveAllBoneParents(Transform bone)
            {
                HashSet<Transform> bones = new HashSet<Transform>();
                while (bone != null)
                {
                    if (skeleton_set.Contains(bone)) // add if transform is in skeleton
                        bones.Add(bone);
                    bone = bone.parent;
                }
                return bones;
            }

            bool HashSetIntersect(in HashSet<Transform> a, in HashSet<Transform> b)
            {
                foreach (var val in a)
                    if (b.Contains(val))
                        return true;
                return false;
            }

            // make a set which describes the lowest detail lod for specified bones
            HashSet<Transform>[] bone_lods_set = new HashSet<Transform>[kNumLODS];
            for (int i = 0; i < kNumLODS; i++) bone_lods_set[i] = new HashSet<Transform>(SkeletonLOD[i].Bones);

            // init bone lods array (an element for each bone- lowest detail lod for each bone)
            int[] bone_lods = new int[skeleton.Length];
            for (int i = 0; i < bone_lods.Length; i++)
                bone_lods[i] = kNumLODS - 1; // set all bones to initially animate at all LODS

            for (int bidx = 0; bidx < skeleton.Length; bidx++)
            {
                int lowest_detail_lod = kNumLODS - 1;
                var bParents = RetrieveAllBoneParents(skeleton[bidx]); // get all bone parents of this bone (and self)

                for (int lod = 0; lod < kNumLODS; lod++)
                {
                    if (HashSetIntersect(bParents, bone_lods_set[lod])) // if this bone or any of its parents is in an SkeletonLOD set, then select that LOD
                    {
                        lowest_detail_lod = lod;
                        break;
                    }
                }

                bone_lods[bidx] = lowest_detail_lod;
            }

            error = null;
            return bone_lods;
        }
    }
}
