using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ARPlaceCube : MonoBehaviour
{
    [Header("AR Refs")]
    [SerializeField] private ARRaycastManager raycastManager;   // Handles raycasts into the AR world -_-
    [SerializeField] private ARAnchorManager anchorManager;     // Handles anchors for stability -_-
    [SerializeField] private Camera arCamera;                   // Reference to AR Camera (auto-fills if null) -_-

    [Header("Content")]
    [SerializeField] private GameObject contentPrefab;          // The 3D model we actually want to place -_-
    [SerializeField] private bool placeOnce = false;            // If true: only place the object once -_-
    [SerializeField] private bool useAnchors = true;            // If true: object attaches to AR Anchors for stability -_-
    [SerializeField] private bool allowFeaturePointFallback = true; // Use feature points if no plane hit -_-

    [Header("Orientation")]
    [Tooltip("Rotate object so forward faces camera, but constrained to the plane.")]
    [SerializeField] private bool faceCameraOnPlane = true;     // Whether object should face camera direction -_-
    [Tooltip("If plane is nearly horizontal, force object upright.")]
    [SerializeField] private bool keepUprightOnHorizPlanes = true; // Prevent weird tilts on flat floors -_-
    [Range(0f, 15f)][SerializeField] private float horizUprightThresholdDeg = 10f; // How much tilt counts as “flat” -_-

    [Header("Smoothing")]
    [Range(0f, 20f)][SerializeField] private float positionLerpSpeed = 12f; // Controls smoothness of position updates -_-
    [Range(0f, 20f)][SerializeField] private float rotationLerpSpeed = 12f; // Controls smoothness of rotation updates -_-

    private static readonly List<ARRaycastHit> s_Hits = new();  // Cache for raycast hits -_-
    private bool isPlacing = false;                             // Prevents double placement on same tap -_-
    private Transform placedRoot;                               // The parent transform of the spawned object -_-
    private ARAnchor currentAnchor;                             // Reference to the current anchor -_-
    private GameObject spawned;                                 // The spawned content prefab -_-

    void Awake()
    {
        // Auto-assign AR camera if not set manually -_-
        if (!arCamera) arCamera = Camera.main;
    }

    void Update()
    {
        if (!raycastManager) return;                           // Safety: no raycast manager = no AR -_-
        if (placeOnce && placedRoot != null) return;           // Skip if we only want to place once -_-

        // Input handling: tap or mouse click -_-
        bool pressed = (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began) ||
                       Input.GetMouseButtonDown(0);
        if (!pressed || isPlacing) return;                     // Ignore if no press or already placing -_-

        isPlacing = true;                                      // Block further placements until reset -_-

        // Get screen position of input -_-
        Vector2 screenPos = (Input.touchCount > 0) ? (Vector2)Input.GetTouch(0).position
                                                  : (Vector2)Input.mousePosition;

        // 1) Try raycast against planes -_-
        if (!raycastManager.Raycast(screenPos, s_Hits, TrackableType.Planes))
        {
            // 2) Fallback to feature points if allowed -_-
            if (!(allowFeaturePointFallback && raycastManager.Raycast(screenPos, s_Hits, TrackableType.FeaturePoint)))
            {
                StartCoroutine(ResetPlacingSoon()); // If no hit, just reset placement flag -_-
                return;
            }
        }

        // Get first hit result -_-
        var hit = s_Hits[0];
        Pose pose = hit.pose;                                 // Position + rotation of hit -_-

        // Compute rotation based on camera/plane -_-
        Quaternion rot = ComputeRotation(hit, pose);

        // Special case: if plane is flat, force upright -_-
        if (keepUprightOnHorizPlanes && (hit.hitType & TrackableType.Planes) != 0)
        {
            float tiltDeg = Vector3.Angle(pose.up, Vector3.up);
            if (tiltDeg <= horizUprightThresholdDeg)
            {
                Vector3 fwd = rot * Vector3.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude > 1e-4f)
                    rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            }
        }

        // If object doesn’t exist yet, spawn it -_-
        if (spawned == null)
        {
            if (!contentPrefab) { StartCoroutine(ResetPlacingSoon()); return; } // No prefab = nothing to do -_-

            // Place with anchor if possible -_-
            if (useAnchors && anchorManager != null && hit.trackable is ARPlane plane)
            {
                currentAnchor = anchorManager.AttachAnchor(plane, new Pose(pose.position, rot));
                if (currentAnchor != null)
                {
                    spawned = Instantiate(contentPrefab, currentAnchor.transform);
                    placedRoot = currentAnchor.transform; // Anchor is root now -_-
                }
            }

            // If anchor fails, fallback to just spawning -_-
            if (spawned == null)
            {
                spawned = Instantiate(contentPrefab, pose.position, rot);
                placedRoot = spawned.transform;
            }
        }
        else
        {
            // Object already exists → move smoothly -_-
            Vector3 newPos = Vector3.Lerp(placedRoot.position, pose.position, 1f - Mathf.Exp(-positionLerpSpeed * Time.deltaTime));
            Quaternion newRot = Quaternion.Slerp(placedRoot.rotation, rot, 1f - Mathf.Exp(-rotationLerpSpeed * Time.deltaTime));

            // If using anchors and new plane is detected → re-anchor -_-
            if (useAnchors && anchorManager != null && hit.trackable is ARPlane newPlane &&
                (Vector3.Distance(placedRoot.position, pose.position) > 0.05f || Quaternion.Angle(placedRoot.rotation, rot) > 5f))
            {
                Reanchor(newPlane, new Pose(newPos, newRot)); // Create fresh anchor -_-
            }
            else
            {
                placedRoot.SetPositionAndRotation(newPos, newRot); // Just update directly -_-
            }
        }

        StartCoroutine(ResetPlacingSoon()); // Reset guard so next tap works -_-
    }

    Quaternion ComputeRotation(ARRaycastHit hit, Pose pose)
    {
        Vector3 planeUp = ((hit.hitType & TrackableType.Planes) != 0) ? pose.up : Vector3.up; // Up vector depends on hit type -_-

        if (faceCameraOnPlane)
        {
            // Align forward with camera projected on plane -_-
            Vector3 camFwd = arCamera ? arCamera.transform.forward : Vector3.forward;
            Vector3 projected = Vector3.ProjectOnPlane(camFwd, planeUp);
            if (projected.sqrMagnitude < 1e-4f) projected = Vector3.ProjectOnPlane(Vector3.forward, planeUp);
            return Quaternion.LookRotation(projected.normalized, planeUp);
        }

        // Fallback: just align object’s up to plane’s up -_-
        return Quaternion.FromToRotation(Vector3.up, planeUp);
    }

    void Reanchor(ARPlane plane, Pose pose)
    {
        if (!useAnchors || anchorManager == null || spawned == null) return; // No re-anchor if anchors disabled -_-

        Transform content = spawned.transform;
        content.SetParent(null, true);   // Detach from old anchor -_-
        if (currentAnchor) Destroy(currentAnchor); // Destroy old anchor -_-

        // Create a new anchor -_-
        currentAnchor = anchorManager.AttachAnchor(plane, pose);
        if (currentAnchor != null)
        {
            currentAnchor.transform.SetPositionAndRotation(pose.position, pose.rotation);
            content.SetParent(currentAnchor.transform, true); // Re-parent content under new anchor -_-
            placedRoot = currentAnchor.transform;
        }
        else
        {
            // Fallback: no anchor, just place normally -_-
            content.position = pose.position;
            content.rotation = pose.rotation;
            placedRoot = content;
        }
    }

    IEnumerator ResetPlacingSoon()
    {
        yield return new WaitForSeconds(0.15f); // Quick cooldown to avoid double taps -_-
        isPlacing = false; // Ready for next placement -_-
    }
}
