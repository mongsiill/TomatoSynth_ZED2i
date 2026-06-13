using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Perception.GroundTruth;
using UnityEngine.Perception.GroundTruth.Consumers;
using UnityEngine.Perception.GroundTruth.LabelManagement;

public class Zed2iCocoRleMaskJsonExporter : MonoBehaviour
{
    [Header("Output")]
    public string OutputFolderName = "left_amodal_mask";

    [Header("References")]
    public Zed2iStereoCameraRig rig;

    [Header("Capture")]
    public int framesBetweenCaptures = 1;
    public int startFrame = 2;
    public int frameIndex = 0;

    private const int MaskRenderLayer = 31;
    private const string RipeLabel = "ripe";
    private const string UnripeLabel = "unripe";

    private Camera maskCamera;
    private Material maskMaterial;
    private RenderTexture renderTexture;
    private Texture2D readTexture;

    private bool captureInProgress;
    private bool cacheBuilt;

    private readonly List<TomatoTarget> cachedTomatoes = new List<TomatoTarget>();

    private void Awake()
    {
        if (rig == null)
            rig = GetComponent<Zed2iStereoCameraRig>();

        TomatoBodySubmeshExcluder.ApplyToScene(rig);

        SetupMaskCamera();
        SetupMaskMaterial();
        AllocateBuffers();
        RebuildTomatoCache();
    }

    private void OnDestroy()
    {
        ReleaseBuffers();

        if (maskMaterial != null)
            Destroy(maskMaterial);
    }

    private void LateUpdate()
    {
        if (ShouldCaptureThisFrame())
            RequestCapture();
    }

    private bool ShouldCaptureThisFrame()
    {
        if (framesBetweenCaptures <= 0)
            return false;

        if (Time.frameCount < startFrame)
            return false;

        return (Time.frameCount - startFrame) % framesBetweenCaptures == 0;
    }

    private void RequestCapture()
    {
        if (captureInProgress)
            return;

        captureInProgress = true;

        string frameId = frameIndex.ToString("D6");
        int imageId = frameIndex;
        CocoDataset coco = CaptureOneFrameCocoRleJson(imageId);

        if (coco == null)
        {
            captureInProgress = false;
            return;
        }

        StartCoroutine(WriteCaptureAtEndOfFrame(coco, frameId));
    }

    private IEnumerator WriteCaptureAtEndOfFrame(CocoDataset coco, string frameId)
    {
        yield return new WaitForEndOfFrame();

        if (!TryGetPerceptionImageFileName(out string imageFileName))
        {
            Debug.LogWarning(
                $"[COCO RLE] Skipped annotations_{frameId}.json because no Perception step was found for frame {Time.frameCount}."
            );

            captureInProgress = false;
            yield break;
        }

        coco.images[0].file_name = imageFileName;

        string outputDir = GetOutputDirectory();
        Directory.CreateDirectory(outputDir);

        string jsonPath = Path.Combine(outputDir, $"annotations_{frameId}.json");
        File.WriteAllText(jsonPath, JsonUtility.ToJson(coco, true));

        Debug.Log(
            $"[COCO RLE] Saved={jsonPath}"
        );

        frameIndex++;

        captureInProgress = false;
    }

    [ContextMenu("Rebuild Tomato Cache")]
    public void RebuildTomatoCache()
    {
        cachedTomatoes.Clear();

        foreach (Transform tr in FindObjectsOfType<Transform>(true))
        {
            if (!tr.name.StartsWith("tomato_", StringComparison.Ordinal))
                continue;

            if (HasTomatoParent(tr))
                continue;

            Renderer[] renderers = tr.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                continue;

            Labeling labeling = tr.GetComponent<Labeling>();
            if (labeling == null || !labeling.enabled)
                continue;

            string label = GetRipenessLabel(labeling);
            int categoryId = LabelToCategoryId(label);

            if (categoryId < 0)
                continue;

            cachedTomatoes.Add(new TomatoTarget
            {
                root = tr,
                objectName = tr.name,
                labeling = labeling,
                categoryId = categoryId,
                renderers = renderers
            });
        }

        cacheBuilt = true;
    }

    private CocoDataset CaptureOneFrameCocoRleJson(int imageId)
    {
        if (!ValidateReady())
            return null;

        if (!cacheBuilt || cachedTomatoes.Count == 0)
            RebuildTomatoCache();

        SetupMaskCamera();
        SetupMaskMaterial();
        AllocateBuffers();

        int width = rig.imageWidth;
        int height = rig.imageHeight;

        CocoDataset coco = CreateCocoDataset(imageId, string.Empty, width, height);
        List<TomatoTarget> candidates = GetCameraVisibleTomatoes();

        int annotationId = 1;
        int emptyMasks = 0;

        foreach (TomatoTarget target in candidates)
        {
            byte[] maskRaw = RenderSingleObjectMask(target.renderers);
            byte[] mask = FlipMaskVertically(maskRaw, width, height);

            if (!TryComputeMaskStats(mask, width, height, out int[] bbox, out int area))
            {
                emptyMasks++;
                continue;
            }

            coco.annotations.Add(new CocoAnnotation
            {
                id = annotationId,
                image_id = imageId,
                category_id = target.categoryId,
                instance_id = (int)target.labeling.instanceId,
                object_name = target.objectName,
                segmentation = EncodeBinaryMaskToCocoRle(mask, width, height),
                bbox = bbox,
                area = area,
                iscrowd = 0
            });

            annotationId++;
        }

        return coco;
    }

    private string GetOutputDirectory()
    {
        SoloEndpoint soloEndpoint = DatasetCapture.activateEndpoint as SoloEndpoint;
        if (soloEndpoint != null && !string.IsNullOrEmpty(soloEndpoint.currentPath))
            return Path.Combine(soloEndpoint.currentPath, OutputFolderName);

        return Path.Combine(rig.GetDatasetRootPath(), OutputFolderName);
    }

    private bool TryGetPerceptionImageFileName(out string imageFileName)
    {
        if (TryGetPerceptionSequenceStep(Time.frameCount, out int sequence, out int step))
        {
            imageFileName = $"step{step}.camera.png";
            return true;
        }

        imageFileName = null;
        return false;
    }

    private bool TryGetPerceptionSequenceStep(int frame, out int sequence, out int step)
    {
        var sequenceAndStep = DatasetCapture.GetSequenceAndStepFromFrame(frame);
        sequence = sequenceAndStep.Item1;
        step = sequenceAndStep.Item2;
        return sequence >= 0 && step >= 0;
    }

    private bool ValidateReady()
    {
        if (rig == null)
        {
            Debug.LogError("[COCO RLE] Zed2iStereoCameraRig is missing.");
            return false;
        }

        if (rig.leftGtCamera == null)
        {
            Debug.LogError("[COCO RLE] rig.leftGtCamera is missing.");
            return false;
        }

        return true;
    }

    private CocoDataset CreateCocoDataset(int imageId, string imageFileName, int width, int height)
    {
        return new CocoDataset
        {
            info = new CocoInfo
            {
                description = "Unity ZED2i tomato COCO RLE mask annotation",
                mask_format = "COCO compressed RLE"
            },
            images = new List<CocoImage>
            {
                new CocoImage
                {
                    id = imageId,
                    file_name = imageFileName,
                    width = width,
                    height = height
                }
            },
            categories = new List<CocoCategory>
            {
                new CocoCategory { id = 0, name = RipeLabel },
                new CocoCategory { id = 1, name = UnripeLabel }
            },
            annotations = new List<CocoAnnotation>()
        };
    }

    private List<TomatoTarget> GetCameraVisibleTomatoes()
    {
        List<TomatoTarget> candidates = new List<TomatoTarget>();

        Camera cam = rig.leftGtCamera;
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);

        foreach (TomatoTarget target in cachedTomatoes)
        {
            if (target.root == null || !target.root.gameObject.activeInHierarchy)
                continue;

            Bounds? bounds = GetMergedRendererBounds(target.renderers);
            if (!bounds.HasValue)
                continue;

            if (!GeometryUtility.TestPlanesAABB(planes, bounds.Value))
                continue;

            if (!BoundsIntersectsDepthRange(cam, bounds.Value, rig.depthMinMeters, rig.depthMaxMeters))
                continue;

            candidates.Add(target);
        }

        return candidates;
    }

    private Bounds? GetMergedRendererBounds(Renderer[] renderers)
    {
        bool hasBounds = false;
        Bounds merged = new Bounds();

        foreach (Renderer r in renderers)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                continue;

            if (!hasBounds)
            {
                merged = r.bounds;
                hasBounds = true;
            }
            else
            {
                merged.Encapsulate(r.bounds);
            }
        }

        return hasBounds ? merged : null;
    }

    private bool BoundsIntersectsDepthRange(Camera cam, Bounds bounds, float minDepth, float maxDepth)
    {
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;

        Vector3[] corners =
        {
            center + new Vector3(-extents.x, -extents.y, -extents.z),
            center + new Vector3( extents.x, -extents.y, -extents.z),
            center + new Vector3(-extents.x,  extents.y, -extents.z),
            center + new Vector3( extents.x,  extents.y, -extents.z),
            center + new Vector3(-extents.x, -extents.y,  extents.z),
            center + new Vector3( extents.x, -extents.y,  extents.z),
            center + new Vector3(-extents.x,  extents.y,  extents.z),
            center + new Vector3( extents.x,  extents.y,  extents.z)
        };

        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;

        foreach (Vector3 worldPoint in corners)
        {
            float z = cam.transform.InverseTransformPoint(worldPoint).z;
            minZ = Mathf.Min(minZ, z);
            maxZ = Mathf.Max(maxZ, z);
        }

        if (maxZ <= 0f)
            return false;

        return maxZ >= minDepth && minZ <= maxDepth;
    }

    private byte[] RenderSingleObjectMask(Renderer[] renderers)
    {
        int width = rig.imageWidth;
        int height = rig.imageHeight;
        List<RendererState> states = new List<RendererState>();

        RenderTexture previousActive = RenderTexture.active;

        try
        {
            foreach (Renderer r in renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                    continue;

                states.Add(new RendererState
                {
                    renderer = r,
                    gameObject = r.gameObject,
                    oldLayer = r.gameObject.layer,
                    oldMaterials = r.sharedMaterials
                });

                r.gameObject.layer = MaskRenderLayer;

                Material[] replacementMaterials = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < replacementMaterials.Length; i++)
                    replacementMaterials[i] = maskMaterial;

                r.sharedMaterials = replacementMaterials;
            }

            maskCamera.targetTexture = renderTexture;
            maskCamera.Render();

            RenderTexture.active = renderTexture;
            readTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            readTexture.Apply(false);
        }
        finally
        {
            RenderTexture.active = previousActive;

            if (maskCamera != null)
                maskCamera.targetTexture = null;

            foreach (RendererState state in states)
            {
                if (state.gameObject != null)
                    state.gameObject.layer = state.oldLayer;

                if (state.renderer != null)
                    state.renderer.sharedMaterials = state.oldMaterials;
            }
        }

        Color32[] pixels = readTexture.GetPixels32();
        byte[] mask = new byte[pixels.Length];

        for (int i = 0; i < pixels.Length; i++)
            mask[i] = pixels[i].r > 10 ? (byte)255 : (byte)0;

        return mask;
    }

    private void SetupMaskCamera()
    {
        if (rig == null || rig.leftGtCamera == null)
            return;

        if (maskCamera == null)
        {
            GameObject go = new GameObject("CocoRleMaskCamera");
            go.transform.SetParent(transform, false);
            maskCamera = go.AddComponent<Camera>();
        }

        CopyCameraSettings(rig.leftGtCamera, maskCamera);

        maskCamera.nearClipPlane = rig.depthMinMeters;
        maskCamera.farClipPlane = rig.depthMaxMeters;
        maskCamera.clearFlags = CameraClearFlags.SolidColor;
        maskCamera.backgroundColor = Color.black;
        maskCamera.cullingMask = 1 << MaskRenderLayer;
        maskCamera.enabled = false;
        maskCamera.allowHDR = false;
        maskCamera.allowMSAA = false;
    }

    private void CopyCameraSettings(Camera source, Camera destination)
    {
        destination.transform.position = source.transform.position;
        destination.transform.rotation = source.transform.rotation;
        destination.transform.localScale = source.transform.localScale;
        destination.fieldOfView = source.fieldOfView;
        destination.aspect = source.aspect;
        destination.projectionMatrix = source.projectionMatrix;
        destination.stereoTargetEye = StereoTargetEyeMask.None;
    }

    private void SetupMaskMaterial()
    {
        if (maskMaterial != null)
            return;

        Shader shader = Shader.Find("HDRP/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader == null)
        {
            Debug.LogError("[COCO RLE] No suitable unlit shader found.");
            return;
        }

        maskMaterial = new Material(shader);
        SetMaterialColor(maskMaterial, Color.white);
    }

    private void AllocateBuffers()
    {
        if (rig == null)
            return;

        int width = rig.imageWidth;
        int height = rig.imageHeight;

        if (renderTexture != null && renderTexture.width == width && renderTexture.height == height)
            return;

        ReleaseBuffers();

        renderTexture = new RenderTexture(
            width,
            height,
            24,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.Linear
        );
        renderTexture.Create();

        readTexture = new Texture2D(width, height, TextureFormat.RGB24, false, true);
    }

    private void ReleaseBuffers()
    {
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
            renderTexture = null;
        }

        if (readTexture != null)
        {
            Destroy(readTexture);
            readTexture = null;
        }
    }

    private bool HasTomatoParent(Transform tr)
    {
        Transform parent = tr.parent;

        while (parent != null)
        {
            if (parent.name.StartsWith("tomato_", StringComparison.Ordinal))
                return true;

            parent = parent.parent;
        }

        return false;
    }

    private string GetRipenessLabel(Labeling labeling)
    {
        if (labeling == null)
            return null;

        foreach (string label in labeling.labels)
        {
            if (label == RipeLabel || label == UnripeLabel)
                return label;
        }

        return null;
    }

    private int LabelToCategoryId(string label)
    {
        if (label == RipeLabel)
            return 0;

        if (label == UnripeLabel)
            return 1;

        return -1;
    }

    private void SetMaterialColor(Material material, Color color)
    {
        if (material.HasProperty("_UnlitColor"))
            material.SetColor("_UnlitColor", color);

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    private byte[] FlipMaskVertically(byte[] source, int width, int height)
    {
        byte[] result = new byte[source.Length];

        for (int y = 0; y < height; y++)
        {
            int sourceRow = y * width;
            int resultRow = (height - 1 - y) * width;
            Array.Copy(source, sourceRow, result, resultRow, width);
        }

        return result;
    }

    private bool TryComputeMaskStats(byte[] mask, int width, int height, out int[] bbox, out int area)
    {
        int xMin = width;
        int yMin = height;
        int xMax = -1;
        int yMax = -1;
        area = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (mask[y * width + x] == 0)
                    continue;

                area++;

                if (x < xMin) xMin = x;
                if (y < yMin) yMin = y;
                if (x > xMax) xMax = x;
                if (y > yMax) yMax = y;
            }
        }

        if (area == 0)
        {
            bbox = null;
            return false;
        }

        bbox = new[] { xMin, yMin, xMax - xMin + 1, yMax - yMin + 1 };
        return true;
    }

    private CocoRle EncodeBinaryMaskToCocoRle(byte[] binaryMask, int width, int height)
    {
        List<int> counts = new List<int>();
        int runLength = 0;
        int currentValue = 0;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int value = binaryMask[y * width + x] > 0 ? 1 : 0;

                if (value == currentValue)
                {
                    runLength++;
                }
                else
                {
                    counts.Add(runLength);
                    runLength = 1;
                    currentValue = value;
                }
            }
        }

        counts.Add(runLength);

        return new CocoRle
        {
            size = new[] { height, width },
            counts = CompressCocoRleCounts(counts)
        };
    }

    private string CompressCocoRleCounts(List<int> counts)
    {
        List<char> chars = new List<char>();

        for (int i = 0; i < counts.Count; i++)
        {
            int value = counts[i];

            if (i > 2)
                value -= counts[i - 2];

            bool more = true;

            while (more)
            {
                int c = value & 0x1f;
                value >>= 5;

                bool signBitSet = (c & 0x10) != 0;

                if ((value == 0 && !signBitSet) || (value == -1 && signBitSet))
                    more = false;
                else
                    c |= 0x20;

                chars.Add((char)(c + 48));
            }
        }

        return new string(chars.ToArray());
    }

    private class TomatoTarget
    {
        public Transform root;
        public string objectName;
        public Labeling labeling;
        public int categoryId;
        public Renderer[] renderers;
    }

    private struct RendererState
    {
        public Renderer renderer;
        public GameObject gameObject;
        public int oldLayer;
        public Material[] oldMaterials;
    }

    [Serializable]
    private class CocoDataset
    {
        public CocoInfo info;
        public List<CocoImage> images;
        public List<CocoCategory> categories;
        public List<CocoAnnotation> annotations;
    }

    [Serializable]
    private class CocoInfo
    {
        public string description;
        public string mask_format;
    }

    [Serializable]
    private class CocoImage
    {
        public int id;
        public string file_name;
        public int width;
        public int height;
    }

    [Serializable]
    private class CocoCategory
    {
        public int id;
        public string name;
    }

    [Serializable]
    private class CocoAnnotation
    {
        public int id;
        public int image_id;
        public int category_id;
        public int instance_id;
        public string object_name;
        public CocoRle segmentation;
        public int[] bbox;
        public int area;
        public int iscrowd;
    }

    [Serializable]
    private class CocoRle
    {
        public int[] size;
        public string counts;
    }
}
