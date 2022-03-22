using System.Collections.Generic;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using System;

namespace UnityEngine.Experimental.Rendering.Universal
{
    /// <summary>
    /// The queue type for the objects to render.
    /// </summary>
    public enum RenderQueueType
    {
        /// <summary>
        /// Use this for opaque objects.
        /// </summary>
        Opaque,

        /// <summary>
        /// Use this for transparent objects.
        /// </summary>
        Transparent,
    }

    [RendererFeatureInfo("Render Objects (Experimental)", false)]
    [Tooltip("Render Objects simplifies the injection of additional render passes by exposing a selection of commonly used settings.")]
    [URPHelpURL("urp-renderer-feature", "#render-objects-renderer-featurea-namerender-objects-renderer-featurea")]
    public class RenderObjects : ScriptableRendererFeature
    {
        [System.Serializable]
        public class RenderObjectsSettings
        {
            public string passTag = "RenderObjectsFeature";
            public RenderPassEvent Event = RenderPassEvent.AfterRenderingOpaques;

            public FilterSettings filterSettings = new FilterSettings();

            public Material overrideMaterial = null;
            public int overrideMaterialPassIndex = 0;

            public bool overrideDepthState = false;
            public CompareFunction depthCompareFunction = CompareFunction.LessEqual;
            public bool enableWrite = true;

            public StencilStateData stencilSettings = new StencilStateData();

            public CustomCameraSettings cameraSettings = new CustomCameraSettings();
        }

        [System.Serializable]
        public class FilterSettings
        {
            // TODO: expose opaque, transparent, all ranges as drop down
            public RenderQueueType RenderQueueType;
            public LayerMask LayerMask;
            public string[] PassNames;

            public FilterSettings()
            {
                RenderQueueType = RenderQueueType.Opaque;
                LayerMask = 0;
            }
        }

        [System.Serializable]
        public class CustomCameraSettings
        {
            public bool overrideCamera = false;
            public bool restoreCamera = true;
            public Vector4 offset;
            public float cameraFieldOfView = 60.0f;
        }

        public RenderObjectsSettings settings = new RenderObjectsSettings();

        RenderObjectsPass renderObjectsPass;

        /// <inheritdoc/>
        public override void Create()
        {
            FilterSettings filter = settings.filterSettings;

            // Render Objects pass doesn't support events before rendering prepasses.
            // The camera is not setup before this point and all rendering is monoscopic.
            // Events before BeforeRenderingPrepasses should be used for input texture passes (shadow map, LUT, etc) that doesn't depend on the camera.
            // These events are filtering in the UI, but we still should prevent users from changing it from code or
            // by changing the serialized data.
            if (settings.Event < RenderPassEvent.BeforeRenderingPrePasses)
                settings.Event = RenderPassEvent.BeforeRenderingPrePasses;

            renderObjectsPass = new RenderObjectsPass(settings.passTag, settings.Event, filter.PassNames,
                filter.RenderQueueType, filter.LayerMask, settings.cameraSettings);

            renderObjectsPass.overrideMaterial = settings.overrideMaterial;
            renderObjectsPass.overrideMaterialPassIndex = settings.overrideMaterialPassIndex;

            if (settings.overrideDepthState)
                renderObjectsPass.SetDetphState(settings.enableWrite, settings.depthCompareFunction);

            if (settings.stencilSettings.overrideStencilState)
                renderObjectsPass.SetStencilState(settings.stencilSettings.stencilReference,
                    settings.stencilSettings.stencilCompareFunction, settings.stencilSettings.passOperation,
                    settings.stencilSettings.failOperation, settings.stencilSettings.zFailOperation);
        }

        /// <inheritdoc/>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(renderObjectsPass);
        }

        internal override bool SupportsNativeRenderPass()
        {
            return true;
        }

        [Obsolete("This function is only and upgrade function used to upgrade the render objects feature")]
        internal static RenderObjects UpgradeFrom(RenderObjectsAssetLegacy asset)
        {
            var renderObjects = new RenderObjects();
            renderObjects.settings.passTag = asset.settings.passTag;
            renderObjects.settings.Event = asset.settings.Event;

            renderObjects.settings.filterSettings.RenderQueueType = asset.settings.filterSettings.RenderQueueType;
            renderObjects.settings.filterSettings.LayerMask = asset.settings.filterSettings.LayerMask;
            renderObjects.settings.filterSettings.PassNames = asset.settings.filterSettings.PassNames;

            renderObjects.settings.overrideMaterial = asset.settings.overrideMaterial;
            renderObjects.settings.overrideMaterialPassIndex = asset.settings.overrideMaterialPassIndex;

            renderObjects.settings.overrideDepthState = asset.settings.overrideDepthState;
            renderObjects.settings.depthCompareFunction = asset.settings.depthCompareFunction;
            renderObjects.settings.enableWrite = asset.settings.enableWrite;

            renderObjects.settings.stencilSettings = asset.settings.stencilSettings;

            renderObjects.settings.cameraSettings.overrideCamera = asset.settings.cameraSettings.overrideCamera;
            renderObjects.settings.cameraSettings.restoreCamera = asset.settings.cameraSettings.restoreCamera;
            renderObjects.settings.cameraSettings.offset = asset.settings.cameraSettings.offset;
            renderObjects.settings.cameraSettings.cameraFieldOfView = asset.settings.cameraSettings.cameraFieldOfView;

            return renderObjects;
        }
    }
}
