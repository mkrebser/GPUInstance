Shader "Instanced/instancemeshdefault_SkinnedMesh"
{
  Properties
  {
    _Color("Color", Color) = (1,1,1,1)
    _MainTex("Albedo (RGB)", 2D) = "white" {}
    _Glossiness("Smoothness", Range(0,1)) = 0.5
    _SpecGlossMap("Roughness Map", 2D) = "white" {}
    _Metallic("Metallic", Range(0,1)) = 0.0
    _MetallicGlossMap("Metallic", 2D) = "white" {}
    _BumpMap("Normal Map", 2D) = "bump" {}
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


    // NOTE* possible unity bug- sometimes it uses the wrong shader (eg, it will not use the instanced shader variant- it will use the variant without instancing which will cause nothing to render)
    //     if this happens replace the shader_feature with this: (also adding/removing other pragmas seems to fix it as well)
    //#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
    //#pragma shader_feature Blend1 Blend2 Blend3 Blend4 
    //#endif
    //
    // I have no idea why this fixes it. It should be the same regardless. Oh and if the bug happens with the new #idef defines... just switch it back. Not even kidding lol.
    // This bug tends to happen when I increase the size of the structs in buffers used by the shader. Im thinking that adding/removing the pragmas is forcing the shaders to recompile on some deeper level...

    half _Glossiness;
    half _Metallic;
    fixed4 _Color;
    sampler2D _MainTex;
    sampler2D _BumpMap;
    sampler2D _SpecGlossMap;
    sampler2D _MetallicGlossMap;

    #include "gpuinstance_includes.cginc"

    struct Input
    {
      float2 uv_MainTex; // main texture
      float2 uv_BumpMap;
      float2 uv_SpecGlossMap;
      float2 uv_MetallicGlossMap;
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
      o.Metallic = tex2D(_MetallicGlossMap, IN.uv_MetallicGlossMap).rgb * _Metallic;
      o.Smoothness = _Glossiness * tex2D(_SpecGlossMap, IN.uv_SpecGlossMap).a;
      o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_BumpMap));
    }
    ENDCG
  }
    FallBack "Diffuse"
}