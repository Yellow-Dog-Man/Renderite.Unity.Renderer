using UnityEngine;
using System.Collections.Generic;

public struct TransformData
{
    public Vector3 LocalPosition;
    public Quaternion LocalRotation;
    public Vector3 LocalScale;
}

public static class TransformExtensions
{
    public static int GetChildIndex(this Transform self)
    {
        if (self.parent != null)
            for (int i = 0; i < self.parent.childCount; i++)
                if (self.parent.GetChild(i) == self)
                    return i;

        return -1;
    }

    public static Quaternion InverseTransformRotation(this Transform self, Quaternion worldRotation)
    {
        return Quaternion.Inverse(self.rotation) * worldRotation;
    }

    public static Quaternion TransformRotation(this Transform self, Quaternion localRotation)
    {
        return self.rotation * localRotation;
    }

    public static void DestroyChildren(this Transform self)
    {
        for (int i = 0; i < self.childCount; i++)
        {
            var go = self.GetChild(i).gameObject;

            if (go)
                Object.Destroy(go);
        }
    }

    public static Transform FindContaining(this Transform self, string substring)
    {
        for (int i = 0; i < self.childCount; i++)
        {
            var child = self.GetChild(i);
            if (child.name.Contains(substring))
                return child;
        }

        return null;
    }
}