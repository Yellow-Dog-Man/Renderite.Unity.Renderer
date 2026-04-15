using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ViveHandTracking.Sample {

  class JointRotationRenderer : MonoBehaviour {
    public bool isLeft = false;
    public GameObject JointPrefab = null;

    private List<GameObject> joints = new List<GameObject>();

    IEnumerator Start() {
      while (GestureProvider.Status == GestureStatus.NotStarted) yield return null;

      int count = GestureProvider.HaveSkeleton ? 21 : 1;
      for (int i = 0; i < count; i++) {
        var go = GameObject.Instantiate(JointPrefab);
        go.name = "joint" + i;
        go.transform.parent = transform;
        go.SetActive(false);
        joints.Add(go);
      }
    }

    void Update() {
      var hand = isLeft ? GestureProvider.LeftHand : GestureProvider.RightHand;
      if (hand == null) {
        foreach (var j in joints) j.SetActive(false);
        return;
      }

      for (int i = 0; i < joints.Count; i++) {
        var go = joints[i];
        go.transform.position = hand.points[i];
        go.transform.rotation = hand.rotations[i];
        go.SetActive(true);
      }
    }
  }

}
