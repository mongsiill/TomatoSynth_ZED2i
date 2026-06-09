using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Perception.GroundTruth.LabelManagement;

public static class CreateTomatoRipenessIdLabelConfig
{
    private const string ConfigDir = "Assets/PerceptionConfigs";
    private const string ConfigPath = ConfigDir + "/TomatoRipenessIdLabelConfig.asset";

    [MenuItem("Tools/Perception/Create Tomato Ripeness ID Label Config")]
    public static void CreateConfig()
    {
        if (!Directory.Exists(ConfigDir))
            Directory.CreateDirectory(ConfigDir);

        IdLabelConfig oldConfig = AssetDatabase.LoadAssetAtPath<IdLabelConfig>(ConfigPath);
        if (oldConfig != null)
            AssetDatabase.DeleteAsset(ConfigPath);

        IdLabelConfig config = ScriptableObject.CreateInstance<IdLabelConfig>();
        config.autoAssignIds = false;

        config.Init(new[]
        {
            new IdLabelEntry { label = "ripe", id = 1 },
            new IdLabelEntry { label = "unripe", id = 2 }
        });

        AssetDatabase.CreateAsset(config, ConfigPath);
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[Perception] Created TomatoRipenessIdLabelConfig.asset with ripe=1, unripe=2");
    }
}