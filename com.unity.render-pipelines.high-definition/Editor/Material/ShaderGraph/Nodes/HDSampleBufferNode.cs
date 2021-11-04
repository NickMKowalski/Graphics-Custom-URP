using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEditor.Graphing;
using UnityEditor.ShaderGraph;
using UnityEditor.ShaderGraph.Drawing.Controls;
using UnityEditor.ShaderGraph.Internal;
using UnityEngine.Rendering.HighDefinition;
using System.Reflection;
using System.Linq;

namespace UnityEditor.Rendering.HighDefinition
{
    [SRPFilter(typeof(HDRenderPipeline))]
    [Title("Input", "High Definition Render Pipeline", "HD Sample Buffer")]
    sealed class HDSampleBufferNode : AbstractMaterialNode, IGeneratesBodyCode, IGeneratesFunction, IMayRequireScreenPosition, IMayRequireDepthTexture, IMayRequireNDCPosition
    {
        const string k_ScreenPositionSlotName = "UV";
        const string k_OutputSlotName = "Output";
        const string k_SamplerInputSlotName = "Sampler";

        const int k_ScreenPositionSlotId = 0;
        const int k_OutputSlotId = 2;
        public const int k_SamplerInputSlotId = 3;

        public enum BufferType
        {
            NormalWorldSpace,
            Roughness,
            MotionVectors,
            IsSky,
            PostProcessInput,
        }

        [SerializeField]
        private BufferType m_BufferType = BufferType.NormalWorldSpace;

        [EnumControl("Source Buffer")]
        public BufferType bufferType
        {
            get { return m_BufferType; }
            set
            {
                if (m_BufferType == value)
                    return;

                m_BufferType = value;
                UpdateNodeAfterDeserialization();
                Dirty(ModificationScope.Graph);
            }
        }

        public override string documentationURL => Documentation.GetPageLink("SGNode-HD-Sample-Buffer");

        public HDSampleBufferNode()
        {
            name = "HD Sample Buffer";
            synonyms = new string[] { "normal", "motion vector", "roughness", "postprocessinput", "blit", "issky"};
            UpdateNodeAfterDeserialization();
        }

        public override bool hasPreview { get { return true; } }
        public override PreviewMode previewMode => PreviewMode.Preview2D;

        int channelCount;

        public sealed override void UpdateNodeAfterDeserialization()
        {
            AddSlot(new ScreenPositionMaterialSlot(k_ScreenPositionSlotId, k_ScreenPositionSlotName, k_ScreenPositionSlotName, ScreenSpaceType.Default));
            AddSlot(new SamplerStateMaterialSlot(k_SamplerInputSlotId, k_SamplerInputSlotName, k_SamplerInputSlotName, SlotType.Input));

            switch (bufferType)
            {
                case BufferType.NormalWorldSpace:
                    AddSlot(new Vector3MaterialSlot(k_OutputSlotId, k_OutputSlotName, k_OutputSlotName, SlotType.Output, Vector3.zero, ShaderStageCapability.Fragment));
                    channelCount = 3;
                    break;
                case BufferType.Roughness:
                    AddSlot(new Vector1MaterialSlot(k_OutputSlotId, k_OutputSlotName, k_OutputSlotName, SlotType.Output, 0, ShaderStageCapability.Fragment));
                    channelCount = 1;
                    break;
                case BufferType.MotionVectors:
                    AddSlot(new Vector2MaterialSlot(k_OutputSlotId, k_OutputSlotName, k_OutputSlotName, SlotType.Output, Vector2.zero, ShaderStageCapability.Fragment));
                    channelCount = 2;
                    break;
                case BufferType.IsSky:
                    AddSlot(new Vector1MaterialSlot(k_OutputSlotId, k_OutputSlotName, k_OutputSlotName, SlotType.Output, 0, ShaderStageCapability.Fragment));
                    channelCount = 1;
                    break;
                case BufferType.PostProcessInput:
                    AddSlot(new ColorRGBAMaterialSlot(k_OutputSlotId, k_OutputSlotName, k_OutputSlotName, SlotType.Output, Color.black, ShaderStageCapability.Fragment));
                    channelCount = 4;
                    break;
            }

            RemoveSlotsNameNotMatching(new[]
            {
                k_ScreenPositionSlotId,
                k_SamplerInputSlotId,
                k_OutputSlotId,
            });
        }

        string GetFunctionName() => $"Unity_HDRP_SampleBuffer_{bufferType}_$precision";

        public void GenerateNodeFunction(FunctionRegistry registry, GenerationMode generationMode)
        {
            // Preview SG doesn't have access to HDRP depth buffer
            if (!generationMode.IsPreview())
            {
                registry.RequiresIncludePath("Packages/com.unity.render-pipelines.high-definition/Runtime/Material/NormalBuffer.hlsl");
                registry.RequiresIncludePath("Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Builtin/BuiltinData.hlsl");

                registry.ProvideFunction(GetFunctionName(), s =>
                {
                    if (bufferType == BufferType.PostProcessInput)
                    {
                        // Declare post process input here because the property collector don't support TEXTURE_X type
                        s.AppendLine($"TEXTURE2D_X({nameof(HDShaderIDs._CustomPostProcessInput)});");
                        s.AppendLine($"SAMPLER(sampler{nameof(HDShaderIDs._CustomPostProcessInput)});");
                    }

                    s.AppendLine("$precision{1} {0}($precision2 uv, SamplerState samplerState)", GetFunctionName(), channelCount);
                    using (s.BlockScope())
                    {
                        switch (bufferType)
                        {
                            case BufferType.NormalWorldSpace:
                                s.AppendLine("uint2 pixelCoords = uint2(uv * _ScreenSize.xy);");
                                s.AppendLine("NormalData normalData;");
                                s.AppendLine("DecodeFromNormalBuffer(pixelCoords, normalData);");
                                s.AppendLine("return IsSky(pixelCoords) ? 0 : normalData.normalWS;");
                                break;
                            case BufferType.Roughness:
                                s.AppendLine("uint2 pixelCoords = uint2(uv * _ScreenSize.xy);");
                                s.AppendLine("NormalData normalData;");
                                s.AppendLine("DecodeFromNormalBuffer(pixelCoords, normalData);");
                                s.AppendLine("return IsSky(pixelCoords) ? 0 : PerceptualRoughnessToRoughness(normalData.perceptualRoughness);");
                                break;
                            case BufferType.MotionVectors:
                                s.AppendLine($"float4 motionVecBufferSample = SAMPLE_TEXTURE2D_X_LOD(_CameraMotionVectorsTexture, samplerState, uv * _RTHandleScale.xy, 0);");
                                s.AppendLine("float2 motionVec;");
                                s.AppendLine("DecodeMotionVector(motionVecBufferSample, motionVec);");
                                s.AppendLine("return motionVec;");
                                break;
                            case BufferType.IsSky:
                                s.AppendLine("return IsSky(uv) ? 1 : 0;");
                                break;
                            case BufferType.PostProcessInput:
                                s.AppendLine("return SAMPLE_TEXTURE2D_X_LOD(_CustomPostProcessInput, samplerState, uv * _RTHandlePostProcessScale.xy, 0);");
                                break;
                            default:
                                s.AppendLine("return 0.0;");
                                break;
                        }
                    }
                });
            }
            else
            {
                registry.ProvideFunction(GetFunctionName(), s =>
                {
                    s.AppendLine("$precision{1} {0}($precision2 uv)", GetFunctionName(), channelCount);
                    using (s.BlockScope())
                    {
                        switch (bufferType)
                        {
                            case BufferType.NormalWorldSpace:
                                s.AppendLine("return LatlongToDirectionCoordinate(uv);");
                                break;
                            case BufferType.MotionVectors:
                                s.AppendLine("return uv * 2 - 1;");
                                break;
                            case BufferType.Roughness:
                                s.AppendLine("return uv.x;");
                                break;
                            case BufferType.PostProcessInput:
                            default:
                                s.AppendLine("return 0.0;");
                                break;
                        }
                    }
                });
            }
        }

        public void GenerateNodeCode(ShaderStringBuilder sb, GenerationMode generationMode)
        {
            string uv = GetSlotValue(k_ScreenPositionSlotId, generationMode);
            if (generationMode.IsPreview())
            {
                sb.AppendLine($"$precision{channelCount} {GetVariableNameForSlot(k_OutputSlotId)} = {GetFunctionName()}(IN.ScreenPosition.xy);");
            }
            else
            {
                var samplerSlot = FindInputSlot<MaterialSlot>(k_SamplerInputSlotId);
                var edgesSampler = owner.GetEdges(samplerSlot.slotReference);
                var sampler = edgesSampler.Any() ? $"{GetSlotValue(k_SamplerInputSlotId, generationMode)}.samplerstate" : "s_linear_clamp_sampler";
                sb.AppendLine($"$precision{channelCount} {GetVariableNameForSlot(k_OutputSlotId)} = {GetFunctionName()}({uv}.xy, {sampler});");
            }
        }

        public bool RequiresDepthTexture(ShaderStageCapability stageCapability) => true;
        public bool RequiresNDCPosition(ShaderStageCapability stageCapability = ShaderStageCapability.All) => true;
        public bool RequiresScreenPosition(ShaderStageCapability stageCapability = ShaderStageCapability.All) => true;
    }
}
