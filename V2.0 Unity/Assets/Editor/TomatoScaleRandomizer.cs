using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class TomatoScaleRandomizer
{
    private const string TomatoObjectPrefix = "tomato_";
    private const string FruitOnlyProxySuffix = "_FruitOnlyGt";
    private const float MinScaleMultiplier = 0.8f;
    private const float MaxScaleMultiplier = 1.1f;
    private static readonly Quaternion StemUpRotation = Quaternion.Euler(34f, 2.6f, 12.5f);
    private static readonly Vector3 LocalStemDirection = Quaternion.Inverse(StemUpRotation) * Vector3.up;

    [MenuItem("Tools/Tomato/Randomize All tomato_ Scales 0.8-1.1 Keep Stem Anchor")]
    public static void RandomizeAllTomatoScales()
    {
        int changed = 0;

        Scene scene = SceneManager.GetActiveScene();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform tr in root.GetComponentsInChildren<Transform>(true))
            {
                if (!IsTargetTomatoObject(tr))
                    continue;

                if (RandomizeScaleKeepingStemAnchor(tr))
                    changed++;
            }
        }

        if (changed > 0)
            EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log($"[Tomato Scale Randomizer] Randomized {changed} tomato_ object scales while keeping stem anchors fixed.");
    }

    [MenuItem("Tools/Tomato/Randomize Selected tomato_ Scales 0.8-1.1 Keep Stem Anchor")]
    public static void RandomizeSelectedTomatoScales()
    {
        int changed = 0;

        foreach (GameObject go in Selection.gameObjects)
        {
            Transform tr = go.transform;
            if (!IsTargetTomatoObject(tr))
                continue;

            if (RandomizeScaleKeepingStemAnchor(tr))
                changed++;
        }

        if (changed > 0)
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log($"[Tomato Scale Randomizer] Randomized {changed} selected tomato_ object scales while keeping stem anchors fixed.");
    }

    private static bool RandomizeScaleKeepingStemAnchor(Transform tr)
    {
        if (!TryGetStemAnchorLocalPoint(tr, out Vector3 localAnchorPoint))
            return false;

        float multiplier = UnityEngine.Random.Range(MinScaleMultiplier, MaxScaleMultiplier);
        Vector3 oldAnchorWorld = tr.TransformPoint(localAnchorPoint);

        Undo.RecordObject(tr, "Randomize tomato scale keeping stem anchor");
        tr.localScale *= multiplier;
        Vector3 newAnchorWorld = tr.TransformPoint(localAnchorPoint);
        tr.position += oldAnchorWorld - newAnchorWorld;
        EditorUtility.SetDirty(tr);

        return true;
    }

    private static bool TryGetStemAnchorLocalPoint(Transform tr, out Vector3 localAnchorPoint)
    {
        MeshFilter meshFilter = tr.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            localAnchorPoint = Vector3.zero;
            return false;
        }

        Mesh mesh = meshFilter.sharedMesh;
        Bounds bounds = mesh.bounds;
        Vector3 center = bounds.center;

        try
        {
            Vector3[] vertices = mesh.vertices;
            if (vertices.Length > 0)
            {
                float bestDot = float.NegativeInfinity;
                Vector3 bestVertex = vertices[0];

                foreach (Vector3 vertex in vertices)
                {
                    float dot = Vector3.Dot(vertex - center, LocalStemDirection);
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        bestVertex = vertex;
                    }
                }

                localAnchorPoint = bestVertex;
                return true;
            }
        }
        catch (UnityException)
        {
            // Fall back to the mesh bounds if vertex data is not readable.
        }

        Vector3 extents = bounds.extents;
        localAnchorPoint = center + new Vector3(
            LocalStemDirection.x >= 0f ? extents.x : -extents.x,
            LocalStemDirection.y >= 0f ? extents.y : -extents.y,
            LocalStemDirection.z >= 0f ? extents.z : -extents.z
        );

        return true;
    }

    private static bool IsTargetTomatoObject(Transform tr)
    {
        if (!tr.name.StartsWith(TomatoObjectPrefix, StringComparison.Ordinal))
            return false;

        if (tr.name.EndsWith(FruitOnlyProxySuffix, StringComparison.Ordinal))
            return false;

        return true;
    }
}
