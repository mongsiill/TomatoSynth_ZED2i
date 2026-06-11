using UnityEngine;

public class TomatoFruitOnlyProxySync : MonoBehaviour
{
    public Transform source;

    private void LateUpdate()
    {
        if (source == null)
            return;

        transform.localPosition = source.localPosition;
        transform.localRotation = source.localRotation;
        transform.localScale = source.localScale;
    }
}
