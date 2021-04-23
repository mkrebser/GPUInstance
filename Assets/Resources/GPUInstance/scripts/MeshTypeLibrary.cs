using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GPUInstance;
using System;
using System.Linq;

namespace GPUInstance
{
    /// <summary>
    /// Helper class that will load an entire folder of textures into individual mesh types
    /// </summary>
    public static class Texture2MeshType
    {
        /// <summary>
        /// Load all texture from an input file & return them as a UVData
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static List<instancemesh.MeshTypeKey> Textures2MeshType(List<Texture2D> textures, Mesh mesh, Material material)
        {
            var l = new List<instancemesh.MeshTypeKey>();

            if (textures == null || textures.Count == 0)
                return l;

            foreach (var t in textures)
            {
                var new_mat = new instancemesh.MeshTypeKey(mesh, MonoBehaviour.Instantiate(material));
                new_mat.material_key.mainTexture = t;
                l.Add(new_mat);
            }

            return l;
        }
    }

    /// <summary>
    /// A library that contains UnityEngine mesh types
    /// </summary>
    public static class BaseMeshLibrary
    {
        static Mesh[] types;

        /// <summary>
        /// Create default cube mesh. Cube faces are added in Front, Back, Right, Left, Up, Down order ! Vertices & triangles for each face is wrapped from bottom left corner of the face in clockwise direction
        /// </summary>
        /// <returns></returns>
        public static Mesh CreateDefault()
        {
            Vector3 v000 = new Vector3(-0.5f, -0.5f, -0.5f);
            Vector3 v001 = new Vector3(-0.5f, -0.5f, 0.5f);
            Vector3 v010 = new Vector3(-0.5f, 0.5f, -0.5f);
            Vector3 v011 = new Vector3(-0.5f, 0.5f, 0.5f);
            Vector3 v100 = new Vector3(0.5f, -0.5f, -0.5f);
            Vector3 v101 = new Vector3(0.5f, -0.5f, 0.5f);
            Vector3 v110 = new Vector3(0.5f, 0.5f, -0.5f);
            Vector3 v111 = new Vector3(0.5f, 0.5f, 0.5f);

            var verts = new Vector3[]
            {
                // all faces wrapped from bottom left corner (clockwise) when looking at opposite direction of normal

                // front (normal is z+)
                v101, // 0
                v111, // 1
                v011, // 2
                v001, // 3

                // back (normal is z-)
                v000, // 4
                v010, // 5
                v110, // 6
                v100, // 7

                // right (normal is x+)
                v100, // 8
                v110, // 9
                v111, // 10
                v101, // 11

                // left (normal is x-)
                v001, // 12
                v011, // 13
                v010, // 14
                v000, // 15

                // top (normal is y+)
                v010, // 16
                v011, // 17
                v111, // 18
                v110, // 19

                // bottom (normal is y-)
                v001, // 20
                v000, // 21
                v100, // 22
                v101  // 23     
            };

            var normals = new Vector3[]
            {
                new Vector3(0, 0, 1),
                new Vector3(0, 0, 1),
                new Vector3(0, 0, 1),
                new Vector3(0, 0, 1),

                new Vector3(0, 0, -1),
                new Vector3(0, 0, -1),
                new Vector3(0, 0, -1),
                new Vector3(0, 0, -1),

                new Vector3(1, 0, 0),
                new Vector3(1, 0, 0),
                new Vector3(1, 0, 0),
                new Vector3(1, 0, 0),

                new Vector3(-1, 0, 0),
                new Vector3(-1, 0, 0),
                new Vector3(-1, 0, 0),
                new Vector3(-1, 0, 0),

                new Vector3(0, 1, 0),
                new Vector3(0, 1, 0),
                new Vector3(0, 1, 0),
                new Vector3(0, 1, 0),

                new Vector3(0, -1, 0),
                new Vector3(0, -1, 0),
                new Vector3(0, -1, 0),
                new Vector3(0, -1, 0)
            };

            var uvs = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(0, 1),
                new Vector2(1, 1),
                new Vector2(1, 0),

                new Vector2(0, 0),
                new Vector2(0, 1),
                new Vector2(1, 1),
                new Vector2(1, 0),

                new Vector2(0, 0),
                new Vector2(0, 1),
                new Vector2(1, 1),
                new Vector2(1, 0),

                new Vector2(0, 0),
                new Vector2(0, 1),
                new Vector2(1, 1),
                new Vector2(1, 0),

                new Vector2(0, 0),
                new Vector2(0, 1),
                new Vector2(1, 1),
                new Vector2(1, 0),

                new Vector2(0, 0),
                new Vector2(0, 1),
                new Vector2(1, 1),
                new Vector2(1, 0),
            };

            var tris = new int[]
            {
                // front
                0, 1, 2, 0, 2, 3,
                // back
                4, 5, 6, 4, 6, 7,
                // right
                8, 9, 10, 8, 10, 11,
                // left
                12, 13, 14, 12, 14, 15,
                // top
                16, 17, 18, 16, 18, 19,
                // bottom
                20, 21, 22, 20, 22, 23
            };

            var m = new Mesh();
            m.vertices = verts;
            m.triangles = tris;
            m.normals = normals;
            m.uv = uvs;
            m.RecalculateBounds();
            m.name = "DefaultMesh";
            return m;
        }

        /// <summary>
        /// Create a plane that is only drawn on one side (faces z+) direction
        /// </summary>
        /// <returns></returns>
        public static Mesh CreatePlane()
        {
            var verts = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0),
                new Vector3(-0.5f, 0.5f, 0),
                new Vector3(0.5f, -0.5f, 0),
                new Vector3(0.5f, 0.5f, 0)
            };

            var uvs = new Vector2[]
            {
                new Vector2(0,0),
                new Vector2(0,1),
                new Vector2(1,0),
                new Vector2(1,1)
            };

            var normals = new Vector3[]
            {
                new Vector3(0,0,1),
                new Vector3(0,0,1),
                new Vector3(0,0,1),
                new Vector3(0,0,1)
            };

            var tris = new int[]
            {
                0,2,1,
                2,3,1
            };

            var mesh = new Mesh();
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.RecalculateBounds();
            mesh.name = "Plane";
            return mesh;
        }
        /// <summary>
        /// Create a plane that is drawn on both sized z+ and z-
        /// </summary>
        /// <returns></returns>
        public static Mesh CreatePlane2Sides()
        {
            var verts = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0),
                new Vector3(-0.5f, 0.5f, 0),
                new Vector3(0.5f, -0.5f, 0),
                new Vector3(0.5f, 0.5f, 0),

                new Vector3(-0.5f, -0.5f, 0),
                new Vector3(-0.5f, 0.5f, 0),
                new Vector3(0.5f, -0.5f, 0),
                new Vector3(0.5f, 0.5f, 0)
            };

            var uvs = new Vector2[]
            {
                new Vector2(0,0),
                new Vector2(0,1),
                new Vector2(1,0),
                new Vector2(1,1),

                new Vector2(0,0),
                new Vector2(0,1),
                new Vector2(1,0),
                new Vector2(1,1)
            };

            var normals = new Vector3[]
            {
                new Vector3(0,0,1),
                new Vector3(0,0,1),
                new Vector3(0,0,1),
                new Vector3(0,0,1),

                new Vector3(0,0,-1),
                new Vector3(0,0,-1),
                new Vector3(0,0,-1),
                new Vector3(0,0,-1)
            };

            var tris = new int[]
            {
                0,2,1,
                2,3,1,

                4,5,6,
                6,5,7
            };

            var mesh = new Mesh();
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.RecalculateBounds();
            mesh.name = "Plane";
            return mesh;
        }
    }

    /// <summary>
    /// A library that contains UnityEngine materials
    /// </summary>
    public static class BaseMaterialLibrary
    {
        /// <summary>
        /// Create default material
        /// </summary>
        /// <returns></returns>
        public static Material CreateDefault()
        {
            var m = new Material(Shader.Find("Instanced/instancemeshdefault"));
            m.enableInstancing = true;
            return m;
        }
    }
}
