using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BranchMaterialReplacer
{
    private const string TargetObjectPrefix = "Branch_";
    private const string OldMaterialName = "z_No Name";
    private const string NewMaterialName = "z_No Name 1";

    [MenuItem("Tools/Debug/Replace Branch Material")]
    public static void ReplaceBranchMaterial()
    {
        Material newMat = FindMaterialAssetByName(NewMaterialName);

        if (newMat == null)
        {
            Debug.LogError($"[BranchMaterialReplacer] New material not found: {NewMaterialName}");
            return;
        }

        int checkedRenderers = 0;
        int replacedSlots = 0;

        Scene scene = SceneManager.GetActiveScene();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform tr in root.GetComponentsInChildren<Transform>(true))
            {
                if (!tr.name.StartsWith(TargetObjectPrefix, StringComparison.Ordinal))
                    continue;

                Renderer[] renderers = tr.GetComponentsInChildren<Renderer>(true);

                foreach (Renderer renderer in renderers)
                {
                    checkedRenderers++;

                    Material[] mats = renderer.sharedMaterials;
                    bool changed = false;

                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == null)
                            continue;

                        string matName = NormalizeMaterialName(mats[i].name);

                        if (matName == OldMaterialName)
                        {
                            mats[i] = newMat;
                            changed = true;
                            replacedSlots++;
                        }
                    }

                    if (changed)
                    {
                        Undo.RecordObject(renderer, "Replace Branch Material");
                        renderer.sharedMaterials = mats;
                        EditorUtility.SetDirty(renderer);
                    }
                }
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log(
            $"[BranchMaterialReplacer] CheckedRenderers={checkedRenderers}, " +
            $"ReplacedSlots={replacedSlots}, " +
            $"Old='{OldMaterialName}', New='{NewMaterialName}'"
        );
    }

    private static Material FindMaterialAssetByName(string materialName)
    {
        string[] guids = AssetDatabase.FindAssets($"{materialName} t:Material");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (mat != null && mat.name == materialName)
                return mat;
        }

        return null;
    }

    private static string NormalizeMaterialName(string name)
    {
        return name
            .Replace(" (Instance)", "")
            .Replace("(Instance)", "")
            .Trim();
    }
}