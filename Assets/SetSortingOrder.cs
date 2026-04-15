using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class SetSortingOrder : MonoBehaviour
{
    public int Order;

    [ExecuteInEditMode]
    void Update()
    {
        var renderer = GetComponent<MeshRenderer>();

        if (renderer != null)
            renderer.sortingOrder = Order;
    }
}
