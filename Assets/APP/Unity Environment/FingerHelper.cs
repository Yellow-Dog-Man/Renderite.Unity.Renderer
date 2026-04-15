using Renderite.Shared;
using System.Collections.Generic;
using UnityEngine;
using System;

public static class FingerHelper
{
    public const int FINGER_SEGMENT_COUNT = (int)(BodyNode.LEFT_FINGER_END - BodyNode.LEFT_FINGER_START) + 1;

    public static int FlatSegmentIndex(this BodyNode node)
    {
        node = node.GetSide(Chirality.Left);

        var index = (int)(node - BodyNode.LEFT_FINGER_START);

        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(node), $"Invalid node: {node}");

        if(index >= FINGER_SEGMENT_COUNT)
            throw new ArgumentOutOfRangeException(nameof(node), $"Invalid node: {node}");

        return index;
    }
}