Shader "Instanced/instancemeshdefault_SkinnedMeshDiffuse"
{
  Properties
  {
    _Color("Color", Color) = (1,1,1,1)
    _MainTex("Albedo (RGB)", 2D) = "white" {}
  }
    SubShader{
    Tags{ "RenderType" = "Opaque" }
    LOD 200

    CGPROGRAM
    // Upgrade NOTE: excluded shader from OpenGL ES 2.0 because it uses non-square matrices
#pragma exclude_renderers gles
    // Physically based Standard lighting model
#pragma surface surf Standard addshadow vertex:vert
#pragma instancing_options procedural:setup
#pragma target 5.0

#pragma shader_feature Blend1 Blend2 Blend3 Blend4 // shader features define num blend weights

    fixed4 _Color;
    sampler2D _MainTex;

#include "gpuinstance_includes.cginc"

    struct Input
    {
      float2 uv_MainTex; // main texture
    };

    void setup()
    {
      do_instance_setup();
    }

    void vert(inout appdata_full v, out Input o)
    {
      UNITY_INITIALIZE_OUTPUT(Input, o);
      int id = get_instance_id();
      anim_vertex(id, v.texcoord1, v.vertex, v.normal);
    }

    void surf(Input IN, inout SurfaceOutputStandard o)
    {
      half4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
      o.Albedo = c.rgb;
      o.Alpha = c.a;
    }
    ENDCG
  }
    FallBack "Diffuse"
}
