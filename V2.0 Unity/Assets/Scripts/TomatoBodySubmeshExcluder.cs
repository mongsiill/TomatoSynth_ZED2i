using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Perception.GroundTruth.LabelManagement;
using UnityEngine.Rendering;

public static class TomatoBodySubmeshExcluder
{
    private const string TomatoObjectPrefix = "tomato_";
    private const string BodyMaterialName = "Body";
    private const string FruitOnlyProxySuffix = "_FruitOnlyGt";

    public const int OriginalTomatoLayer = 29;
    public const int FruitOnlyGtLayer = 30;

    public static int ApplyToScene(Zed2iStereoCameraRig rig)
    {
        if (rig == null)
        {
            Debug.LogWarning("[TomatoBodySubmeshExcluder] Skipped setup because Zed2iStereoCameraRig is missing.");
            return 0;
        }

        ConfigureCameraMasks(rig);

        int changed = 0;
        foreach (Transform tr in UnityEngine.Object.FindObjectsOfType<Transform>(true))
        {
            if (!tr.name.StartsWith(TomatoObjectPrefix, StringComparison.Ordinal))
                continue;

            if (tr.name.EndsWith(FruitOnlyProxySuffix, StringComparison.Ordinal))
                continue;

            if (HasTomatoParent(tr))
                continue;

            if (TryCreateFruitOnlyProxy(tr))
                changed++;
        }

        if (changed > 0)
        {
            Debug.Log(
                $"[TomatoBodySubmeshExcluder] Created {changed} fruit-only GT tomato proxies. " +
                "RGB/custom depth use original tomatoes; GT annotations use proxies without Body submesh."
            );
        }

        return changed;
    }

    private static bool TryCreateFruitOnlyProxy(Transform tomato)
    {
        MeshFilter meshFilter = tomato.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = tomato.GetComponent<MeshRenderer>();
        Labeling sourceLabeling = tomato.GetComponent<Labeling>();

        if (meshFilter == null || meshRenderer == null || meshFilter.sharedMesh == null || sourceLabeling == null)
            return false;

        string proxyName = tomato.name + FruitOnlyProxySuffix;
        if (tomato.parent != null && tomato.parent.Find(proxyName) != null)
            return false;

        Material[] sourceMaterials = meshRenderer.sharedMaterials;
        Mesh sourceMesh = meshFilter.sharedMesh;
        int slotCount = Mathf.Min(sourceMesh.subMeshCount, sourceMaterials.Length);

        if (slotCount == 0)
            return false;

        List<int> keptSubMeshes = new List<int>();
        List<Material> keptMaterials = new List<Material>();
        bool foundBody = false;

        for (int i = 0; i < slotCount; i++)
        {
            if (IsBodyMaterial(sourceMaterials[i]))
            {
                foundBody = true;
                continue;
            }

            keptSubMeshes.Add(i);
            keptMaterials.Add(sourceMaterials[i]);
        }

        if (!foundBody || keptSubMeshes.Count == 0)
            return false;

        Mesh fruitOnlyMesh;
        try
        {
            fruitOnlyMesh = BuildMeshWithoutSubMeshes(sourceMesh, keptSubMeshes);
        }
        catch (Exception e)
        {
            Debug.LogWarning(
                $"[TomatoBodySubmeshExcluder] Could not create fruit-only proxy for {tomato.name}. " +
                $"Enable Read/Write on mesh asset '{sourceMesh.name}'. {e.Message}"
            );
            return false;
        }

        if (fruitOnlyMesh == null)
            return false;

        fruitOnlyMesh.name = sourceMesh.name + "_FruitOnly";

        GameObject proxy = new GameObject(proxyName);
        Transform proxyTransform = proxy.transform;
        proxyTransform.SetParent(tomato.parent, false);
        proxyTransform.SetSiblingIndex(tomato.GetSiblingIndex() + 1);
        proxyTransform.localPosition = tomato.localPosition;
        proxyTransform.localRotation = tomato.localRotation;
        proxyTransform.localScale = tomato.localScale;
        proxy.layer = FruitOnlyGtLayer;

        MeshFilter proxyMeshFilter = proxy.AddComponent<MeshFilter>();
        proxyMeshFilter.sharedMesh = fruitOnlyMesh;

        MeshRenderer proxyMeshRenderer = proxy.AddComponent<MeshRenderer>();
        proxyMeshRenderer.sharedMaterials = keptMaterials.ToArray();
        CopyRendererSettings(meshRenderer, proxyMeshRenderer);

        Labeling proxyLabeling = proxy.AddComponent<Labeling>();
        proxyLabeling.useAutoLabeling = sourceLabeling.useAutoLabeling;
        proxyLabeling.labels.Clear();
        foreach (string label in sourceLabeling.labels)
            proxyLabeling.labels.Add(label);

        TomatoFruitOnlyProxySync sync = proxy.AddComponent<TomatoFruitOnlyProxySync>();
        sync.source = tomato;

        sourceLabeling.enabled = false;
        SetLayerRecursive(tomato.gameObject, OriginalTomatoLayer);

        return true;
    }

    private static void ConfigureCameraMasks(Zed2iStereoCameraRig rig)
    {
        ConfigureRgbCamera(rig.leftRgbCamera);
        ConfigureRgbCamera(rig.rightRgbCamera);
        ConfigureGtCamera(rig.leftGtCamera);
    }

    private static void ConfigureRgbCamera(Camera camera)
    {
        if (camera == null)
            return;

        camera.cullingMask |= 1 << OriginalTomatoLayer;
        camera.cullingMask &= ~(1 << FruitOnlyGtLayer);
    }

    private static void ConfigureGtCamera(Camera camera)
    {
        if (camera == null)
            return;

        camera.cullingMask |= 1 << FruitOnlyGtLayer;
        camera.cullingMask &= ~(1 << OriginalTomatoLayer);
    }

    private static Mesh BuildMeshWithoutSubMeshes(Mesh sourceMesh, List<int> keptSubMeshes)
    {
        Dictionary<int, int> oldToNew = new Dictionary<int, int>();
        List<int[]> remappedSubMeshIndices = new List<int[]>();
        List<MeshTopology> topologies = new List<MeshTopology>();

        foreach (int subMeshIndex in keptSubMeshes)
        {
            int[] sourceIndices = sourceMesh.GetIndices(subMeshIndex);
            int[] remappedIndices = new int[sourceIndices.Length];

            for (int i = 0; i < sourceIndices.Length; i++)
            {
                int oldIndex = sourceIndices[i];
                if (!oldToNew.TryGetValue(oldIndex, out int newIndex))
                {
                    newIndex = oldToNew.Count;
                    oldToNew.Add(oldIndex, newIndex);
                }

                remappedIndices[i] = newIndex;
            }

            remappedSubMeshIndices.Add(remappedIndices);
            topologies.Add(sourceMesh.GetTopology(subMeshIndex));
        }

        if (oldToNew.Count == 0)
            return null;

        int[] newToOld = new int[oldToNew.Count];
        foreach (KeyValuePair<int, int> pair in oldToNew)
            newToOld[pair.Value] = pair.Key;

        Mesh result = new Mesh();
        result.indexFormat = oldToNew.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

        CopyVector3(sourceMesh.vertices, newToOld, result.SetVertices);
        CopyVector3(sourceMesh.normals, newToOld, result.SetNormals);
        CopyVector4(sourceMesh.tangents, newToOld, result.SetTangents);
        CopyColor(sourceMesh.colors, newToOld, result.SetColors);
        CopyColor32(sourceMesh.colors32, newToOld, result.SetColors);
        CopyVector2(sourceMesh.uv, newToOld, result.SetUVs, 0);
        CopyVector2(sourceMesh.uv2, newToOld, result.SetUVs, 1);
        CopyVector2(sourceMesh.uv3, newToOld, result.SetUVs, 2);
        CopyVector2(sourceMesh.uv4, newToOld, result.SetUVs, 3);
        CopyVector2(sourceMesh.uv5, newToOld, result.SetUVs, 4);
        CopyVector2(sourceMesh.uv6, newToOld, result.SetUVs, 5);
        CopyVector2(sourceMesh.uv7, newToOld, result.SetUVs, 6);
        CopyVector2(sourceMesh.uv8, newToOld, result.SetUVs, 7);

        result.subMeshCount = remappedSubMeshIndices.Count;
        for (int i = 0; i < remappedSubMeshIndices.Count; i++)
            result.SetIndices(remappedSubMeshIndices[i], topologies[i], i);

        result.RecalculateBounds();
        return result;
    }

    private static bool IsBodyMaterial(Material material)
    {
        if (material == null)
            return false;

        string materialName = material.name
            .Replace(" (Instance)", "")
            .Replace("(Instance)", "")
            .Trim();

        return string.Equals(materialName, BodyMaterialName, StringComparison.Ordinal);
    }

    private static bool HasTomatoParent(Transform tr)
    {
        Transform parent = tr.parent;
        while (parent != null)
        {
            if (parent.name.StartsWith(TomatoObjectPrefix, StringComparison.Ordinal))
                return true;

            parent = parent.parent;
        }

        return false;
    }

    private static void SetLayerRecursive(GameObject gameObject, int layer)
    {
        gameObject.layer = layer;

        foreach (Transform child in gameObject.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    private static void CopyRendererSettings(MeshRenderer source, MeshRenderer target)
    {
        target.enabled = source.enabled;
        target.shadowCastingMode = source.shadowCastingMode;
        target.receiveShadows = source.receiveShadows;
        target.lightProbeUsage = source.lightProbeUsage;
        target.reflectionProbeUsage = source.reflectionProbeUsage;
        target.motionVectorGenerationMode = source.motionVectorGenerationMode;
        target.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
        target.rendererPriority = source.rendererPriority;
        target.renderingLayerMask = source.renderingLayerMask;
    }

    private static void CopyVector3(Vector3[] source, int[] newToOld, Action<List<Vector3>> set)
    {
        if (source == null || source.Length == 0)
            return;

        List<Vector3> values = new List<Vector3>(newToOld.Length);
        for (int i = 0; i < newToOld.Length; i++)
            values.Add(source[newToOld[i]]);

        set(values);
    }

    private static void CopyVector4(Vector4[] source, int[] newToOld, Action<List<Vector4>> set)
    {
        if (source == null || source.Length == 0)
            return;

        List<Vector4> values = new List<Vector4>(newToOld.Length);
        for (int i = 0; i < newToOld.Length; i++)
            values.Add(source[newToOld[i]]);

        set(values);
    }

    private static void CopyVector2(Vector2[] source, int[] newToOld, Action<int, List<Vector2>> set, int channel)
    {
        if (source == null || source.Length == 0)
            return;

        List<Vector2> values = new List<Vector2>(newToOld.Length);
        for (int i = 0; i < newToOld.Length; i++)
            values.Add(source[newToOld[i]]);

        set(channel, values);
    }

    private static void CopyColor(Color[] source, int[] newToOld, Action<List<Color>> set)
    {
        if (source == null || source.Length == 0)
            return;

        List<Color> values = new List<Color>(newToOld.Length);
        for (int i = 0; i < newToOld.Length; i++)
            values.Add(source[newToOld[i]]);

        set(values);
    }

    private static void CopyColor32(Color32[] source, int[] newToOld, Action<List<Color32>> set)
    {
        if (source == null || source.Length == 0)
            return;

        List<Color32> values = new List<Color32>(newToOld.Length);
        for (int i = 0; i < newToOld.Length; i++)
            values.Add(source[newToOld[i]]);

        set(values);
    }
}
