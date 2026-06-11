using System;
using System.IO;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Perception.GroundTruth;
using UnityEngine.Perception.GroundTruth.Consumers;
using UnityEngine.Perception.GroundTruth.Sensors.Channels;
using UnityEngine.Perception.GroundTruth.Utilities;
using UnityEngine.Rendering;

public class Zed2iDepthWithBodyExrExporter : MonoBehaviour
{
    [Header("Output")]
    public string OutputFolderName = "left_depth_with_body";
    public string DepthAnnotationName = "DepthWithBody";

    [Header("References")]
    public Zed2iStereoCameraRig rig;

    [Header("Capture")]
    public int framesBetweenCaptures = 1;
    public int startFrame = 2;
    public int frameIndex = 0;

    [Header("Tomato Layer Split")]
    public bool useTomatoLayerSplit = true;
    public int originalTomatoLayer = 29;
    public int fruitOnlyGtLayer = 30;

    private DepthWithBodyLabeler labeler;

    private void Awake()
    {
        if (rig == null)
            rig = GetComponent<Zed2iStereoCameraRig>();

        InstallLabeler();
    }

    private void OnEnable()
    {
        InstallLabeler();
    }

    private void OnValidate()
    {
        originalTomatoLayer = Mathf.Clamp(originalTomatoLayer, 0, 31);
        fruitOnlyGtLayer = Mathf.Clamp(fruitOnlyGtLayer, 0, 31);
    }

    private void InstallLabeler()
    {
        PerceptionCamera perceptionCamera = GetSourcePerceptionCamera();
        if (perceptionCamera == null)
            return;

        foreach (CameraLabeler existingLabeler in perceptionCamera.labelers)
        {
            if (existingLabeler is DepthWithBodyLabeler existingDepthLabeler)
            {
                labeler = existingDepthLabeler;
                labeler.SetExporter(this);
                return;
            }
        }

        labeler = new DepthWithBodyLabeler(this);
        perceptionCamera.AddLabeler(labeler);
    }

    public bool ShouldWriteFrame(int frameCount)
    {
        if (framesBetweenCaptures <= 0)
            return false;

        if (frameCount < startFrame)
            return false;

        return (frameCount - startFrame) % framesBetweenCaptures == 0;
    }

    public int BuildDepthCullingMask(int sourceMask)
    {
        if (!useTomatoLayerSplit)
            return sourceMask;

        int mask = sourceMask;
        mask |= 1 << originalTomatoLayer;
        mask &= ~(1 << fruitOnlyGtLayer);
        return mask;
    }

    public string GetOutputDirectory()
    {
        SoloEndpoint soloEndpoint = DatasetCapture.activateEndpoint as SoloEndpoint;
        if (soloEndpoint != null && !string.IsNullOrEmpty(soloEndpoint.currentPath))
            return Path.Combine(soloEndpoint.currentPath, OutputFolderName);

        if (rig != null)
            return Path.Combine(rig.GetDatasetRootPath(), OutputFolderName);

        return Path.Combine(Application.dataPath, "..", OutputFolderName);
    }

    public string GetSourceSensorId()
    {
        PerceptionCamera perceptionCamera = GetSourcePerceptionCamera();
        if (perceptionCamera != null && !string.IsNullOrEmpty(perceptionCamera.id))
            return perceptionCamera.id;

        return "camera_0";
    }

    public PerceptionCamera GetSourcePerceptionCamera()
    {
        if (rig == null)
            rig = GetComponent<Zed2iStereoCameraRig>();

        if (rig == null || rig.leftGtCamera == null)
            return null;

        return rig.leftGtCamera.GetComponent<PerceptionCamera>();
    }
}

[Serializable]
public sealed class DepthWithBodyLabeler : CameraLabeler
{
    private const LosslessImageEncodingFormat ImageEncodingFormat = LosslessImageEncodingFormat.Exr;

    private Zed2iDepthWithBodyExrExporter exporter;
    private DepthWithBodyChannel channel;
    private RenderTexture depthTexture;

    public DepthWithBodyLabeler()
    {
    }

    public DepthWithBodyLabeler(Zed2iDepthWithBodyExrExporter exporter)
    {
        SetExporter(exporter);
    }

    public override string labelerId => exporter != null ? exporter.DepthAnnotationName : "DepthWithBody";

    public override string description =>
        "Writes a linear EXR depth image using the Perception depth channel while keeping original tomato body geometry visible.";

    protected override bool supportsVisualization => false;

    public void SetExporter(Zed2iDepthWithBodyExrExporter sourceExporter)
    {
        exporter = sourceExporter;
        UpdateChannelLayerMask();
    }

    protected override void Setup()
    {
        channel = perceptionCamera.EnableChannel<DepthWithBodyChannel>();
        channel.outputTextureReadback += OnDepthTextureReadback;
        depthTexture = channel.outputTexture;
        UpdateChannelLayerMask();
    }

    protected override void OnUpdate()
    {
        UpdateChannelLayerMask();
    }

    protected override void Cleanup()
    {
        if (channel != null)
            channel.outputTextureReadback -= OnDepthTextureReadback;

        channel = null;
        depthTexture = null;
    }

    private void UpdateChannelLayerMask()
    {
        if (exporter == null || channel == null)
            return;

        if (perceptionCamera != null)
            channel.depthLayerMask = exporter.BuildDepthCullingMask(perceptionCamera.layerMask);
        else if (exporter.rig != null && exporter.rig.leftGtCamera != null)
            channel.depthLayerMask = exporter.BuildDepthCullingMask(exporter.rig.leftGtCamera.cullingMask);
    }

    private void OnDepthTextureReadback(int frameCount, NativeArray<float4> data)
    {
        if (exporter == null || depthTexture == null || !exporter.ShouldWriteFrame(frameCount))
            return;

        var sequenceAndStep = DatasetCapture.GetSequenceAndStepFromFrame(frameCount);
        int sequence = sequenceAndStep.Item1;
        int step = sequenceAndStep.Item2;
        if (sequence < 0 || step < 0)
            return;

        string outputDir = exporter.GetOutputDirectory();
        Directory.CreateDirectory(outputDir);

        string depthFileName = $"step{step}.{exporter.GetSourceSensorId()}.{exporter.DepthAnnotationName}.exr";
        string depthPath = Path.Combine(outputDir, depthFileName);
        int width = depthTexture.width;
        int height = depthTexture.height;
        GraphicsFormat graphicsFormat = depthTexture.graphicsFormat;

        ImageEncoder.EncodeImage(
            data,
            width,
            height,
            graphicsFormat,
            ImageEncodingFormat,
            encodedImageData =>
            {
                File.WriteAllBytes(depthPath, encodedImageData.ToArray());
                exporter.frameIndex++;
                Debug.Log($"[DepthWithBody] Saved={depthPath}");
            });
    }
}

public sealed class DepthWithBodyChannel : CameraChannel<float4>
{
    private static Material depthMaterial;

    public LayerMask depthLayerMask = -1;

    public override Color clearColor => Color.clear;

    public override RenderTexture CreateOutputTexture(int width, int height)
    {
        RenderTexture texture = new RenderTexture(width, height, 32, GraphicsFormat.R32G32B32A32_SFloat)
        {
            name = "DepthWithBody Channel",
            enableRandomWrite = true,
            filterMode = FilterMode.Point
        };
        texture.Create();
        return texture;
    }

    public override void Execute(UnityEngine.Perception.GroundTruth.Sensors.CameraChannelInputs inputs, RenderTexture renderTarget)
    {
        if (depthMaterial == null)
            depthMaterial = new Material(RenderUtilities.LoadPrewarmedShader("Perception/Depth"));

        CullingResults cullingResults = inputs.cullingResults;
        if (inputs.camera.TryGetCullingParameters(out ScriptableCullingParameters cullingParameters))
        {
            cullingParameters.cullingMask = (uint)depthLayerMask.value;
            cullingResults = inputs.ctx.Cull(ref cullingParameters);
        }

        var rendererListDesc = RenderUtilities.CreateRendererListDesc(
            inputs.camera,
            cullingResults,
            depthMaterial,
            0,
            depthLayerMask);
        var rendererList = inputs.ctx.CreateRendererList(rendererListDesc);

        inputs.cmd.SetRenderTarget(renderTarget);
        inputs.cmd.ClearRenderTarget(true, true, clearColor);
        inputs.cmd.DrawRendererList(rendererList);
    }
}
