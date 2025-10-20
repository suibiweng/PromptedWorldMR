using System.Collections.Generic;
using UnityEngine;

public class FingerTracker : MonoBehaviour
{
    [Header("Hands")]
    public OVRHand ovrleftHand;
    public OVRHand ovrrightHand;

    [Header("Indicators")]
    public bool enableIndicators = true;            // build / manage indicators
    public bool autoHideIndicators = true;          // hide when a hand isn’t tracked
    public float indicatorScale = 0.02f;
    public Color indicatorColor = Color.red;
    [Tooltip("Layer to assign to indicator spheres (optional).")]
    public string indicatorLayerName = "Ignore Raycast";

    // caches (bone -> transform)
    private readonly Dictionary<OVRSkeleton.BoneId, Transform> _leftTips  = new();
    private readonly Dictionary<OVRSkeleton.BoneId, Transform> _rightTips = new();

    // indicator refs
    private readonly List<GameObject> _leftTipIndicators  = new();
    private readonly List<GameObject> _rightTipIndicators = new();
    private bool _indicatorsBuilt;

    public enum Hand { Left, Right }
    public enum Finger { Thumb=0, Index=1, Middle=2, Ring=3, Pinky=4 }

    // ---------------- Public API: fingertip access ----------------
    // 0=Thumb, 1=Index, 2=Middle, 3=Ring, 4=Pinky
    public Transform LeftHand(int tip)  => GetByIndex(Hand.Left,  tip);
    public Transform RightHand(int tip) => GetByIndex(Hand.Right, tip);

    public bool TryLeftHand(int tip, out Transform t)  => TryGetByIndex(Hand.Left,  tip, out t);
    public bool TryRightHand(int tip, out Transform t) => TryGetByIndex(Hand.Right, tip, out t);

    // Optional enum versions
    public Transform GetFingertipTransformOrNull(Hand hand, Finger finger)
        => TryGetFingertipTransform(hand, finger, out var tf) ? tf : null;

    public bool TryGetFingertipTransform(Hand hand, Finger finger, out Transform tipTransform)
    {
        tipTransform = null;

        var ovrHand = (hand == Hand.Left) ? ovrleftHand : ovrrightHand;
        if (!IsHandReady(ovrHand)) return false;

        var map = (hand == Hand.Left) ? _leftTips : _rightTips;
        var boneId = FingerToBoneId(finger);

        if (map.TryGetValue(boneId, out var cached) && cached != null)
        {
            tipTransform = cached;
            return true;
        }

        var skel  = ovrHand.GetComponent<OVRSkeleton>();
        var bones = skel != null ? skel.Bones : null;   // IList<OVRBone>
        if (bones == null) return false;

        FillTipCache(bones, map);
        if (map.TryGetValue(boneId, out var found) && found != null)
        {
            tipTransform = found;
            return true;
        }
        return false;
    }

    // ---------------- Indicators control ----------------
    /// Build indicators immediately for any ready hands.
    public void BuildIndicatorsNow()
    {
        if (!enableIndicators) return;
        BuildIndicatorsForHand(Hand.Left,  _leftTips,  _leftTipIndicators,  "Left");
        BuildIndicatorsForHand(Hand.Right, _rightTips, _rightTipIndicators, "Right");
        _indicatorsBuilt = _leftTipIndicators.Count > 0 || _rightTipIndicators.Count > 0;
    }

    /// Show or hide all indicators (ignores autoHide; this is a hard toggle).
    public void ShowIndicators(bool show)
    {
        SetActiveForList(_leftTipIndicators, show);
        SetActiveForList(_rightTipIndicators, show);
    }

    /// Update indicator appearance at runtime.
    public void SetIndicatorColor(Color c)
    {
        indicatorColor = c;
        Recolor(_leftTipIndicators, c);
        Recolor(_rightTipIndicators, c);
    }

    public void SetIndicatorScale(float s)
    {
        indicatorScale = s;
        Rescale(_leftTipIndicators, s);
        Rescale(_rightTipIndicators, s);
    }

    // ---------------- Unity loop ----------------
    private void LateUpdate()
    {
        // lazily build indicators once hands become ready
        if (enableIndicators && !_indicatorsBuilt && (IsHandReady(ovrleftHand) || IsHandReady(ovrrightHand)))
            BuildIndicatorsNow();

        if (!enableIndicators || !_indicatorsBuilt) return;

        // auto hide/show by tracking
        if (autoHideIndicators)
        {
            ToggleListByHandReady(_leftTipIndicators,  IsHandReady(ovrleftHand));
            ToggleListByHandReady(_rightTipIndicators, IsHandReady(ovrrightHand));
        }

        // if a skeleton reinitializes and some tips are missing, rebuild that side
        if (IsHandReady(ovrleftHand)  && _leftTipIndicators.Count  < 5) BuildIndicatorsForHand(Hand.Left,  _leftTips,  _leftTipIndicators,  "Left");
        if (IsHandReady(ovrrightHand) && _rightTipIndicators.Count < 5) BuildIndicatorsForHand(Hand.Right, _rightTips, _rightTipIndicators, "Right");
    }

    // ---------------- Internals ----------------
    private Transform GetByIndex(Hand hand, int tipIndex)
        => TryGetByIndex(hand, tipIndex, out var t) ? t : null;

    private bool TryGetByIndex(Hand hand, int tipIndex, out Transform t)
    {
        t = null;
        if (tipIndex < 0 || tipIndex > 4) return false;
        var finger = (Finger)tipIndex;
        return TryGetFingertipTransform(hand, finger, out t);
    }

    private static bool IsHandReady(OVRHand hand)
    {
        if (!hand) return false;
        var sk = hand.GetComponent<OVRSkeleton>();
        return hand.IsTracked && sk && sk.IsDataValid && sk.Bones != null && sk.Bones.Count > 0;
    }

    private static OVRSkeleton.BoneId FingerToBoneId(Finger f)
    {
        switch (f)
        {
            case Finger.Thumb:  return OVRSkeleton.BoneId.Hand_ThumbTip;
            case Finger.Index:  return OVRSkeleton.BoneId.Hand_IndexTip;
            case Finger.Middle: return OVRSkeleton.BoneId.Hand_MiddleTip;
            case Finger.Ring:   return OVRSkeleton.BoneId.Hand_RingTip;
            default:            return OVRSkeleton.BoneId.Hand_PinkyTip;
        }
    }

    private static void FillTipCache(IList<OVRBone> bones, Dictionary<OVRSkeleton.BoneId, Transform> cache)
    {
        if (bones == null) return;

        // If already have all 5, skip
        if (cache.Count >= 5 &&
            cache.ContainsKey(OVRSkeleton.BoneId.Hand_ThumbTip)  && cache[OVRSkeleton.BoneId.Hand_ThumbTip]  &&
            cache.ContainsKey(OVRSkeleton.BoneId.Hand_IndexTip)  && cache[OVRSkeleton.BoneId.Hand_IndexTip]  &&
            cache.ContainsKey(OVRSkeleton.BoneId.Hand_MiddleTip) && cache[OVRSkeleton.BoneId.Hand_MiddleTip] &&
            cache.ContainsKey(OVRSkeleton.BoneId.Hand_RingTip)   && cache[OVRSkeleton.BoneId.Hand_RingTip]   &&
            cache.ContainsKey(OVRSkeleton.BoneId.Hand_PinkyTip)  && cache[OVRSkeleton.BoneId.Hand_PinkyTip])
            return;

        for (int i = 0; i < bones.Count; i++)
        {
            var b = bones[i];
            if (b == null) continue;
            switch (b.Id)
            {
                case OVRSkeleton.BoneId.Hand_ThumbTip:
                case OVRSkeleton.BoneId.Hand_IndexTip:
                case OVRSkeleton.BoneId.Hand_MiddleTip:
                case OVRSkeleton.BoneId.Hand_RingTip:
                case OVRSkeleton.BoneId.Hand_PinkyTip:
                    cache[b.Id] = b.Transform;
                    break;
            }
        }
    }

    private void BuildIndicatorsForHand(Hand hand,
                                        Dictionary<OVRSkeleton.BoneId, Transform> cache,
                                        List<GameObject> indicatorList,
                                        string label)
    {
        var ovrHand = (hand == Hand.Left) ? ovrleftHand : ovrrightHand;
        if (!IsHandReady(ovrHand)) return;

        var skel = ovrHand.GetComponent<OVRSkeleton>();
        if (skel?.Bones == null) return;

        FillTipCache(skel.Bones, cache);

        // Make spheres for each cached tip
        foreach (var kv in cache)
        {
            var tipTf = kv.Value;
            if (!tipTf) continue;

            // Avoid duplicates under the bone
            bool exists = false;
            for (int i = 0; i < tipTf.childCount; i++)
            {
                if (tipTf.GetChild(i).name == $"{label}_Fingertip_{kv.Key}")
                { exists = true; break; }
            }
            if (exists) continue;

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"{label}_Fingertip_{kv.Key}";
            var col = go.GetComponent<Collider>(); if (col) col.isTrigger = true;

            if (!string.IsNullOrEmpty(indicatorLayerName))
            {
                int layer = LayerMask.NameToLayer(indicatorLayerName);
                if (layer >= 0) go.layer = layer;
            }

            var mr = go.GetComponent<MeshRenderer>();
            if (mr)
            {
                mr.material = new Material(mr.sharedMaterial);
                mr.material.color = indicatorColor;
            }

            go.transform.SetParent(tipTf, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * indicatorScale;

            indicatorList.Add(go);
        }
    }

    private static void SetActiveForList(List<GameObject> list, bool active)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var go = list[i];
            if (!go) { list.RemoveAt(i); continue; }
            if (go.activeSelf != active) go.SetActive(active);
        }
    }

    private static void ToggleListByHandReady(List<GameObject> list, bool handReady)
        => SetActiveForList(list, handReady);

    private static void Recolor(List<GameObject> list, Color c)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var go = list[i]; if (!go) { list.RemoveAt(i); continue; }
            var mr = go.GetComponent<MeshRenderer>();
            if (mr) mr.material.color = c;
        }
    }

    private static void Rescale(List<GameObject> list, float s)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var go = list[i]; if (!go) { list.RemoveAt(i); continue; }
            go.transform.localScale = Vector3.one * s;
        }
    }
}
