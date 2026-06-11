using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Perception.GroundTruth;
using UnityEngine.Perception.GroundTruth.Consumers;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class Zed2iAutoLineCaptureController : MonoBehaviour
{
    [Header("References")]
    public Zed2iStereoCameraRig rig;
    public Zed2iCocoRleMaskJsonExporter amodalExporter;
    public Zed2iDepthWithBodyExrExporter depthWithBodyExporter;
    public CameraCmdVelKeyboardController velocityController;

    [Header("Line Switching")]
    public float splitZ = 70f;
    public float lowerZLimit = -55f;
    public float lineSpacingX = 9.1f;
    public float fifthLineSpacingX = 13.8f;
    public int maxLineMoves = 10;

    [Header("Capture Timing")]
    public int stoppedFramesBeforeMove = 2;
    public int settleFramesAfterMove = 6;
    public bool pausePerceptionDuringMove = true;

    [Header("Completion")]
    public bool stopVelocityOnComplete = true;
    public bool stopPlayModeOnComplete = true;

    private Vector3 startPosition;
    private int lineMoveCount;
    private bool isSwitchingLine;
    private int originalAmodalFramesBetweenCaptures;
    private int originalDepthWithBodyFramesBetweenCaptures;
    private Vector3 originalLinearCmd;
    private Vector3 originalAngularCmd;
    private bool velocityPaused;
    private PerceptionCamera[] perceptionCameras;
    private bool[] originalPerceptionCameraStates;

    private static readonly FieldInfo SoloCurrentPathField =
        typeof(SoloEndpoint).GetField("m_CurrentPath", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo SoloDataGeneratedField =
        typeof(SoloEndpoint).GetField("m_DataGenerated", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo SoloRegisteredAnnotationsField =
        typeof(SoloEndpoint).GetField("m_RegisteredAnnotations", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo SoloRegisteredMetricsField =
        typeof(SoloEndpoint).GetField("m_RegisteredMetrics", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo SoloRegisteredSensorsField =
        typeof(SoloEndpoint).GetField("m_RegisteredSensors", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo SoloWriteOutJsonFileMethod =
        typeof(SoloEndpoint).GetMethod("WriteOutJsonFile", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly PropertyInfo CurrentSimulationProperty =
        typeof(DatasetCapture).GetProperty("currentSimulation", BindingFlags.Static | BindingFlags.NonPublic);

    private void Awake()
    {
        if (rig == null)
            rig = GetComponent<Zed2iStereoCameraRig>();

        TomatoBodySubmeshExcluder.ApplyToScene(rig);

        if (amodalExporter == null)
            amodalExporter = GetComponent<Zed2iCocoRleMaskJsonExporter>();

        if (depthWithBodyExporter == null)
            depthWithBodyExporter = GetComponent<Zed2iDepthWithBodyExrExporter>();

        if (depthWithBodyExporter != null && depthWithBodyExporter.rig == null)
            depthWithBodyExporter.rig = rig;

        if (velocityController == null)
            velocityController = GetComponent<CameraCmdVelKeyboardController>();

        startPosition = transform.position;

        if (amodalExporter != null)
            originalAmodalFramesBetweenCaptures = amodalExporter.framesBetweenCaptures;

        if (depthWithBodyExporter != null)
            originalDepthWithBodyFramesBetweenCaptures = depthWithBodyExporter.framesBetweenCaptures;

        CachePerceptionCameras();
    }

    private void Update()
    {
        if (isSwitchingLine || lineMoveCount >= maxLineMoves)
            return;

        if (ShouldSwitchLine())
            StartCoroutine(SwitchLineOrComplete());
    }

    private bool ShouldSwitchLine()
    {
        if (startPosition.z < splitZ)
            return transform.position.z < lowerZLimit;

        return transform.position.z < splitZ;
    }

    private IEnumerator SwitchLineOrComplete()
    {
        isSwitchingLine = true;
        PauseCapture();

        int nextMoveNumber = lineMoveCount + 1;
        if (nextMoveNumber >= maxLineMoves)
        {
            CompleteCapture();
            yield break;
        }

        yield return new WaitForEndOfFrame();

        for (int i = 0; i < stoppedFramesBeforeMove; i++)
            yield return null;

        Vector3 nextPosition = startPosition;
        nextPosition.x += GetLineSpacingX(nextMoveNumber);
        transform.position = nextPosition;
        startPosition = nextPosition;
        lineMoveCount = nextMoveNumber;

        PrepareNextSoloDirectory(lineMoveCount);

        if (amodalExporter != null)
        {
            amodalExporter.frameIndex = 0;
            amodalExporter.RebuildTomatoCache();
        }

        if (depthWithBodyExporter != null)
            depthWithBodyExporter.frameIndex = 0;

        for (int i = 0; i < settleFramesAfterMove; i++)
            yield return null;

        ResumeCapture();
        isSwitchingLine = false;
    }

    private float GetLineSpacingX(int moveNumber)
    {
        return moveNumber == 5 ? fifthLineSpacingX : lineSpacingX;
    }

    private void PauseCapture()
    {
        PauseVelocity();

        if (amodalExporter != null)
            amodalExporter.framesBetweenCaptures = 0;

        if (depthWithBodyExporter != null)
            depthWithBodyExporter.framesBetweenCaptures = 0;

        if (!pausePerceptionDuringMove)
            return;

        CachePerceptionCameras();

        for (int i = 0; i < perceptionCameras.Length; i++)
        {
            if (perceptionCameras[i] == null)
                continue;

            originalPerceptionCameraStates[i] = perceptionCameras[i].enabled;
            perceptionCameras[i].enabled = false;
        }
    }

    private void ResumeCapture()
    {
        if (amodalExporter != null)
            amodalExporter.framesBetweenCaptures = originalAmodalFramesBetweenCaptures;

        if (depthWithBodyExporter != null)
            depthWithBodyExporter.framesBetweenCaptures = originalDepthWithBodyFramesBetweenCaptures;

        if (!pausePerceptionDuringMove || perceptionCameras == null)
        {
            ResumeVelocity();
            return;
        }

        for (int i = 0; i < perceptionCameras.Length; i++)
        {
            if (perceptionCameras[i] != null)
                perceptionCameras[i].enabled = originalPerceptionCameraStates[i];
        }

        ResumeVelocity();
    }

    private void PauseVelocity()
    {
        if (velocityController == null || velocityPaused)
            return;

        originalLinearCmd = velocityController.linearCmd;
        originalAngularCmd = velocityController.angularCmd;
        velocityController.linearCmd = Vector3.zero;
        velocityController.angularCmd = Vector3.zero;
        velocityPaused = true;
    }

    private void ResumeVelocity()
    {
        if (velocityController == null || !velocityPaused)
            return;

        velocityController.linearCmd = originalLinearCmd;
        velocityController.angularCmd = originalAngularCmd;
        velocityPaused = false;
    }

    private void CachePerceptionCameras()
    {
        perceptionCameras = GetComponentsInChildren<PerceptionCamera>(true);
        originalPerceptionCameraStates = new bool[perceptionCameras.Length];

        for (int i = 0; i < perceptionCameras.Length; i++)
            originalPerceptionCameraStates[i] = perceptionCameras[i] != null && perceptionCameras[i].enabled;
    }

    private void PrepareNextSoloDirectory(int segmentIndex)
    {
        SoloEndpoint soloEndpoint = DatasetCapture.activateEndpoint as SoloEndpoint;
        if (soloEndpoint == null)
        {
            Debug.LogWarning("[ZED2iAutoLineCapture] Active Perception endpoint is not SoloEndpoint. Output folder was not advanced.");
            return;
        }

        WriteCurrentSoloMetadataFiles(soloEndpoint);

        soloEndpoint.soloDatasetName = $"solo_{segmentIndex}";

        if (SoloCurrentPathField != null)
            SoloCurrentPathField.SetValue(soloEndpoint, null);

        if (SoloDataGeneratedField != null)
            SoloDataGeneratedField.SetValue(soloEndpoint, false);

        Debug.Log($"[ZED2iAutoLineCapture] Next SOLO output directory: {soloEndpoint.currentPath}");
    }

    private void CompleteCapture()
    {
        Debug.Log($"[ZED2iAutoLineCapture] Completed after {lineMoveCount} line moves.");

        WriteCurrentSoloMetadataFiles(DatasetCapture.activateEndpoint as SoloEndpoint);

        if (stopVelocityOnComplete && velocityController != null)
        {
            velocityController.linearCmd = Vector3.zero;
            velocityController.angularCmd = Vector3.zero;
        }

        if (stopPlayModeOnComplete)
            StopPlayModeOrQuit();
    }

    private void StopPlayModeOrQuit()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void WriteCurrentSoloMetadataFiles(SoloEndpoint soloEndpoint)
    {
        if (soloEndpoint == null || SoloWriteOutJsonFileMethod == null)
            return;

        string metadataPath = soloEndpoint.metadataPath;

        WriteSoloJsonFile(soloEndpoint, metadataPath, "metadata.json", GetCurrentSimulationMetadata());
        WriteSoloJsonFile(soloEndpoint, metadataPath, "annotation_definitions.json", SoloRegisteredAnnotationsField?.GetValue(soloEndpoint));
        WriteSoloJsonFile(soloEndpoint, metadataPath, "metric_definitions.json", SoloRegisteredMetricsField?.GetValue(soloEndpoint));
        WriteSoloJsonFile(soloEndpoint, metadataPath, "sensor_definitions.json", SoloRegisteredSensorsField?.GetValue(soloEndpoint));
    }

    private object GetCurrentSimulationMetadata()
    {
        object currentSimulation = CurrentSimulationProperty?.GetValue(null);
        if (currentSimulation == null)
            return null;

        FieldInfo metadataField = currentSimulation.GetType().GetField("m_SimulationMetadata", BindingFlags.Instance | BindingFlags.NonPublic);
        return metadataField?.GetValue(currentSimulation);
    }

    private void WriteSoloJsonFile(SoloEndpoint soloEndpoint, string metadataPath, string filename, object producer)
    {
        if (producer == null)
            return;

        try
        {
            SoloWriteOutJsonFileMethod.Invoke(soloEndpoint, new[] { metadataPath, filename, producer });
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[ZED2iAutoLineCapture] Failed to write {filename}: {e.Message}");
        }
    }
}
