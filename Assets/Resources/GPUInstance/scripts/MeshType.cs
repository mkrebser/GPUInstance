using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GPUInstance
{

    public sealed partial class instancemesh : System.IDisposable, InstanceMeshSet
    {
        /// <summary>
        /// Used as a key for MeshType
        /// </summary>
        public struct MeshTypeKey : System.IEquatable<MeshTypeKey>
        {
            public Mesh mesh_key;
            public Material material_key;

            public MeshTypeKey(in Mesh mesh, in Material mat) { this.mesh_key = mesh; this.material_key = mat; }

            public MeshTypeKey(in MeshType m) { this.mesh_key = m.shared_mesh; this.material_key = m.shared_material; }

            public override bool Equals(object obj)
            {
                return obj is MeshTypeKey ? this.Equals((MeshTypeKey)obj) : false;
            }

            public bool Equals(MeshTypeKey m)
            {
                return ReferenceEquals(m, null) ? false : (m.mesh_key == mesh_key && m.material_key == this.material_key);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 29 + this.mesh_key.GetHashCode();
                    hash = hash * 31 + this.material_key.GetHashCode();
                    return hash;
                }
            }
        }
    }

    /// <summary>
    /// Data that will be associated with a mesh type
    /// </summary>
    public class MeshTypeData
    {
        public int argsByteOffset { get { checked { return argsIntOffset * sizeof(uint); } } }
        public int argsIntOffset { get; private set; } = int.MinValue;
        public ushort groupID { get; private set; } = 0;
        /// <summary>
        /// Each MeshType is defined by a (mesh, material) key. Because of this, the same material can be referenced by multiple mesh. Each MeshType needs to set unique buffer on its own material.
        /// To avoid issues with multiple mesh types (eg multiple mesh referencing the same material), the materials always get instanced. The instanced versions are used for
        /// the actual DrawMeshIndirect() invocation
        /// </summary>
        public Material InstancedMaterial { get; private set; }

        public MeshTypeData(in int argsIntOffset, in ushort groupID, in Material m)
        {
            this.argsIntOffset = argsIntOffset;
            this.groupID = groupID;
            this.InstancedMaterial = m;
        }
    }

    /// <summary>
    /// Class that describes a mesh type
    /// </summary>
    public class MeshType
    {
        /// <summary>
        /// Mesh used for this mesh type. It is called 'shared' because multiple MeshTypes can potentially reference the same mesh
        /// </summary>
        public Mesh shared_mesh { get; private set; } = null;
        /// <summary>
        /// Material used for this mesh type. It is called 'shared' because multiple MeshTypes can potentially reference the same material
        /// </summary>
        public Material shared_material { get; private set; } = null;

        public MeshTypeData mData { get; private set; } = null;

        /// <summary>
        /// Public groupID that gets set after adding a meshtype to a instancemeshShader object.
        /// </summary>
        public ushort groupID { get; private set; } = 0;

        public Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 100000);
        public UnityEngine.Rendering.ShadowCastingMode castShadows { get; set; }
        public bool receiveShadows { get; set; }

        public SkinWeights SkinWeight { get; private set; } = SkinWeights.FourBones;

        /// <summary>
        /// Initialized MeshType?
        /// </summary>
        public bool Initialized { get { return this.groupID > 0 && this.groupID != instancemesh.NULL_ID; } }

        public bool IsSkinnedMesh() { return this.shared_mesh != null && this.shared_mesh.boneWeights != null && this.shared_mesh.boneWeights.Length > 0 && this.shared_mesh.bindposes != null && this.shared_mesh.bindposes.Length > 0; }

        public MeshType(Mesh mes, Material mat, ushort groupID, MeshTypeData mData, UnityEngine.Rendering.ShadowCastingMode castShadows = UnityEngine.Rendering.ShadowCastingMode.Off, bool receiveShadows = false)
        {
            if (mes == null || mat == null)
                throw new System.Exception("Error, null args");
            if (!mat.enableInstancing)
                throw new System.Exception("Error, input material must enable instancing");
            this.shared_mesh = mes;
            this.shared_material = mat;
            this.receiveShadows = receiveShadows;
            this.castShadows = castShadows;
            this.groupID = groupID;
            this.mData = mData;
        }

        /// <summary>
        /// Set bone blend weights for this material
        /// </summary>
        /// <param name="bw"></param>
        public void SetBlendWeights(SkinWeights bw)
        {
            if (!this.IsSkinnedMesh())
                throw new System.Exception("Error, this MeshType does not support blend weights!");
            string blend = null;
            if (bw == SkinWeights.OneBone) blend = "Blend1";
            else if (bw == SkinWeights.TwoBones) blend = "Blend2";
            else if (bw == SkinWeights.FourBones) blend = "Blend4";
            else if (bw == SkinWeights.Unlimited) blend = "Blend4";
            this.mData.InstancedMaterial.EnableKeyword(blend);
            this.SkinWeight = bw;
        }

        public void Dispose()
        {
            this.groupID = 0;
            this.shared_mesh = null;
            this.shared_material = null;
            this.mData = null;
        }
    }
}