using System.Collections;
using UnityEngine;

namespace ViveHandTracking {

// extension functions for use in editor/wavevr engine
internal static class GestureResultExtension {
  public static GestureResultRaw CreateHand(bool left) {
    var hand = new GestureResultRaw();
    hand.isLeft = left;
    hand.points = new Vector3[21];
    hand.rotations = new Quaternion[21];
    return hand;
  }

  // generate rotation data from points
  public static void GenerateRotation(this GestureResultRaw hand, bool hasSkeleton = true) {
    if (!hasSkeleton) {
      hand.position = hand.points[0];
      var forward = hand.points[0] - GestureProvider.Current.transform.position;
      hand.rotations[0] = Quaternion.LookRotation(forward, Vector3.up);
      hand.pinch.pinchStart = hand.points[0];
      hand.pinch.pinchDirection = forward;
      return;
    }

    hand.position = (hand.points[0] + hand.points[9]) / 2f;
    Vector3 indexDir = (hand.points[5] - hand.points[0]).normalized;
    Vector3 midDir = (hand.points[9] - hand.points[0]).normalized;
    Vector3 palmDir =
        hand.isLeft ? Vector3.Cross(indexDir, midDir) : Vector3.Cross(midDir, indexDir);
    Vector3 thumbAxis = (hand.points[17] - hand.points[1]).normalized;
    hand.rotations[0] = Quaternion.LookRotation(palmDir, midDir);

    int nodeIndex = 1;
    for (int i = 0; i < 5; ++i, nodeIndex += 4) {
      Vector3 joint1 = hand.points[nodeIndex + 1];
      Vector3 vec1 = (joint1 - hand.points[nodeIndex]) * 10;
      Vector3 joint2 = hand.points[nodeIndex + 2];
      Vector3 vec2 = (joint2 - joint1) * 10;
      Vector3 vec3 = hand.points[nodeIndex + 3] - joint2;

      Vector3 fingerNormal;
      if (i == 0)
        fingerNormal =
            CalculateFingerNormal(vec1, vec2, thumbAxis, hand.isLeft ? palmDir : -palmDir);
      else
        fingerNormal = CalculateFingerNormal(vec1, vec2, palmDir, Vector3.Cross(indexDir, palmDir));

      hand.rotations[nodeIndex] = Quaternion.LookRotation(Vector3.Cross(fingerNormal, vec1), vec1);
      hand.rotations[nodeIndex + 1] =
          Quaternion.LookRotation(Vector3.Cross(fingerNormal, vec2), vec2);
      hand.rotations[nodeIndex + 2] =
          Quaternion.LookRotation(Vector3.Cross(fingerNormal, vec3), vec3);
      hand.rotations[nodeIndex + 3] = hand.rotations[nodeIndex + 2];
    }

    hand.pinch.pinchStart = hand.points[1] + hand.points[5] - hand.points[0];
    var position = (hand.points[0] + hand.points[9]) / 2f;
    var start = (GestureProvider.Current.transform.position + position) / 2f;
    hand.pinch.pinchDirection = hand.pinch.pinchStart - start;
  }

  private static Vector3 CalculateFingerNormal(Vector3 vec1, Vector3 vec2, Vector3 forward,
                                               Vector3 right) {
    Vector3 vec1p = vec1 - Vector3.Dot(vec1, right) * right / right.sqrMagnitude;
    Vector3 vec2p = vec2 - Vector3.Dot(vec2, right) * right / right.sqrMagnitude;
    float angle0 = Vector3.Angle(vec1p, vec2p);
    float angle1 = Vector3.Angle(vec1p, forward);
    float angle2 = Vector3.Angle(vec2p, forward);

    if (angle0 > angle1 && angle0 > angle2)
      return Vector3.Cross(vec1, vec2) * AngleSign(vec1p, vec2p, right);
    else if (angle1 > angle2)
      return Vector3.Cross(vec1, forward) * AngleSign(vec1p, forward, right);
    else
      return Vector3.Cross(vec2, forward) * AngleSign(vec2p, forward, right);
  }

  private static float AngleSign(Vector3 v1, Vector3 v2, Vector3 axis) {
    return Mathf.Sign(Vector3.Dot(axis, Vector3.Cross(v1, v2)));
  }
}

}
