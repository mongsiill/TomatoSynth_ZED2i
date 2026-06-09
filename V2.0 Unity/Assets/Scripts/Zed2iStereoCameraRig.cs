using System;
using System.IO;
using UnityEngine;
using UnityEngine.Perception.GroundTruth;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class Zed2iStereoCameraRig : MonoBehaviour
{
    [Header("Dataset Root")]
    public string datasetRoot = "dataset";

    [Header("Resolution")]
    [Min(1)] public int imageWidth = 1920;
    [Min(1)] public int imageHeight = 1080;

    [Header("ZED 2i-like Stereo Setup")]
    [Min(0.001f)] public float baselineMeters = 0.12f;
    [Range(1f, 179f)] public float horizontalFovDeg = 110f;
    [Range(1f, 179f)] public float verticalFovDeg = 70f;

    [Header("Clip Range")]
    [Min(0.01f)] public float nearClip = 0.3f;

    [Tooltip("RGB camera far clip. Use large value so greenhouse/floor/background remain visible.")]
    public float rgbFarClip = 100f;

    [Tooltip("GT camera far clip. Depth, bbox, visible mask, and amodal mask are based on this camera.")]
    public float gtFarClip = 20f;

    [Header("Dataset Valid Depth Range")]
    public float depthMinMeters = 0.3f;
    public float depthMaxMeters = 20f;

    [Header("Perception Auto Setup")]
    public bool autoAddPerceptionCamera = true;

    [Min(0)]
    public int perceptionStartAtFrame = 1;

    [Header("Camera References")]
    public Camera leftRgbCamera;
    public Camera rightRgbCamera;
    public Camera leftGtCamera;

    private void OnValidate()
    {
        if (rgbFarClip <= nearClip) rgbFarClip = nearClip + 0.1f;
        if (gtFarClip <= nearClip) gtFarClip = nearClip + 0.1f;
        if (depthMaxMeters <= depthMinMeters) depthMaxMeters = depthMinMeters + 0.1f;

        if (leftRgbCamera != null) ConfigureCamera(leftRgbCamera, rgbFarClip);
        if (rightRgbCamera != null) ConfigureCamera(rightRgbCamera, rgbFarClip);
        if (leftGtCamera != null) ConfigureCamera(leftGtCamera, gtFarClip);
    }

    [ContextMenu("Setup ZED2i Stereo Rig")]
    public void SetupStereoRig()
    {
        leftRgbCamera = GetOrCreateCamera("LeftCamera_RGB");
        rightRgbCamera = GetOrCreateCamera("RightCamera_RGB");
        leftGtCamera = GetOrCreateCamera("LeftCamera_GT");

        // Rig origin = left camera optical center
        SetupCameraTransform(leftRgbCamera, Vector3.zero);
        SetupCameraTransform(leftGtCamera, Vector3.zero);

        // Right RGB camera only for stereo RGB pair
        SetupCameraTransform(rightRgbCamera, new Vector3(baselineMeters, 0f, 0f));

        ConfigureCamera(leftRgbCamera, rgbFarClip);
        ConfigureCamera(rightRgbCamera, rgbFarClip);
        ConfigureCamera(leftGtCamera, gtFarClip);

        SetupDisplayOutput();

        if (autoAddPerceptionCamera)
            SetupPerceptionCameras();

        CreateDatasetDirectories();
        SaveCameraParams();

        Debug.Log("[ZED2iStereoCameraRig] Setup complete. Cameras: LeftCamera_RGB, RightCamera_RGB, LeftCamera_GT");
    }

    private Camera GetOrCreateCamera(string cameraName)
    {
        Transform child = transform.Find(cameraName);

        if (child == null)
        {
            child = new GameObject(cameraName).transform;
            child.SetParent(transform, false);
        }

        if (!child.TryGetComponent(out Camera cam))
            cam = child.gameObject.AddComponent<Camera>();

        return cam;
    }

    private void SetupCameraTransform(Camera cam, Vector3 localPosition)
    {
        cam.transform.localPosition = localPosition;
        cam.transform.localRotation = Quaternion.identity;
        cam.transform.localScale = Vector3.one;
    }

    private void ConfigureCamera(Camera cam, float farClip)
    {
        Intrinsics k = ComputeIntrinsics();

        cam.ResetProjectionMatrix();

        cam.nearClipPlane = nearClip;
        cam.farClipPlane = farClip;
        cam.aspect = (float)imageWidth / imageHeight;
        cam.fieldOfView = verticalFovDeg;

        cam.stereoTargetEye = StereoTargetEyeMask.None;
        cam.allowHDR = false;
        cam.allowMSAA = false;

        cam.projectionMatrix = BuildProjectionMatrix(k, nearClip, farClip);
    }

    private void SetupDisplayOutput()
    {
        // Display 1 shows LeftCamera_RGB for monitoring.
        leftRgbCamera.enabled = true;
        leftRgbCamera.targetDisplay = 0;
        leftRgbCamera.depth = 10;

        // Keep other cameras enabled for Perception capture.
        rightRgbCamera.enabled = true;
        rightRgbCamera.targetDisplay = 1;
        rightRgbCamera.depth = 0;

        leftGtCamera.enabled = true;
        leftGtCamera.targetDisplay = 1;
        leftGtCamera.depth = 0;
    }

    private void SetupPerceptionCameras()
    {
        SetupSinglePerceptionCamera(leftRgbCamera, "left_rgb");
        SetupSinglePerceptionCamera(rightRgbCamera, "right_rgb");
        SetupSinglePerceptionCamera(leftGtCamera, "left_gt");

        Debug.Log("[ZED2iStereoCameraRig] Perception Camera components checked/added.");
    }

    private void SetupSinglePerceptionCamera(Camera cam, string description)
    {
        if (cam == null)
            return;

        PerceptionCamera perceptionCamera = cam.GetComponent<PerceptionCamera>();

        if (perceptionCamera == null)
            perceptionCamera = cam.gameObject.AddComponent<PerceptionCamera>();

    #if UNITY_EDITOR
        SetPerceptionCameraStartAtFrame(
            perceptionCamera,
            perceptionStartAtFrame
        );
    #endif

        Debug.Log(
            $"[ZED2iStereoCameraRig] PerceptionCamera ready: {cam.name}, " +
            $"role={description}, startAtFrame={perceptionStartAtFrame}, "
        );
    }

    #if UNITY_EDITOR
    private void SetPerceptionCameraStartAtFrame(
        PerceptionCamera perceptionCamera,
        int startAtFrame
    )
    {
        SerializedObject so = new SerializedObject(perceptionCamera);

        bool startSet = TrySetIntProperty(
            so,
            new[]
            {
                "m_StartAtFrame",
                "startAtFrame",
                "m_FirstCaptureFrame",
                "firstCaptureFrame",
                "m_StartFrame",
                "startFrame"
            },
            startAtFrame
        );

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(perceptionCamera);

        if (!startSet)
        {
            Debug.LogWarning(
                "[ZED2iStereoCameraRig] Could not find serialized property for Perception Camera Start At Frame. " +
                "Please set it manually to 1 in the Inspector."
            );
        }
    }

    private bool TrySetIntProperty(
        SerializedObject so,
        string[] candidateNames,
        int value
    )
    {
        foreach (string name in candidateNames)
        {
            SerializedProperty prop = so.FindProperty(name);

            if (prop != null && prop.propertyType == SerializedPropertyType.Integer)
            {
                prop.intValue = value;
                return true;
            }
        }

        SerializedProperty iterator = so.GetIterator();

        while (iterator.NextVisible(true))
        {
            if (iterator.propertyType != SerializedPropertyType.Integer)
                continue;

            string displayName = iterator.displayName.ToLowerInvariant();
            string propertyName = iterator.name.ToLowerInvariant();

            bool looksLikeStartAtFrame =
                (displayName.Contains("start") && displayName.Contains("frame")) ||
                (propertyName.Contains("start") && propertyName.Contains("frame")) ||
                (displayName.Contains("first") && displayName.Contains("capture")) ||
                (propertyName.Contains("first") && propertyName.Contains("capture"));

            if (looksLikeStartAtFrame)
            {
                iterator.intValue = value;
                return true;
            }
        }

        return false;
    }
    #endif

    private Intrinsics ComputeIntrinsics()
    {
        return new Intrinsics
        {
            fx = imageWidth * 0.5f / Mathf.Tan(horizontalFovDeg * Mathf.Deg2Rad * 0.5f),
            fy = imageHeight * 0.5f / Mathf.Tan(verticalFovDeg * Mathf.Deg2Rad * 0.5f),
            cx = imageWidth * 0.5f,
            cy = imageHeight * 0.5f
        };
    }

    private Matrix4x4 BuildProjectionMatrix(Intrinsics k, float near, float far)
    {
        float w = imageWidth;
        float h = imageHeight;

        Matrix4x4 m = new Matrix4x4();

        m[0, 0] = 2f * k.fx / w;
        m[1, 1] = 2f * k.fy / h;

        m[0, 2] = 1f - 2f * k.cx / w;
        m[1, 2] = 2f * k.cy / h - 1f;

        m[2, 2] = -(far + near) / (far - near);
        m[2, 3] = -(2f * far * near) / (far - near);
        m[3, 2] = -1f;

        return m;
    }

    private void CreateDatasetDirectories()
    {
        string root = GetDatasetRootPath();

        Directory.CreateDirectory(root);

        Directory.CreateDirectory(Path.Combine(root, "left_rgb"));
        Directory.CreateDirectory(Path.Combine(root, "right_rgb"));

        Directory.CreateDirectory(Path.Combine(root, "left_depth_gt"));
        Directory.CreateDirectory(Path.Combine(root, "left_mask"));
        Directory.CreateDirectory(Path.Combine(root, "left_bounding_box"));

        Directory.CreateDirectory(Path.Combine(root, "left_amodal_instance_mask"));
        Directory.CreateDirectory(Path.Combine(root, "left_amodal_class_mask"));
        Directory.CreateDirectory(Path.Combine(root, "left_amodal_overlay"));
    }

    private void SaveCameraParams()
    {
        Intrinsics k = ComputeIntrinsics();

        CameraParams p = new CameraParams
        {
            image_width = imageWidth,
            image_height = imageHeight,

            baseline_m = baselineMeters,

            horizontal_fov_deg = horizontalFovDeg,
            vertical_fov_deg = verticalFovDeg,

            near_clip_m = nearClip,
            rgb_far_clip_m = rgbFarClip,
            gt_far_clip_m = gtFarClip,

            depth_min_m = depthMinMeters,
            depth_max_m = depthMaxMeters,

            fx = k.fx,
            fy = k.fy,
            cx = k.cx,
            cy = k.cy
        };

        string path = Path.Combine(GetDatasetRootPath(), "camera_params.json");
        File.WriteAllText(path, JsonUtility.ToJson(p, true));

        Debug.Log($"[ZED2iStereoCameraRig] Saved camera params: {path}");
        Debug.Log($"[ZED2iStereoCameraRig] fx={k.fx:F3}, fy={k.fy:F3}, cx={k.cx:F3}, cy={k.cy:F3}");
    }

    public string GetDatasetRootPath()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", datasetRoot));
    }

    [Serializable]
    public class CameraParams
    {
        public int image_width;
        public int image_height;

        public float baseline_m;

        public float horizontal_fov_deg;
        public float vertical_fov_deg;

        public float near_clip_m;
        public float rgb_far_clip_m;
        public float gt_far_clip_m;

        public float depth_min_m;
        public float depth_max_m;

        public float fx;
        public float fy;
        public float cx;
        public float cy;
    }

    private class Intrinsics
    {
        public float fx;
        public float fy;
        public float cx;
        public float cy;
    }
}