using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GPUInstance;

namespace GPUInstanceTest
{
    public class billboarddemo : MonoBehaviour
    {
        public Camera FrustumCamera;

        public List<Sprite> Explosion;
        public Material ExplosionMaterial;

        private MeshInstancer m;
        private PathArrayHelper p;

        const int N = 100000;

        private InstanceData<InstanceProperties>[] instances = new InstanceData<InstanceProperties>[N];
        private Path[] paths = new Path[N];

        // Make texture anim.. Really all this function does is create a list of consecutive (offset, tiling) of each sprite in the atlas
        TextureUVAnimation MakeGPUTextureAnim(List<Sprite> sprites)
        {
            TextureUVAnimation anim = new TextureUVAnimation(new List<TextureUVAnimation.Frame>(), 0);
            foreach (var sprite in sprites)
            {
                var atlas_pixel_width = (float)sprite.texture.width;
                var atlas_pixel_height = (float)sprite.texture.height;
                var frame = new TextureUVAnimation.Frame();
                frame.Color = Color.white;
                frame.offset = new Vector2(sprite.rect.xMin / atlas_pixel_width, sprite.rect.yMin / atlas_pixel_height);
                frame.tiling = new Vector2(sprite.rect.width / atlas_pixel_width, sprite.rect.height / atlas_pixel_height);
                anim.animation.Add(frame);
            }
            return anim;
        }

        // Start is called before the first frame update
        void Start()
        {
            List<TextureUVAnimation> anims = new List<TextureUVAnimation>();
            anims.Add(MakeGPUTextureAnim(this.Explosion));

            this.m = new MeshInstancer();
            this.m.Initialize(max_parent_depth: 1, pathCount: 2, override_mesh: BaseMeshLibrary.CreatePlane());
            this.p = new PathArrayHelper(this.m);

            var tlib = new TextureAnimationLibrary(anims);
            this.m.SetAllTextureAnimations(tlib);
            this.m.FrustumCamera = this.FrustumCamera;

            var explosion_mesh_type = this.m.AddNewMeshType(this.m.Default.shared_mesh, Instantiate(this.ExplosionMaterial));

            float f = 80.0f;
            for (int i = 0; i < N; i++)
            {
                this.instances[i] = new InstanceData<InstanceProperties>(explosion_mesh_type);
                this.instances[i].position = new Vector3(Random.Range(-f, f), Random.Range(-f, f), Random.Range(-f, f));
                this.instances[i].props_animationID = tlib.Animations[0].animation_id;
                this.instances[i].props_IsTextureAnimation = true;
                this.instances[i].props_AnimationSeconds = Random.Range(0, 1.0f);
                this.instances[i].props_AnimationSpeed = 2.0f;

                var path = new Path(2, this.m, look_at_camera: true); // instruct instance to look at camera via path API
                this.p.InitializePath(ref path);
                this.p.SetPathStaticPosition(path, this.instances[i].position, Vector3.up); // just set a not-moving path
                this.p.UpdatePath(ref path);
                this.instances[i].SetPath(path, this.m);
                this.paths[i] = path;

                this.m.Initialize(ref this.instances[i]);
                this.m.Append(ref this.instances[i]);
            }
        }

        // Update is called once per frame
        void Update()
        {
            this.m.Update(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (this.m != null) this.m.Dispose();
        }
    }
}
