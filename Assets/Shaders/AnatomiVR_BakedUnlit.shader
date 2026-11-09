Shader "AnatomiVR/BakedUnlit"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map (Combined Bake)", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _ShadingStrength("Studio Bake Shading", Range(0, 1)) = 0.65
        _Smoothness("Surface Smoothness", Range(0, 1)) = 0.2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Name "BakedUnlitForward"
            Tags { "LightMode" = "UniversalForward" }

            // İçe bakan oda duvarları (duvarlar, zemin, tavan) ve negatif ölçekli posterler için çift taraflı (Double-Sided)
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ STEREO_MULTIVIEW_ON STEREO_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 viewDirWS  : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _ShadingStrength;
                half _Smoothness;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = normInputs.normalWS;
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(posInputs.positionWS);
                return output;
            }

            half4 frag(Varyings input, FRONT_FACE_TYPE isFrontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;

                float3 N = normalize(input.normalWS);
                if (!IS_FRONT_VFACE(isFrontFace, true, false))
                {
                    N = -N;
                }
                float3 V = normalize(input.viewDirWS);

                // Sabit (statik) stüdyo aydınlatma yönleri — gerçek zamanlı ışık veya gölge kullanılmaz
                float3 keyDir  = normalize(float3(-0.35, 0.82, -0.45));
                float3 fillDir = normalize(float3(0.60, 0.35, 0.72));

                half keyLight  = saturate(dot(N, keyDir) * 0.5 + 0.5);
                half fillLight = saturate(dot(N, fillDir) * 0.5 + 0.5);
                half hemi      = saturate(N.y * 0.5 + 0.5);

                half studioRig = 0.52 + 0.32 * keyLight + 0.14 * fillLight + 0.10 * hemi;

                // Hafif speküler vurgu (metal masa ve kemik konturları için)
                float3 H = normalize(keyDir + V);
                half specPower = exp2(4.0 + _Smoothness * 5.0);
                half spec = pow(saturate(dot(N, H)), specPower) * (_Smoothness * 0.35);

                half shade = lerp(1.0h, studioRig, _ShadingStrength);
                half3 finalRGB = texColor.rgb * shade + spec;

                return half4(finalRGB, texColor.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Off
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ STEREO_MULTIVIEW_ON STEREO_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthOnlyFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
