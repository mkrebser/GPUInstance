Shader "Instanced/instancemeshuv_Emissive"
{
	Properties
	{
		_MainTex("Albedo (RGB)", 2D) = "white" {}
		_Intensity("Intensity", Range(0, 3)) = 1.5
	}
		SubShader
		{
		Tags
		{
			"RenderType" = "Transparent"
			"Queue" = "Transparent"
		}
		LOD 200
		Blend SrcAlpha OneMinusSrcAlpha
		ZWrite off
		Lighting off

		CGPROGRAM
		// Upgrade NOTE: excluded shader from OpenGL ES 2.0 because it uses non-square matrices
#pragma exclude_renderers gles
		// Physically based Standard lighting model
#pragma surface surf Standard vertex:vert alpha:fade
#pragma instancing_options procedural:setup
#pragma target 5.0
#pragma multi_compile_instancing
	sampler2D _MainTex;
	float _Intensity;

	//instance data holder
#include "gpuinstance_includes.cginc"

	struct Input
	{
		float2 uv_MainTex;
		float4 tileoff;
		fixed4 color;
	};

  void setup()
  {
    do_instance_setup();
  }

	void vert(inout appdata_full v, out Input o)
	{
    UNITY_INITIALIZE_OUTPUT(Input, o);
    int id = get_instance_id();
    o.color = get_instance_color(id);
    o.tileoff = get_tile_offset(id);
	}

	void surf(Input IN, inout SurfaceOutputStandard o)
	{
		fixed4 c = tex2D(_MainTex, IN.uv_MainTex * IN.tileoff.xy + IN.tileoff.zw) * IN.color;
		o.Albedo = fixed3(0, 0, 0);
		o.Alpha = c.a;
		o.Emission = c.rgb * _Intensity;
	}

	ENDCG
	}
	FallBack "Diffuse"
}