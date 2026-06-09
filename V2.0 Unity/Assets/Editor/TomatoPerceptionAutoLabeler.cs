using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Perception.GroundTruth.LabelManagement;

public static class TomatoPerceptionAutoLabeler
{
    private const string TomatoObjectPrefix = "tomato_";

    private const string RipeLabel = "ripe";
    private const string UnripeLabel = "unripe";

    [MenuItem("Tools/Perception/Label tomato_ Objects By Ripeness")]
    public static void LabelTomatoObjectsByRipeness()
    {
        int tomatoCount = 0;
        int ripeCount = 0;
        int unripeCount = 0;

        Scene scene = SceneManager.GetActiveScene();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform tr in root.GetComponentsInChildren<Transform>(true))
            {
                if (!IsTargetTomatoObject(tr))
                    continue;

                tomatoCount++;

                Renderer renderer = tr.GetComponentInChildren<Renderer>(true);
                Material material = renderer.sharedMaterial;

                string materialName = NormalizeMaterialName(material.name);
                string label = GetRipenessLabel(materialName);

                Labeling labeling = tr.GetComponent<Labeling>();
                if (labeling == null)
                    labeling = Undo.AddComponent<Labeling>(tr.gameObject);

                Undo.RecordObject(labeling, "Set tomato ripeness label");

                labeling.useAutoLabeling = false;
                labeling.labels.Clear();
                labeling.labels.Add(label);

                EditorUtility.SetDirty(labeling);

                if (label == RipeLabel)
                    ripeCount++;
                else
                    unripeCount++;
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log(
            $"[Tomato Ripeness Labeling] Total={tomatoCount}, " +
            $"Ripe={ripeCount}, Unripe={unripeCount}"
        );
    }

    private static bool IsTargetTomatoObject(Transform tr)
    {
        if (!tr.name.StartsWith(TomatoObjectPrefix, StringComparison.Ordinal))
            return false;

        return true;
    }

    private static string GetRipenessLabel(string materialName)
    {
        if (materialName == "Tom1" ||
            materialName == "Tom1 1" ||
            materialName == "Tom1 2" ||
            materialName == "Tom_6" ||
            materialName == "Tom_7")
        {
            return RipeLabel;
        }

        return UnripeLabel;
    }

    private static string NormalizeMaterialName(string name)
    {
        return name
            .Replace(" (Instance)", "")
            .Replace("(Instance)", "")
            .Trim();
    }
}