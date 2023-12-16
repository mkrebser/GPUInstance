using UnityEditor;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using System.Collections;

namespace GPUAnimation
{
    public class EditorGPUInstanceAnimComposer : EditorWindow
    {
        /// <summary>
        /// args struct used for when the animation sampling is occuring
        /// </summary>
        struct sampling_args
        {
            public Animator animator;
            public Dictionary<Transform, int> t_pivs;
            public AnimationController anim_controller;

            public List<Transform> desired_bones;
            public Transform root;
            public List<temp_animation> animations;

            /// <summary>
            /// While sampling.. This is the current animation clip index
            /// </summary>
            public int animations_i;
            /// <summary>
            /// While sampling.. This is the current frame index
            /// </summary>
            public int times_i;
        }

        class temp_animation
        {
            /// <summary>
            /// Bone Animation instance for each bone
            /// </summary>
            public BoneAnimation[] bone_animations;
            /// <summary>
            /// Name of this animation clip
            /// </summary>
            public string name;
            /// <summary>
            /// Animation key frame times
            /// </summary>
            public List<float> frame_times;
        }

        /// <summary>
        /// model being used to compose gpu animations
        /// </summary>
        GameObject model = null;
        /// <summary>
        /// Number of samples per second to sample animations
        /// </summary>
        int samples_per_second = -1;
        /// <summary>
        /// currently sampling?
        /// </summary>
        public bool is_sampling;
        sampling_args s_args;
        /// <summary>
        /// Directory to save things to
        /// </summary>
        public string save_dir = "";

        private string[] LODStr = { "LOD0", "LOD1", "LOD2", "LOD3", "LOD4" };

        [MenuItem("Window/EditorGPUInstanceAnimComposer", false, 2000)]
        public static void ShowWindow()
        {
            //Show existing window instance. If one doesn't exist, make one.
            EditorWindow.GetWindow(typeof(EditorGPUInstanceAnimComposer));
        }

        void OnGUI()
        {

            GUILayout.Label("Model Gameobject", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            model = (GameObject)EditorGUILayout.ObjectField(model, typeof(GameObject), true);
            EditorGUILayout.EndHorizontal();

            if (this.model != null)
            {
                // Now do a lot of user-proofing

                var animators = model.GetComponentsInChildren<Animator>();
                if (animators == null || animators.Length != 1)
                {
                    Debug.LogWarning("[Editor GPU Anim Error] Must have one animator component in model game object hierarchy.");
                    this.model = null;
                    return;
                }
                var animator = animators[0];

                var scene_anims = GameObject.FindObjectsByType<Animator>(FindObjectsSortMode.None);
                if (scene_anims == null || scene_anims.All(x => x != animator))
                {
                    Debug.LogWarning(string.Format("[Editor GPU Anim Error] Object: [{0}] must be loaded in the scene hierarchy!", model.name == null ? "" : model.name));
                    this.model = null;
                    return;
                }

                if (animator.runtimeAnimatorController == null)
                {
                    Debug.LogWarning(string.Format("[Editor GPU Anim Error] No animation controller ", animator.name));
                    this.model = null;
                    return;
                }

                if (animator.runtimeAnimatorController.animationClips == null || animator.runtimeAnimatorController.animationClips.Length == 0)
                {
                    Debug.LogWarning(string.Format("[Editor GPU Anim Error] No animation clips found for the input animator. [{0}] ", animator.name));
                    this.model = null;
                    return;
                }
                if (animator.runtimeAnimatorController.animationClips.Any(x => x.length > 1000))
                {
                    Debug.LogWarning(string.Format("[Editor GPU Anim Error] An animation clip on [{0}] exceeds 1000 seconds. Clips longer than 1000 seconds are not supported.", animator.name));
                    this.model = null;
                    return;
                }

                if (new HashSet<string>(animator.runtimeAnimatorController.animationClips.Select(x => x.name)).Count != animator.runtimeAnimatorController.animationClips.Length)
                {
                    Debug.LogWarning("[Editor GPU Anim Error] All animation clips for the animator must have unique names.");
                    this.model = null;
                    return;
                }

                var skinned_meshes = GetOrderedSkinnedMeshInChildren(this.model.transform);
                if (skinned_meshes == null || skinned_meshes.Count <= 0 || skinned_meshes.Any(x => x.bones == null || x.bones.Length == 0))
                {
                    Debug.LogWarning("[Editor GPU Anim Error] Must have atleast one skinned mesh in model game object hierarchy with atleast 1 bone.");
                    this.model = null;
                    return;
                }
                if (new HashSet<Transform>(skinned_meshes.Select(x => x.transform)).Count != skinned_meshes.Count)
                {
                    // Not technically required but is good practice & will have a nicely defined mesh ordering for when it gets instanced
                    Debug.LogWarning("[Editor GPU Anim Error] Each Skinned mesh must have its own gameobject!");
                    this.model = null;
                    return;
                }

                var scene_transforms = new HashSet<Transform>(GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None));
                var model_transforms = model.GetComponentsInChildren<Transform>();
                if (model_transforms.Any(x => !scene_transforms.Contains(x)))
                {
                    Debug.LogWarning(string.Format("[Editor GPU Anim Error] All objects in hierarchy for [{0}] must be loaded in the scene!", model.name == null ? "" : model.name));
                    this.model = null;
                    return;
                }

                var root = skinned_meshes[0].rootBone;
                if (skinned_meshes.All(x => x.rootBone != root))
                {
                    Debug.LogWarning("[Editor GPU Anim Error] All skinned mesh must have the same root bone.");
                    this.model = null;
                    return;
                }

                // make a list of all bones for the model
                HashSet<Transform> bones_set = new HashSet<Transform>();
                foreach (var skin in skinned_meshes)
                    foreach (var bone in skin.bones)
                        bones_set.Add(bone);

                var bones = SortByHierarchyDepth(bones_set.ToList()); // sort bones by hierarchy depth
                var bone_names_list = bones.Select(x => x.name).ToList();
                if (new HashSet<string>(bone_names_list).Count != bone_names_list.Count)
                {
                    Debug.LogWarning("[Editor GPU Anim Error] All bone names must be unique.");
                    this.model = null;
                    return;
                }
                if (bones.Count > 256)
                {
                    Debug.LogWarning("[Editor GPU Anim Error] Too many bones on this model. Please reduce bone count to below 256.");
                    this.model = null;
                    return;
                }

                var lod_groups = model.GetComponentsInChildren<LODGroup>();
                if (lod_groups != null && lod_groups.Any(x => x.lodCount > 5))
                {
                    Debug.LogWarning("[Editor GPU Anim Error] The maximum supported lodcount is 5! It should be LOD0,LOD1,LOD2,LOD3,LOD4. Please remove any additional LOD models.");
                    this.model = null;
                    return;
                }

                var skel_lods = model.GetComponentsInChildren<GPUSkeletonLODComponent>();
                if (skel_lods != null && skel_lods.Length > 1)
                {
                    Debug.LogWarning("[Editor GPU Anim Error] Found more than one GPUSkeletonLODComponent in the input model heirarchy!");
                    this.model = null;
                    return;
                }

                // Get bone lods
                int[] bone_lods = new int[bones.Count];
                for (int i = 0; i < bones.Count; i++) { bone_lods[i] = GPUInstance.instancemesh.NumLODLevels - 1; }
                if (skel_lods != null && skel_lods.Length > 0)
                {
                    string error;
                    bone_lods = skel_lods[0].VerifyAndRetrieveBoneLODS(bones.ToArray(), out error);
                    if (error != null)
                    {
                        Debug.LogWarning(error);
                        this.model = null;
                        return;
                    }
                }

                GUILayout.Label("Animation Samples per second", EditorStyles.miniBoldLabel);
                this.samples_per_second = EditorGUILayout.IntSlider(this.samples_per_second, -1, 50);
                GUILayout.Label("Values less than or equal to zero cause automatic sampling to be used. \n Auto sampling will attempt to reduce to minimum keyframes. Note* Samples/sec is not FPS. Animations get interpolated.");
                GUILayout.Label(" ");

                if (!this.is_sampling)
                {
                    if (EditorGUILayout.DropdownButton(new GUIContent("Compose Animations", ""), FocusType.Passive))
                    {
                        Debug.Log("[Editor GPU Anim] Composing... This could take a while...");

                        this.save_dir = EditorUtility.OpenFolderPanel("Save Directory", "Assets/", "");
                        this.save_dir = FileUtil.GetProjectRelativePath(this.save_dir);

                        if (!System.IO.Directory.Exists(this.save_dir))
                        {
                            Debug.LogWarning(string.Format("[Editor GPU Anim Error] Directory [{0}] does not exist.", this.save_dir == null ? "" : this.save_dir));
                            this.model = null;
                            return;
                        }

                        s_args.animator = animator;
                        s_args.t_pivs = bones.Select((x, i) => new System.ValueTuple<Transform, int>(x.transform, i)).ToDictionary(p => p.Item1, q => q.Item2);
                        s_args.desired_bones = bones;
                        s_args.root = root;
                        s_args.anim_controller = new AnimationController();
                        s_args.animations = initialize_animations(s_args.animator, s_args.t_pivs);
                        initialize_AnimationController(s_args.anim_controller, s_args.t_pivs, s_args.animator, s_args.root, bone_lods);

                        foreach (var pair in s_args.t_pivs) // quick sanity check
                            if (bones[pair.Value] != pair.Key)
                                throw new System.Exception("[Editor GPU Anim Internal Error], bone indices do not match");

                        this.is_sampling = true;
                        AnimationMode.StartAnimationMode();
                    }
                }
                else
                {
                    GUILayout.Label("Composing Animations, Please wait...", EditorStyles.boldLabel);
                }
            }
        }

        static bool ApproximatelyEqual(in float a, in float b, in float e = 0.000001f)
        {
            return System.Math.Abs(a - b) < e;
        }

        List<temp_animation> initialize_animations(Animator animator, Dictionary<Transform, int> t_pivs)
        {
            float forced_animation_timestep = this.samples_per_second <= 0 ? -1.0f : 1.0f / this.samples_per_second;

            // This function will determine keyframe times to sample the animation s.t. all keyframes have an equal amount of time between them
            List<float> find_times(HashSet<float> unique_frame_times)
            {
                if (unique_frame_times.Count <= 0)
                    throw new System.Exception("Error, all animations should have keyframes!");
                if (unique_frame_times.Count == 1)
                    return new List<float>(unique_frame_times);

                // *Note that (int) cast is performed-> anim time truncated to 2 decimals
                List<int> times100 = new List<int>(unique_frame_times.Select(x => (int)(x * 100.0f))); // clamp to 2 decimal precision (and use integers so we can get an least common denomiator to approximate an equal timestep)

                // If the user manually specified frames
                int forced_timestep = (int)((forced_animation_timestep + 0.001f) * 100);
                if (forced_timestep > 0)
                {
                    var fmin = times100.Min();
                    var fmax = times100.Max();
                    var forced_frame_times = Enumerable.Range(fmin, fmax - fmin + 1).Where(x => x % forced_timestep == 0).Select(x => (float)x / 100.0f).ToList();
                    forced_frame_times.Sort();
                    return forced_frame_times;
                }

                // First test if the frames are approx evenly distributed.. If they are, then the same exact keyframes will be used
                List<float> dist_test = new List<float>(unique_frame_times);
                dist_test.Sort(); // sort times
                var dt = dist_test[1] - dist_test[0];
                bool frames_equal = true;
                for (int i = 1; i < dist_test.Count; i++)
                {
                    if (!ApproximatelyEqual(dist_test[i] - dist_test[i - 1], dt, e: 0.01f))
                    {
                        frames_equal = false;
                        break;
                    }
                }
                if (frames_equal)
                {
                    return dist_test;
                }

                // The frames arent evently distributed... revert to 30/samples second
                var default_timestep = (int)((1.0f/ 30.0f) * 100);
                var tmin = times100.Min();
                var tmax = times100.Max();
                var frame_times = Enumerable.Range(tmin, tmax - tmin + 1).Where(x => x % default_timestep == 0).Select(x => (float)x / 100.0f).ToList();
                frame_times.Sort();
                return frame_times;
            }

            List<temp_animation> anim_clips = new List<temp_animation>(animator.runtimeAnimatorController.animationClips.Length);

            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                //make new grouped animation
                var animation = new temp_animation();
                animation.name = clip.name;

                //build a set of unique key frame times
                HashSet<float> times_set = new HashSet<float>();
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    var frame = AnimationUtility.GetEditorCurve(clip, binding);
                    if (frame != null && frame.keys != null)
                    {
                        //add each unique time, round to 2 decimal places
                        foreach (var f in frame.keys)
                            times_set.Add(f.time);
                    }
                }

                //sort times list
                var times = find_times(times_set);

                var dt = times.Count < 2 ? 0.0f : times[1] - times[0];
                Debug.Log(string.Format("Animation [{0}] keyframe timestep: [{1}]s, numframes: [{2}]", clip.name, dt, times.Count));

                //make new grouped animations
                animation.bone_animations = Enumerable.Range(0, t_pivs.Count).Select(x => new BoneAnimation()).ToArray();
                foreach (var pair in t_pivs)
                    animation.bone_animations[pair.Value].Init(times.Count, clip.length);

                animation.frame_times = times;
                anim_clips.Add(animation);
            }

            return anim_clips;
        }

        /// <summary>
        /// Return new list sorted by hierarchy depth & child ordering for ties
        /// </summary>
        /// <param name="list"></param>
        /// <returns></returns>
        List<Transform> SortByHierarchyDepth(List<Transform> list)
        {
            int depth(Transform t)
            {
                int depth = 0;
                while (t != null) { t = t.parent; depth++; }
                return depth;
            }

            return list.Select(x => (x, depth(x))).OrderBy(x => (x.Item2, x.Item1.GetSiblingIndex())).Select(x => x.Item1).ToList();
        }

        /// <summary>
        /// Get all skinned mesh.. Sorted by hierarchy
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        List<SkinnedMeshRenderer> GetOrderedSkinnedMeshInChildren(Transform p)
        {
            var children = p.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (ReferenceEquals(null, children))
                return null;
            var result = SortByHierarchyDepth(children.Select(x => x.transform).ToList()).Select(x => x.GetComponents<SkinnedMeshRenderer>()).ToList();

            List<SkinnedMeshRenderer> skins = new List<SkinnedMeshRenderer>();
            foreach (var skin_arr in result)
                foreach (var s in skin_arr)
                    skins.Add(s);
            return skins;
        }

        /// <summary>
        /// Bones will be re-ordered by their depth & sibling index for all skeletons. This makes for consistent skeletons. For example, Root Bone will always have id=0
        /// </summary>
        /// <param name="r"></param>
        /// <param name="remap"></param>
        /// <param name="bones"></param>
        /// <param name="t_pivs"></param>
        void calc_bone_map(SkinnedMeshRenderer r, in List<Transform> desired_bones, out int[] remap)
        {
            // It is assumed all bones in 'r' are a subset of 'desired_bones'
            remap = new int[desired_bones.Count];
            for (int i = 0; i < remap.Length; i++)
                remap[i] = i; // init to assume bones are ordered

            for (int i = 0; i < r.bones.Length; i++)
            {
                remap[i] = desired_bones.IndexOf(r.bones[i]);
                if (remap[i] < 0)
                    throw new System.Exception("[Editor GPU Anim Internal Error], remapping error. Bones in skinnedmesh renderer should be a subset of desired bones set");
            }
            for (int i = 0; i < r.bones.Length; i++) // sanity check
            {
                if (desired_bones[remap[i]] != r.bones[i])
                    throw new System.Exception("[Editor GPU Anim Internal Error], Failed to reorder bones. ");
            }
        }

        void initialize_AnimationController(AnimationController group, in Dictionary<Transform, int> t_pivs, in Animator anim, in Transform root, in int[] bone_lods)
        {
            //set bone data
            group.bone_parents = new int[t_pivs.Count];
            group.bone_names = new string[t_pivs.Count];
            group.name = anim.runtimeAnimatorController.name;
            group.BoneLODLevels = new int[t_pivs.Count];
            foreach (var pair in t_pivs)
            {
                int parent_key = pair.Key.parent == null || !t_pivs.ContainsKey(pair.Key.parent) || pair.Key == root ? AnimationController.kNoBoneParentID : t_pivs[pair.Key.parent];
                group.bone_parents[pair.Value] = parent_key; // set parent bone index
                group.bone_names[pair.Value] = pair.Key.name; // set bone name
                group.BoneLODLevels[pair.Value] = bone_lods[pair.Value];
            }
        }

        /// <summary>
        /// Default pose animation
        /// </summary>
        /// <param name="t_pivs"></param>
        /// <returns></returns>
        temp_animation none_animation(Dictionary<Transform, int> t_pivs, in sampling_args s_args)
        {
            var animation = new temp_animation();
            animation.name = "none";
            //make new grouped animations
            animation.bone_animations = Enumerable.Range(0, t_pivs.Count).Select(x => new BoneAnimation()).ToArray();
            foreach (var pair in t_pivs)
            {
                animation.bone_animations[pair.Value].Init(1, 1.0f);
                animation.bone_animations[pair.Value].Set(pair.Key, 0);
            }
            return animation;
        }

        /// <summary>
        /// sample the next animation keyframe
        /// </summary>
        void NextSample()
        {
            //get arguments
            var t_pivs = s_args.t_pivs;
            var times = s_args.animations[s_args.animations_i].frame_times;
            var animation_clips = s_args.animator.runtimeAnimatorController.animationClips;
            var animation_clip = animation_clips[s_args.animations_i];
            var animation = s_args.animations[s_args.animations_i];

            //sample at each time
            var t = times[s_args.times_i];

            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(s_args.animator.gameObject, animation_clip, t);
            AnimationMode.EndSampling();
            SceneView.RepaintAll();
            
            foreach (var pair in t_pivs)
            {
                animation.bone_animations[pair.Value].Set(pair.Key, s_args.times_i); //copy new transforms
            }

            s_args.times_i++; s_args.times_i = s_args.times_i >= times.Count ? 0 : s_args.times_i; //increment times_i
            s_args.animations_i = s_args.times_i == 0 ? s_args.animations_i + 1 : s_args.animations_i; //increment animations_i if times_i == 0

            //if animations_i is out of bounds, then end sampling
            if (s_args.animations_i >= animation_clips.Length)
            {
                this.is_sampling = false;
                AnimationMode.StopAnimationMode();

                SaveGPUPrefab(t_pivs); // save

                s_args = new sampling_args(); // reset s_args
            }
        }

        /// <summary>
        /// Save the animation samples & copies of each skinned mesh which will work w/ gpu instance
        /// </summary>
        /// <param name="t_pivs"></param>
        void SaveGPUPrefab(in Dictionary<Transform, int> t_pivs)
        {
            var old_skinned_meshes = GetOrderedSkinnedMeshInChildren(this.model.transform); // old skinned mesh

            // make new skeleton based on skin mesh[0]
            List<Transform> bones = new List<Transform>();
            for (int i = 0; i < s_args.anim_controller.bone_parents.Length; i++)
            {
                bones.Add(new GameObject(s_args.anim_controller.bone_names[i]).transform);
                int parent = s_args.anim_controller.bone_parents[i];
                if (parent >= 0)
                    bones[i].SetParent(bones[parent]);
            }

            for (int i = 0; i < bones.Count; i++)
            {
                bones[i].localPosition = s_args.desired_bones[i].localPosition;
                bones[i].localRotation = s_args.desired_bones[i].localRotation;
                bones[i].localScale = s_args.desired_bones[i].localScale;
            }
            for (int i = 0; i < bones.Count; i++) // sanity check
            {
                if (bones[i].name != s_args.desired_bones[i].name)
                {
                    DestroyImmediate(bones[i].gameObject);
                    throw new System.Exception("[Editor GPU Anim Internal Error] Failed to build new skeleton.");
                }
            }

            // make new obj
            var GPUSkinnedMesh = new GameObject(model.name + "_gpu");
            var gpu_mesh = GPUSkinnedMesh.AddComponent<GPUInstance.GPUSkinnedMeshComponent>();
            bones[0].SetParent(GPUSkinnedMesh.transform); // parent skeleton

            s_args.animations.Insert(0, none_animation(t_pivs, s_args)); //insert none animation
            CondenseAnimation(s_args.anim_controller, s_args.animations); //condense
            s_args.anim_controller.Initialize();
            debug_print(s_args.anim_controller); //print

            gpu_mesh.lods = new List<GPUInstance.GPUSkinnedMeshComponent.GPUSkinnedMesh>();
            gpu_mesh.lods.Add(new GPUInstance.GPUSkinnedMeshComponent.GPUSkinnedMesh());
            gpu_mesh.anim = s_args.anim_controller;
            gpu_mesh.anim.unity_controller = s_args.animator.runtimeAnimatorController;

            ComposeMesh(gpu_mesh, old_skinned_meshes, bones, s_args.anim_controller);
            bool success;
            PrefabUtility.SaveAsPrefabAsset(GPUSkinnedMesh, save_dir + "/" + GPUSkinnedMesh.name + ".prefab", out success);
            DestroyImmediate(GPUSkinnedMesh);

            if (!success)
            {
                Debug.LogWarning("[Editor GPU Anim Error] SaveAsPrefabAssed Failed!");
            }

            Debug.Log("[Editor GPU Anim] Done Composing " + s_args.anim_controller.name);
        }

        /// <summary>
        /// Remove all duplicate occurences of BoneAnimations. ie, if there are different bones with the exact same animation sequence (typically no movement)- they can be reduced to just using the same sequence rather than two duplicates.
        /// </summary>
        /// <param name="group"></param>
        /// <param name="animations"></param>
        void CondenseAnimation(AnimationController group, List<temp_animation> animations)
        {
            //initialize
            group.animations = new Animation[animations.Count];
            //condense the animations
            List<BoneAnimation> unique_bone_animations = new List<BoneAnimation>();
            for (int k = 0; k < animations.Count; k++)
            {
                var a = animations[k];
                //initialize new animation
                var new_animation = new Animation();
                new_animation.name = a.name;
                new_animation.animations_indices = new int[a.bone_animations.Length];

                for (int i = 0; i < a.bone_animations.Length; i++)
                {
                    //get index
                    var index = index_of_equal(unique_bone_animations, a.bone_animations[i]);
                    if (index >= 0)
                    {
                        //set to index of found object
                        new_animation.animations_indices[i] = index;
                    }
                    else
                    {
                        //set to current count and add an object
                        new_animation.animations_indices[i] = unique_bone_animations.Count;
                        unique_bone_animations.Add(a.bone_animations[i]);
                    }
                }
                //set animation data
                group.animations[k] = new_animation;
            }
            //set bone animations array
            group.bone_animations = unique_bone_animations.ToArray();
            //initialize all instances
            foreach (var a in group.animations)
                a.InitializeBoneAnimationsArray(group);
        }

        /// <summary>
        /// get the index of an equivalent value
        /// </summary>
        /// <param name="alist"></param>
        /// <param name="val"></param>
        /// <returns></returns>
        int index_of_equal(List<BoneAnimation> alist, BoneAnimation val)
        {
            for (int i = 0; i < alist.Count; i++)
            {
                if (key_frame_compare(val, alist[i]))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// compare to instance animations data and return if equal
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        bool key_frame_compare(BoneAnimation a, BoneAnimation b)
        {
            if (a.data.Length != b.data.Length)
                return false;
            for (int i = 0; i < a.data.Length; i++)
            {
                if (!Mathf.Approximately(a.data[i], b.data[i]))
                    return false;
            }
            return true;
        }

        private void Update()
        {
            if (!Application.isEditor)
            {
                return;
            }

            if (this.is_sampling)
            {
                NextSample();
            }
        }

        private void OnDestroy()
        {
            if (AnimationMode.InAnimationMode())
                AnimationMode.StopAnimationMode();
        }

        private void OnDisable()
        {
            if (AnimationMode.InAnimationMode())
                AnimationMode.StopAnimationMode();
        }

        /// <summary>
        /// print the output of composition
        /// </summary>
        /// <param name="t_pivs"></param>
        /// <param name="t_objs"></param>
        /// <param name="grouped_animation"></param>
        void debug_print(AnimationController group)
        {
            //if output is toggled then we print stuff
            var b_names = group.namedBones.ToDictionary(x => x.Value, y => y.Key);
            b_names.Add(-1, "NULL_NAME");
            var bones = "Bones:";
            for (int i = 0; i < group.BoneCount; i++)
            {
                var p = group.bone_parents[i];
                bones += string.Format("\nBone: [{0}] : {1} is child of bone: [{2}] : {3}",
                    new object[4] { group.bone_names[i], i.ToString(), b_names[p], p.ToString() });
            }
            Debug.Log(bones);

            foreach (var anim in group.animations)
                Debug.Log(anim.ToString(group));

            var all = "Instance Animation Count: " + group.bone_animations.Length.ToString();
            foreach (var a in group.bone_animations)
                all += "\n" + a.ToString();
            Debug.Log(all);

            var tags = "Bones:";
            for (int i = 0; i < group.bone_names.Length; i++)
            {
                if (group.bone_names[i] != null)
                    tags += "\n  bone : " + group.bone_names[i] + "id: " + i.ToString();
                else
                    tags += "\n  bone: [NULL] " + "id : " + i.ToString();
            }
            Debug.Log(tags);

            var atags = "Animations: ";
            for (int i = 0; i < group.animations.Length; i++)
            {
                if (group.animations[i] != null)
                    atags += "\n Animation: " + group.animations[i].name + ", id: " + i.ToString();
                else
                    atags += "\n Animation: NULL, id: " + i.ToString();
            }
            Debug.Log(atags);
        }

        /// <summary>
        /// copy array- shallow element copy
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="arr"></param>
        /// <returns></returns>
        T[] CopyArray<T>(in T[] arr)
        {
            if (ReferenceEquals(null, arr)) return null;
            var copy = new T[arr.Length];
            for (int i = 0; i < arr.Length; i++)
                copy[i] = arr[i];
            return copy;
        }

        /// <summary>
        /// Using Instantiate in editor mode doesn't actually create a new mesh. To avoid modifying the original, the mesh must created from scratch. ffs
        /// </summary>
        /// <param name="m"></param>
        /// <returns></returns>
        Mesh DeepCopy(in Mesh m, in int lod, in GPUInstance.GPUSkinnedMeshComponent c)
        {
            var copy = new Mesh();
            string name = string.Copy(m.name);
            copy.name = name.Replace("(Clone)", "") + "_skin" + c.lods[lod].skins.Count.ToString() + "_LOD" + lod.ToString();
            copy.vertices = CopyArray(m.vertices);
            copy.bindposes = CopyArray(m.bindposes);
            copy.boneWeights = CopyArray(m.boneWeights);
            copy.bounds = m.bounds;
            copy.colors = CopyArray(m.colors);
            copy.hideFlags = m.hideFlags;
            copy.indexFormat = m.indexFormat;
            copy.normals = CopyArray(m.normals);
            copy.tangents = CopyArray(m.tangents);
            copy.triangles = CopyArray(m.triangles);
            copy.uv = CopyArray(m.uv);
            copy.uv2 = CopyArray(m.uv2);
            copy.uv3 = CopyArray(m.uv3);
            copy.uv4 = CopyArray(m.uv4);
            copy.uv5 = CopyArray(m.uv5);
            copy.uv6 = CopyArray(m.uv6);
            copy.uv7 = CopyArray(m.uv7);
            copy.uv8 = CopyArray(m.uv8);
            return copy;
        }
       
        /// <summary>
        /// Using Instantiate in editor mode doesn't actually create a new mesh. To avoid modifying the original, the mesh must created from scratch. ffs
        /// </summary>
        /// <param name="m"></param>
        /// <returns></returns>
        SkinnedMeshRenderer DeepCopy(in SkinnedMeshRenderer r, in int lod, in Transform new_parent, in GPUInstance.GPUSkinnedMeshComponent c, List<Transform> new_bones, Transform new_root)
        {
            string name = string.Copy(r.name);
            var obj = new GameObject(name.Replace("(Clone)", "") + "_LOD" + lod.ToString());
            var copy = obj.AddComponent<SkinnedMeshRenderer>();
            copy.transform.SetParent(new_parent);
            copy.transform.localPosition = r.transform.localPosition;
            copy.transform.localRotation = r.transform.localRotation;
            copy.transform.localScale = r.transform.localScale;
            copy.allowOcclusionWhenDynamic = r.allowOcclusionWhenDynamic;
            copy.bones = new_bones.ToArray();
            copy.rootBone = new_root;
            copy.forceMatrixRecalculationPerRender = r.forceMatrixRecalculationPerRender;
            copy.hideFlags = r.hideFlags;
            copy.lightmapIndex = r.lightmapIndex;
            copy.lightmapScaleOffset = r.lightmapScaleOffset;
            copy.lightProbeProxyVolumeOverride = r.lightProbeProxyVolumeOverride;
            copy.lightProbeUsage = r.lightProbeUsage;
            copy.localBounds = r.localBounds;
            copy.motionVectorGenerationMode = r.motionVectorGenerationMode;
            copy.name = r.name;
            copy.probeAnchor = r.probeAnchor;
            copy.quality = r.quality;
            copy.rayTracingMode = r.rayTracingMode;
            copy.realtimeLightmapIndex = r.realtimeLightmapIndex;
            copy.realtimeLightmapScaleOffset = r.realtimeLightmapScaleOffset;
            copy.receiveShadows = r.receiveShadows;
            copy.reflectionProbeUsage = r.reflectionProbeUsage;
            copy.rendererPriority = r.rendererPriority;
            copy.renderingLayerMask = r.renderingLayerMask;
            copy.shadowCastingMode = r.shadowCastingMode;
            copy.sharedMaterial = r.sharedMaterial;
            copy.sharedMaterials = CopyArray(r.sharedMaterials);
            copy.sharedMesh = DeepCopy(r.sharedMesh, lod, c);
            copy.skinnedMotionVectors = r.skinnedMotionVectors;
            copy.sortingLayerID = r.sortingLayerID;
            copy.sortingLayerName = r.sortingLayerName;
            copy.sortingOrder = r.sortingOrder;
            copy.staticShadowCaster = r.staticShadowCaster;
            copy.tag = r.tag;
            copy.updateWhenOffscreen = r.updateWhenOffscreen;
            return copy;
        }

        /// <summary>
        /// Remap bone index if the bone is not supported for the input LOD level
        /// </summary>
        /// <returns></returns>
        int RemapBoneIndexForLOD(int bone, int lod, AnimationController controller)
        {
            while (bone != AnimationController.kNoBoneParentID && controller.BoneLODLevels[bone] < lod)
            {
                bone = controller.bone_parents[bone];
            }
            return bone;
        }

        void ComposeMesh(GPUInstance.GPUSkinnedMeshComponent c, List<SkinnedMeshRenderer> skinned_meshes, List<Transform> bones, AnimationController controller)
        {
            Dictionary<SkinnedMeshRenderer, (int, int)> skin2_lodAndIndex = new Dictionary<SkinnedMeshRenderer, (int, int)>(); // skin -> (lod, skin index) map
            Dictionary<string, int> skin_indices = new Dictionary<string, int>(); // number of different types of skins (partial skin name -> skin index)
            foreach (var skin in skinned_meshes)
            {
                // retrieve lod level (uses end of string= LOD0, LOD1, LOD2, LOD3, LOD4)
                int lod = 0;
                string upper = skin.name.ToUpper();
                for (int i = 0; i < GPUInstance.instancemesh.NumLODLevels; i++)
                {
                    if (upper.EndsWith(this.LODStr[i])) { lod = i; break; }
                }
                string skin_name = skin.name.Substring(0, skin.name.Length - 4); // skin name without LOD# appended

                if (!skin_indices.ContainsKey(skin_name)) // add to skin name -> index map. They will just be assigned skin index in the order they are read from the input list.
                    skin_indices.Add(skin_name, skin_indices.Count);

                skin2_lodAndIndex.Add(skin, (lod, skin_indices[skin_name])); // add to individual skin -> (lod, index) map
            }
            int num_uniqe_skins = skin_indices.Count;
            int num_lods = skin2_lodAndIndex.Max(x => x.Value.Item1)+1;

            // Initialize GPUSkinnedMeshComponent
            c.lods = new List<GPUInstance.GPUSkinnedMeshComponent.GPUSkinnedMesh>(num_lods);
            for (int i = 0; i < num_lods; i++) c.lods.Add(new GPUInstance.GPUSkinnedMeshComponent.GPUSkinnedMesh());
            foreach (var g in c.lods)
            {
                g.skins = new List<SkinnedMeshRenderer>();
                g.skins.AddRange(Enumerable.Repeat<SkinnedMeshRenderer>(null, num_uniqe_skins));
            }

            // Make a copy of each skinned mesh- apply changes to each mesh & save them as new assets
            foreach (var skin in skinned_meshes)
            {
                // remap for each skinend mesh ffs
                int[] remap = null;
                calc_bone_map(skin, s_args.desired_bones, out remap);

                (int, int) lodAndindex = skin2_lodAndIndex[skin];

                // Pack bone weights and indices into the '1' UV channel- this will be used by the vertex animation shader
                List<Vector4> uv1 = new List<Vector4>();
                List<BoneWeight> new_bws = new List<BoneWeight>();
                var bone_weights = skin.sharedMesh.boneWeights;
                for (int i = 0; i < skin.sharedMesh.vertexCount; i++)
                {
                    var bw = bone_weights[i];

                    // Apply bone remap (re-ordering to depth-sibling index order)
                    bw.boneIndex0 = remap[bw.boneIndex0];
                    bw.boneIndex1 = remap[bw.boneIndex1];
                    bw.boneIndex2 = remap[bw.boneIndex2];
                    bw.boneIndex3 = remap[bw.boneIndex3];

                    // Apply bone LOD remapping (remapping to bones that animate for the desired LOD)
                    bw.boneIndex0 = RemapBoneIndexForLOD(bw.boneIndex0, lodAndindex.Item1, controller);
                    bw.boneIndex1 = RemapBoneIndexForLOD(bw.boneIndex1, lodAndindex.Item1, controller);
                    bw.boneIndex2 = RemapBoneIndexForLOD(bw.boneIndex2, lodAndindex.Item1, controller);
                    bw.boneIndex3 = RemapBoneIndexForLOD(bw.boneIndex3, lodAndindex.Item1, controller);

                    new_bws.Add(bw);

                    var x = Pack2(bw.boneIndex0, bw.boneIndex1);
                    var y = Pack2(bw.boneIndex2, bw.boneIndex3);
                    var z = Pack2f(bw.weight0, bw.weight1);
                    var w = Pack2f(bw.weight2, bw.weight3);
                    uv1.Add(new Vector4(x, y, z, w));
                }

                // copy the shared mesh
                var copy = DeepCopy(r: skin, lod: lodAndindex.Item1, new_parent: c.gameObject.transform, c: c, new_bones: bones, new_root: bones[0]);
                copy.sharedMesh.SetUVs(1, uv1);

                // copy bind poses
                Matrix4x4[] new_bposes = new Matrix4x4[bones.Count];
                for (int i = 0; i < new_bposes.Length; i++)
                    new_bposes[i] = Matrix4x4.identity; // init to identity
                for (int i = 0; i < new_bposes.Length; i++)
                { // remap bind poses
                    if (i < skin.bones.Length) new_bposes[remap[i]] = skin.sharedMesh.bindposes[i];
                }
                copy.sharedMesh.bindposes = new_bposes;
                copy.sharedMesh.boneWeights = new_bws.ToArray();


                c.lods[lodAndindex.Item1].skins[lodAndindex.Item2] = copy;

                AssetDatabase.CreateAsset((Mesh)copy.sharedMesh, save_dir + "/" + copy.sharedMesh.name + ".asset");
            }
            AssetDatabase.SaveAssets();
        }

        float Pack2(int b1, int b2) // pack integers from 0....999 (lossless)
        {
            if (b1 > 999 || b2 > 999 || b1 < 0 || b2 < 0) throw new System.Exception("Invalid");
            return b1 * 1000.0f + (float)b2;
        }
        Vector2Int Unpack2(float v)
        {
            var v1 = v / 1000.0f;
            return new Vector2Int((int)(v1), (int)(v - ((int)v1) * 1000));
        }
        float Pack2f(float b1, float b2) // pack 0...1 floats with up to 0.001 precision
        {
            if (b1 < 0 || b2 < 0 || b1 > 1.0f || b2 > 1.0f) throw new System.Exception("Invalid");
            return Pack2((int)(999 * b1), (int)(999 * b2));
        }
        Vector2 Unpack2f(float v)
        {
            var res = Unpack2(v);
            return new Vector2(res.x, res.y) * (1.0f / 999.0f);
        }
    }
}