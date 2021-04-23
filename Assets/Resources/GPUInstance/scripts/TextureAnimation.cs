using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using GPUInstance;
using System.Text;

namespace GPUInstance
{
    public class TextureAnimationLibrary
    {
        //the buffer is organized in memory as a float array, each item is a floating point value
        // 1 float: length (N) of animation | (N) floats for animation data.. offset_x,offset_y,tile_x,tile_y,(Colors 0...4),offset_x,offset_y, (Colors 0...4) etc...
        //the length of the animation excludes the the space used to store the length
        /// <summary>
        /// Texture animations buffer
        /// </summary>
        public float[] TextureAnimationBuffer { get; private set; }

        /// <summary>
        /// Invalid key (all values below 1 are invalid)
        /// </summary>
        public const int INVALID_TEXTURE_ANIMATION_ID = 0;

        /// <summary>
        /// All texture animations
        /// </summary>
        public readonly List<TextureUVAnimation> Animations;

        /// <summary>
        /// Create texture animation library using the input list.
        /// </summary>
        /// <param name="mappings"></param>
        public TextureAnimationLibrary(List<TextureUVAnimation> animations)
        {
            this.Animations = animations;

            if (ReferenceEquals(null, animations) || animations.Count == 0)
            {
                return;
            }

            //fill animations array
            var index = 1; //we always leave array[0] = 0 so that the first item can be represented as NULL(0) on the shader
            TextureAnimationBuffer = new float[1 + animations.Sum(x => x.BufferLength)];
            for (int anim_index = 0; anim_index < animations.Count; anim_index++)
            {
                var anim = animations[anim_index];

                //each tile needs 8 floats to draw the animation
                //the total length of the animation in the float array is 8 * n + 1 
                //where n is the number of tiles
                var animation_id = index;

                TextureAnimationBuffer[index] = anim.animation.Count; // Set number of frames a sfirst element in the buffer for this animation
                index += 1;

                foreach (var frame in anim.animation)
                {
                    //set buffer data
                    TextureAnimationBuffer[index] =     frame.offset.x;
                    TextureAnimationBuffer[index + 1] = frame.offset.y;
                    TextureAnimationBuffer[index + 2] = frame.tiling.x;
                    TextureAnimationBuffer[index + 3] = frame.tiling.y;
                    TextureAnimationBuffer[index + 4] = frame.Color.r;
                    TextureAnimationBuffer[index + 5] = frame.Color.g;
                    TextureAnimationBuffer[index + 6] = frame.Color.b;
                    TextureAnimationBuffer[index + 7] = frame.Color.a;
                    index += 8;
                }

                this.Animations[anim_index] = new TextureUVAnimation(animations[anim_index].animation, animation_id);
            }
        }

        public override string ToString()
        {
            if (TextureAnimationBuffer != null)
            {
                int index = 0;
                var builder = new StringBuilder();
                while (index < TextureAnimationBuffer.Length)
                {
                    var len = (int)TextureAnimationBuffer[index];
                    builder.Append("Animation Frame Count: ");
                    builder.AppendLine(len.ToString());
                    index += 1;

                    for (int j = index; j < index + (len*8); j+=8)
                    {
                        builder.AppendLine(string.Format("Tiles: {0} {1} {2} {3}",
                            (double)TextureAnimationBuffer[j],
                            (double)TextureAnimationBuffer[j + 1],
                            (double)TextureAnimationBuffer[j + 2],
                            (double)TextureAnimationBuffer[j + 3]));
                        builder.AppendLine(string.Format("Colors: {0} {1} {2} {3}",
                            (double)TextureAnimationBuffer[j + 4],
                            (double)TextureAnimationBuffer[j + 5],
                            (double)TextureAnimationBuffer[j + 6],
                            (double)TextureAnimationBuffer[j + 7]));
                    }
                    index += (len*8);
                }
                return builder.ToString();
            }
            else
                return "INVALID_BUFFER_OBJECT";
        }
    }

    /// <summary>
    /// Struct used to represent a texture animation
    /// </summary>
    public struct TextureUVAnimation
    {
        public List<Frame> animation { get; private set; }
        public int animation_id { get; private set; }

        public struct Frame
        {
            public Vector2 offset;
            public Vector2 tiling;
            public Color Color;
        }

        /// <summary>
        /// Length needed (in 4 byte floats) how much space is needed for this texture animation in gpu buffer format. 1 float for length, 8 * N for each frame.
        /// </summary>
        public int BufferLength { get { return animation.Count * 8 + 1; } }

        public TextureUVAnimation(List<Frame> animation, int animation_id)
        {
            this.animation = animation;
            this.animation_id = animation_id;
        }
    }
}
