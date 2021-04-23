using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace GPUAnimation
{
    /// <summary>
    /// A bone animation
    /// </summary>
    [System.Serializable]
    public class BoneAnimation
    {
        /// <summary>
        /// Raw data for animation. Data layout: |NumFrames sizeof(int)| ANim length sizeof(float)|Frame0: Pos,Rot,Scale, sizeof(10 floats)|Frame1: Pos,Rot,Scale, sizeof(10 floats)|...|FrameN: Pos,Rot,Scale, sizeof(10 floats)|
        /// </summary>
        [SerializeField]
        public float[] data;
        [System.NonSerialized]
        int _id = -1;
        /// <summary>
        /// gpu bone animation id. This value is invalid until its parent AnimationController is initialized by the instancemesh object
        /// </summary>
        public int id { get { return _id; } }
        /// <summary>
        /// Number of key frames in animation
        /// </summary>
        public int KeyFrameCount
        {
            get { return this.data == null ? 0 : (int)this.data[(int)args.key_frame_count]; }
            set { if (this.data != null) this.data[(int)args.key_frame_count] = value; }
        }
        /// <summary>
        /// length of animation in seconds
        /// </summary>
        public float AnimationLengthSeconds
        {
            get
            {
                return this.data[(int)args.anim_length_seconds] * 0.01f;
            }
            set
            {
                if (value > 1000)
                    throw new System.Exception("Error, animation cannot be longer than 1000 seconds.");
                this.data[(int)args.anim_length_seconds] = (int)(value * 100);
            }
        }
        /// <summary>
        /// length of animatiom in ticks
        /// </summary>
        public uint AnimationTickLength
        {
            get
            {
                // The animation clip length is (LengthInSeconds*100). To convert to 'ticks' seconds must be multiplied by '10000'. ... 10000/100 = 100. So it can just be multiplied by 100 to convert!
                return ((uint)this.data[(int)args.anim_length_seconds]) * 100;
            }
        }

        /// <summary>
        /// animation data stride (sizeof(float))
        /// </summary>
        const int animation_stride = 10;
        /// <summary>
        /// index to start adding animation data at. Indices 0...StartDataIndex-1 are for animation arguments
        /// </summary>
        public int StartDataIndex { get { return arg_count; } }
        const int arg_count = 2;

        public bool IsInitialize { get { return id != -1; } }
        internal void SetID(int value)
        {
            if (IsInitialize)
                throw new System.Exception("Error, bone animation already initialized.");
            _id = value;
        }

        /// <summary>
        /// initialize animation with num frames and length in seconds
        /// </summary>
        public void Init(int KeyFrameCount, float animation_time)
        {
            if (KeyFrameCount == 0)
                throw new System.Exception("Error, cannot have zero key frame count. Please add key frames to your animation.");

            data = new float[animation_stride * KeyFrameCount + StartDataIndex];
            this.KeyFrameCount = KeyFrameCount;
            this.AnimationLengthSeconds = animation_time;
        }
        /// <summary>
        /// Set this animation at the input keyframe index using the local position & rotation of the transform
        /// </summary>
        /// <param name="t"></param>
        /// <param name="index"></param>
        public void Set(Transform t, int index)
        {
            int pos = StartDataIndex + animation_stride * index;
            data[pos] = t.localPosition.x;
            data[pos + 1] = t.localPosition.y;
            data[pos + 2] = t.localPosition.z;
            data[pos + 3] = t.localRotation.x;
            data[pos + 4] = t.localRotation.y;
            data[pos + 5] = t.localRotation.z;
            data[pos + 6] = t.localRotation.w;
            data[pos + 7] = t.localScale.x;
            data[pos + 8] = t.localScale.y;
            data[pos + 9] = t.localScale.z;
        }
        /// <summary>
        /// Set this animation at the input keyframe index using the world transform of 't' relative to an input transform
        /// </summary>
        /// <param name="t"></param>
        /// <param name="index"></param>
        public void SetRelative(Transform t, Transform relative_to_this, int index)
        {
            //var rtts = relative_to_this.lossyScale;
            //var rpos = relative_to_this.worldToLocalMatrix.MultiplyPoint3x4(t.position);
            //var rrot = Quaternion.Inverse(relative_to_this.rotation) * t.rotation;
            //var rscale = !t.IsChildOf(relative_to_this) ? t.lossyScale : new Vector3(t.lossyScale.x / rtts.x, t.lossyScale.y / rtts.y, t.lossyScale.z / rtts.z);

            var new_m = relative_to_this.worldToLocalMatrix * Matrix4x4.TRS(t.position, t.rotation, t.lossyScale);
            var rpos = new_m.GetColumn(3);
            var rrot = new_m.rotation;
            var rscale = new_m.lossyScale;

            int pos = StartDataIndex + animation_stride * index;
            data[pos] = rpos.x;
            data[pos + 1] = rpos.y;
            data[pos + 2] = rpos.z;
            data[pos + 3] = rrot.x;
            data[pos + 4] = rrot.y;
            data[pos + 5] = rrot.z;
            data[pos + 6] = rrot.w;
            data[pos + 7] = rscale.x;
            data[pos + 8] = rscale.y;
            data[pos + 9] = rscale.z;
        }
        public Vector3 BonePosition(int keyFrame)
        {
            var pos = StartDataIndex + animation_stride * keyFrame;
            return new Vector3(data[pos], data[pos + 1], data[pos + 2]);
        }
        public Quaternion BoneRotation(int keyFrame)
        {
            var pos = StartDataIndex + animation_stride * keyFrame;
            return new Quaternion(data[pos + 3], data[pos + 4], data[pos + 5], data[pos + 6]);
        }
        public Vector3 BoneScale(int keyFrame)
        {
            var pos = StartDataIndex + animation_stride * keyFrame;
            return new Vector3(data[pos + 7], data[pos + 8], data[pos + 9]);
        }

        public IEnumerable<Vector3> BonePositions
        {
            get
            {
                var c = KeyFrameCount;
                for (int i = 0; i < c; i++)
                    yield return BonePosition(i);
            }
        }
        public IEnumerable<Quaternion> BoneRotations
        {
            get
            {
                var c = KeyFrameCount;
                for (int i = 0; i < c; i++)
                    yield return BoneRotation(i);
            }
        }
        public IEnumerable<Vector3> BoneScales
        {
            get
            {
                var c = KeyFrameCount;
                for (int i = 0; i < c; i++)
                    yield return BoneScale(i);
            }
        }

        /// <summary>
        /// Interpolate animation value. The input value should range from 0 to 0.9999
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public Vector3 InterpPosition(float t)
        {
            //if one keyframe or less then just return
            if (KeyFrameCount <= 1)
                return BonePosition(0);

            int k_count = KeyFrameCount - 1;
            int k_start = (int)(t * (float)(k_count));
            int k_stop = k_start + 1;
            t = (t * (float)(k_count)) - (float)(int)(t * (float)(k_count));

            //get bone positions
            var start = BonePosition(k_start);
            var stop = BonePosition(k_stop);

            //return interpolation
            return new Vector3(
                t * stop.x + (1 - t) * start.x,
                t * stop.y + (1 - t) * start.y,
                t * stop.z + (1 - t) * start.z);
        }
        /// <summary>
        /// Interpolate animation value. The input value should range from 0 to 0.9999
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public Vector3 InterpScale(float t)
        {
            //if one keyframe or less hten just return
            if (KeyFrameCount <= 1)
                return BoneScale(0);

            int k_count = KeyFrameCount - 1;
            int k_start = (int)(t * (float)(k_count));
            int k_stop = k_start + 1;
            t = (t * (float)(k_count)) - (float)(int)(t * (float)(k_count));

            //get bone positions
            var start = BoneScale(k_start);
            var stop = BoneScale(k_stop);

            //return interpolation
            return new Vector3(
                t * stop.x + (1 - t) * start.x,
                t * stop.y + (1 - t) * start.y,
                t * stop.z + (1 - t) * start.z);
        }
        /// <summary>
        /// SLerp rotation, t [0...0.9999]
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public Quaternion InterpRotation(float t)
        {
            //if one keyframe or less hten just return
            if (KeyFrameCount <= 1)
                return BoneRotation(0);

            int k_count = KeyFrameCount - 1;
            int k_start = (int)(t * (float)(k_count));
            int k_stop = k_start + 1;
            t = (t * (float)(k_count)) - (float)(int)(t * (float)(k_count));

            //get bone positions
            var start = BoneRotation(k_start);
            var stop = BoneRotation(k_stop);
            return Slerp2(start, stop, t);
        }

        Quaternion Slerp2(Quaternion q1, Quaternion q2, float t)
        {
            Vector4 v1 = new Vector4(q1.x, q1.y, q1.z, q1.w);
            Vector4 v2 = new Vector4(q2.x, q2.y, q2.z, q2.w);
            float dt = Vector4.Dot(v1, v1);

            dt = dt < 0 ? -dt : dt;
            v2 = dt < 0 ? -v2 : v2;

            if (dt < 0.9995f)
            {
                float angle = Mathf.Acos(dt);
                float s = 1.0f / Mathf.Sqrt(1.0f - dt * dt);    // 1.0f / sin(angle)
                float w1 = Mathf.Sin(angle * (1.0f - t)) * s;
                float w2 = Mathf.Sin(angle * t) * s;
                var res1 = v1 * w1 + v2 * w2;
                return new Quaternion(res1.x, res1.y, res1.z, res1.w);
            }
            else
            {
                var lval = v1 + t * (v2 - v1);  //lerp(q1.value, q2.value, t);      
                var lval_dot = Vector4.Dot(lval, lval); // dot(x, x)
                var res2 = (1.0f / Mathf.Sqrt(lval_dot)) * lval;
                return new Quaternion(res2.x, res2.y, res2.z, res2.w);
            }
        }

        /// <summary>
        /// index of args in data buffer
        /// </summary>
        enum args
        {
            /// <summary>
            /// num of key frames in animation is data[0]
            /// </summary>
            key_frame_count = 0,
            /// <summary>
            /// length of animation is seconds
            /// </summary>
            anim_length_seconds = 1,
        }

        public override string ToString()
        {
            string s = "";
            s += "KeyFrameCount: " + KeyFrameCount + "\n";
            for (int i = 0; i < KeyFrameCount; i++)
            {
                s += "KeyFrame[" + i + "]\n";
                s += "--Position: " + BonePosition(i) + "\n";
                s += "--Rotation: " + BoneRotation(i) + "\n";
                s += "--Scale: " + BoneScale(i) + "\n";
            }
            return s;
        }
    }
}