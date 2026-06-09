using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RecoverHDRPTextures
{
    [MenuItem("Tools/Debug/Recover HDRP Material Textures")]
    public static void Recover()
    {
        int checkedMaterials = 0;
        int recoveredBaseMap = 0;
        int recoveredColor = 0;
        int recoveredNormal = 0;

        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material mat in r.sharedMaterials)
                {
                    if (mat == null) continue;
                    if (mat.shader == null) continue;
                    if (!mat.shader.name.Contains("HDRP/Lit")) continue;

                    checkedMaterials++;

                    Undo.RecordObject(mat, "Recover HDRP Material Textures");

                    // 기존 Standard 계열 texture가 남아 있는 경우
                    if (mat.HasProperty("_MainTex") &&
                        mat.HasProperty("_BaseColorMap") &&
                        mat.GetTexture("_BaseColorMap") == null &&
                        mat.GetTexture("_MainTex") != null)
                    {
                        mat.SetTexture("_BaseColorMap", mat.GetTexture("_MainTex"));
                        recoveredBaseMap++;
                    }

                    // 기존 color가 남아 있는 경우
                    if (mat.HasProperty("_Color") &&
                        mat.HasProperty("_BaseColor"))
                    {
                        Color oldColor = mat.GetColor("_Color");

                        // 완전 기본 흰색이 아닌 경우만 복구
                        if (oldColor != Color.white)
                        {
                            mat.SetColor("_BaseColor", oldColor);
                            recoveredColor++;
                        }
                    }

                    // 기존 normal map이 남아 있는 경우
                    if (mat.HasProperty("_BumpMap") &&
                        mat.HasProperty("_NormalMap") &&
                        mat.GetTexture("_NormalMap") == null &&
                        mat.GetTexture("_BumpMap") != null)
                    {
                        mat.SetTexture("_NormalMap", mat.GetTexture("_BumpMap"));
                        mat.EnableKeyword("_NORMALMAP");
                        recoveredNormal++;
                    }

                    EditorUtility.SetDirty(mat);
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"[RecoverHDRPTextures] Checked={checkedMaterials}, " +
            $"RecoveredBaseMap={recoveredBaseMap}, " +
            $"RecoveredColor={recoveredColor}, " +
            $"RecoveredNormal={recoveredNormal}"
        );
    }
}