using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GPUInstance;

namespace GPUInstanceTest
{
    public class animtexturedemo : MonoBehaviour
    {
        public List<Sprite> RunBack;
        public List<Sprite> RunForward;
        public List<Sprite> RunLeft;
        public List<Sprite> RunRight;

        public Material material;

        private MeshInstancer m;
        private PathArrayHelper p;

        public Camera FrustumCullingCamera;

        const int N = 100;

        private GameObject follow_obj = null;
        private InstanceData<InstanceProperties> follow_instance;
        private Path follow_path;

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
            anims.Add(MakeGPUTextureAnim(this.RunBack));
            anims.Add(MakeGPUTextureAnim(this.RunForward));
            anims.Add(MakeGPUTextureAnim(this.RunLeft));
            anims.Add(MakeGPUTextureAnim(this.RunRight));

            var tlib = new TextureAnimationLibrary(anims); // make texture animation library object.. This will just combine all the above lists into a single buffer

            this.m = new MeshInstancer(); // create & initialize mesh instancer
            this.m.Initialize(override_mesh: BaseMeshLibrary.CreatePlane2Sides(), override_material: this.material, pathCount: 4); // override default cube with a two-sided plane that uses uv cutout shader

            this.m.SetAllTextureAnimations(tlib); // set all texture animations on GPU

            // initialize paths that the instances will follow
            this.p = new PathArrayHelper(this.m);  

            // Create instances
            for (int i = 0; i < N; i++)
                for (int j = 0; j < N; j++)
                {
                    // specify instance data
                    var random_anim_id = anims[Random.Range(0, anims.Count)].animation_id;
                    InstanceData<InstanceProperties> dat = new InstanceData<InstanceProperties>(this.m.Default);
                    dat.position = new Vector3(i, 0, j);
                    dat.rotation = Quaternion.Euler(-90, 0, 0);
                    dat.props_IsTextureAnimation = true;
                    dat.props_animationID = random_anim_id;
                    dat.props_AnimationSpeed = Random.Range(0.5f, 2.0f);
                    dat.props_AnimationSeconds = Random.Range(0, 1.0f);

                    // create & assign path
                    Path path = new Path(path_length: 4, m: this.m, use_constants: true);
                    this.p.InitializePath(ref path);
                    this.p.SetPathConstants(path, new Vector3(0, 0, Random.Range(-10.0f, 10.0f)), new Vector3(Random.Range(-1.0f, 1.0f), 0, Random.Range(-1.0f, 1.0f)), dat.rotation, dat.position);
                    this.p.UpdatePath(ref path);
                    dat.SetPath(path, this.m);

                    // Initialize & send instance to gpu
                    this.m.Initialize(ref dat);
                    this.m.Append(ref dat);

                    // Make a unity gameobject follow the first instance.. Just for testing CPU instance transform approximation
                    if (i==0 && j==0)
                    {
                        this.follow_obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        this.follow_obj.name = "Calculated Object Transform";
                        this.follow_obj.transform.localScale = Vector3.one * 0.25f;
                        this.follow_obj.transform.position = dat.position;
                        this.follow_obj.transform.rotation = dat.rotation;
                        this.follow_instance = dat;
                        this.follow_path = path;
                    }
                }
        }

        // Update is called once per frame
        void Update()
        {
            // set frustum culling camera
            this.m.FrustumCamera = this.FrustumCullingCamera;
            // Use the 'y' component of camera position as the LOD distance for 2d
            this.m.DistanceCullingType = instancemesh.FrustumDistanceCullingType.UNIFORM_DISTANCE;
            this.m.UniformCullingDistance = this.FrustumCullingCamera == null ? 0 : Mathf.Abs(this.FrustumCullingCamera.transform.position.y);

            this.m.Update(Time.deltaTime);

            // Calculate position & rotation of instance
            Vector3 position, direction, up;
            this.p.CalculatePathPositionDirection(this.follow_path, this.follow_instance, out position, out direction, out up);
            this.follow_obj.transform.position = position;
            this.follow_obj.transform.rotation = Quaternion.LookRotation(direction, up);
        }

        private void OnDestroy()
        {
            if (this.m != null) this.m.Dispose();
        }
    }
}
