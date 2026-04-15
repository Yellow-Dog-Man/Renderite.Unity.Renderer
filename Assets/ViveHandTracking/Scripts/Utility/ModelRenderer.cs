using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ViveHandTracking {

[HelpURL("https://hub.vive.com/storage/tracking/unity/model.html")]
public class ModelRenderer : MonoBehaviour {
  private const float minAlpha = 0.2f;

  [Tooltip(
      "Initial rotation for model to point thumb finger upward and index finger forward when fingers open")]
  public Vector3 initialRotation = Vector3.zero;
  [Tooltip("Draw left hand if true, right hand otherwise")]
  public bool IsLeft = false;
  [Tooltip("Root object of skinned mesh")]
  public GameObject Hand = null;
  [Tooltip("Nodes of skinned mesh, must be size of 21 in same order as skeleton definition")]
  public Transform[] Nodes = new Transform[21];
  [Tooltip("Collider type created with hand. The layer of the object is same as this object.")]
  public HandColliderType colliderType = HandColliderType.None;
  [Tooltip("Use hand confidence as alpha, low confidence hand becomes transparent")]
  public bool showConficenceAsAlpha = false;

  private List<Transform> colliders = null;
  private Quaternion[] jointRotation;

  void Awake() {
    InitializeModel();
    Hand.SetActive(false);

    // create colliders
    if (colliderType != HandColliderType.None) {
      colliders = new List<Transform>();
      var go = new GameObject("Collider");
      go.transform.parent = Hand.transform;
      go.layer = gameObject.layer;
      var collider = go.AddComponent<BoxCollider>();
      collider.isTrigger = colliderType == HandColliderType.Trigger;
      colliders.Add(go.transform);
    }
    if (colliderType == HandColliderType.Collider) {
      // add bones colliders
      for (int i = 0; i < Bones.Length; i += 2) {
        var go = new GameObject("Bone" + i);
        go.transform.parent = Hand.transform;
        go.layer = gameObject.layer;
        go.AddComponent<CapsuleCollider>();
        colliders.Add(go.transform);
      }
    }
  }

  IEnumerator Start() {
    while (GestureProvider.Status == GestureStatus.NotStarted) yield return null;
    if (!GestureProvider.HaveSkeleton) this.enabled = false;
  }

  void Update() {
    GestureResult result = IsLeft ? GestureProvider.LeftHand : GestureProvider.RightHand;
    if (result == null) {
      Hand.SetActive(false);
      return;
    }
    Hand.SetActive(true);

    transform.localPosition = result.points[0];
    transform.localRotation = result.rotation * Quaternion.Euler(initialRotation);

    var parentRotation = transform.parent != null ? transform.parent.rotation : Quaternion.identity;
    int nodeIndex = 1;
    int vecIndex = 0;
    for (int i = 0; i < 5; ++i, nodeIndex += 4, vecIndex += 3) {
      Nodes[nodeIndex].rotation =
          parentRotation * result.rotations[nodeIndex] * jointRotation[vecIndex];
      Nodes[nodeIndex + 1].rotation =
          parentRotation * result.rotations[nodeIndex + 1] * jointRotation[vecIndex + 1];
      Nodes[nodeIndex + 2].rotation =
          parentRotation * result.rotations[nodeIndex + 2] * jointRotation[vecIndex + 2];
    }

    if (showConficenceAsAlpha) {
      var color = Hand.GetComponent<Renderer>().material.color;
      color.a = result.confidence > minAlpha ? result.confidence : minAlpha;
      Hand.GetComponent<Renderer>().material.color = color;
    }
    if (colliderType == HandColliderType.Trigger)
      UpdateTigger();
    else if (colliderType == HandColliderType.Collider)
      UpdateCollider();
  }

#region Model axis detection

  private void InitializeModel() {
    // find local normal vector in node local axis, assuming all finger nodes have same local axis
    var right = FindLocalNormal(Nodes[9]);
    // get initial finger direction and length in local axis
    jointRotation = new Quaternion[15];
    int vecIndex = 0;
    int nodeIndex = 1;
    for (int i = 0; i < 5; i++, nodeIndex += 4, vecIndex += 3) {
      var up = Nodes[nodeIndex + 1].localPosition;
      jointRotation[vecIndex] =
          Quaternion.Inverse(Quaternion.LookRotation(Vector3.Cross(right, up), up));
      up = Nodes[nodeIndex + 2].localPosition;
      jointRotation[vecIndex + 1] =
          Quaternion.Inverse(Quaternion.LookRotation(Vector3.Cross(right, up), up));
      up = Nodes[nodeIndex + 3].localPosition;
      jointRotation[vecIndex + 2] =
          Quaternion.Inverse(Quaternion.LookRotation(Vector3.Cross(right, up), up));
    }
  }

  private Vector3 FindLocalNormal(Transform node) {
    var rotation = node.rotation;
    if (transform.parent != null)
      rotation = Quaternion.Inverse(transform.parent.rotation) * rotation;
    var axis = Vector3.zero;
    var minDistance = 0f;
    var dot = Vector3.Dot(rotation * Vector3.forward, Vector3.right);
    if (dot > minDistance) {
      minDistance = dot;
      axis = Vector3.forward;
    } else if (-dot > minDistance) {
      minDistance = -dot;
      axis = Vector3.back;
    }

    dot = Vector3.Dot(rotation * Vector3.right, Vector3.right);
    if (dot > minDistance) {
      minDistance = dot;
      axis = Vector3.right;
    } else if (-dot > minDistance) {
      minDistance = -dot;
      axis = Vector3.left;
    }

    dot = Vector3.Dot(rotation * Vector3.up, Vector3.right);
    if (dot > minDistance) {
      minDistance = dot;
      axis = Vector3.up;
    } else if (-dot > minDistance) {
      minDistance = -dot;
      axis = Vector3.down;
    }
    return axis;
  }

#endregion

#region Colliders

  // Links between keypoints, 2*i & 2*i+1 forms a link.
  private static int[] Bones = new int[] {
    2,  3,  3,  4,           // thumb
    5,  6,  6,  7,  7,  8,   // index
    9,  10, 10, 11, 11, 12,  // middle
    13, 14, 14, 15, 15, 16,  // ring
    17, 18, 18, 19, 19, 20,  // pinky
  };
  private static readonly Vector3 OneCM = Vector3.one * 0.01f;

  void UpdateTigger() {
    var bounds = new Bounds(Nodes[0].position, Vector3.zero);
    for (int i = 1; i < Nodes.Length; i++) bounds.Encapsulate(Nodes[i].position);
    SetBounds(colliders[0], bounds);
  }

  void UpdateCollider() {
    // palm bounds
    var bounds = new Bounds(Nodes[0].position, Vector3.zero);
    bounds.Encapsulate(Nodes[1].position);
    bounds.Encapsulate(Nodes[2].position);
    bounds.Encapsulate(Nodes[5].position);
    bounds.Encapsulate(Nodes[9].position);
    bounds.Encapsulate(Nodes[13].position);
    bounds.Encapsulate(Nodes[17].position);
    SetBounds(colliders[0], bounds);

    int index = 0;
    for (int i = 1; i < colliders.Count; i++) {
      var start = Nodes[Bones[index++]].position;
      var end = Nodes[Bones[index++]].position;
      colliders[i].position = (start + end) / 2;
      var direction = end - start;
      colliders[i].rotation = Quaternion.FromToRotation(Vector3.forward, direction);
      colliders[i].localScale = new Vector3(0.01f, 0.01f, direction.magnitude);
    }
  }

  void SetBounds(Transform t, Bounds b) {
    t.position = b.center;
    t.rotation = Quaternion.identity;
    t.localScale = Vector3.Max(b.size, OneCM);
  }

#endregion
}
}
