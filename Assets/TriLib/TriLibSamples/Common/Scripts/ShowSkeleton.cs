using System;
using System.Collections.Generic;
using TriLibCore.Extensions;
using UnityEngine;

namespace TriLibCore.Samples
{
    /// <summary>
    /// Displays the skeletal hierarchy of a loaded model by drawing lines between each bone (transform).
    /// </summary>
    public class ShowSkeleton : MonoBehaviour
    {
        /// <summary>
        /// A shared material used to render bone connections in <see cref="OnRenderObject"/>.
        /// </summary>
        private static Material _material;

        /// <summary>
        /// Holds references to the bone transforms that make up the model's skeleton.
        /// </summary>
        private List<Transform> _bones;
        /// <summary>
        /// Initializes this skeleton display by collecting bone transforms from the loaded model, 
        /// and optionally adjusts the scene bounds in the provided <see cref="AssetViewer"/>.
        /// </summary>
        /// <param name="assetLoaderContext">
        /// The <see cref="AssetLoaderContext"/> containing the loaded model hierarchy.
        /// </param>
        /// <param name="assetViewer">
        /// The <see cref="AssetViewer"/> controlling the camera and scene visualization.
        /// </param>
        public void Setup(AssetLoaderContext assetLoaderContext, AssetViewer assetViewer)
        {
            _bones = new List<Transform>();
            // Gathers all bones into _bones
            assetLoaderContext.RootModel.GetBones(assetLoaderContext, _bones);
            if (_bones.Count > 0)
            {
                // Updates the viewer bounds if bones exist
                SetCustomBounds(assetViewer);
            }
        }

        /// <summary>
        /// Draws lines between parent and child bones in the Scene view for debugging.
        /// </summary>
        private void OnDrawGizmos()
        {
            if (_bones != null)
            {
                foreach (var transform in _bones)
                {
                    foreach (Transform child in transform)
                    {
                        Gizmos.color = Color.green;
                        Gizmos.DrawLine(transform.position, child.position);
                    }
                }
            }
        }

        /// <summary>
        /// Renders lines between bone transforms each frame at runtime using immediate-mode drawing. 
        /// This provides a live view of the skeleton structure in Play Mode.
        /// </summary>
        private void OnRenderObject()
        {
            _material.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);
            foreach (var transform in _bones)
            {
                foreach (Transform child in transform)
                {
                    GL.Color(Color.green);
                    GL.Vertex(transform.position);
                    GL.Vertex(child.position);
                }
            }
            GL.End();
            GL.PopMatrix();
        }

        /// <summary>
        /// Calculates a bounding box encompassing all bones of the loaded model over all animations,
        /// then updates the camera position in the <see cref="AssetViewer"/> accordingly.
        /// </summary>
        /// <param name="assetViewer">
        /// The <see cref="AssetViewer"/> whose bounds and camera will be adjusted.
        /// </param>
        private void SetCustomBounds(AssetViewer assetViewer)
        {
            var totalBounds = new Bounds();
            var totalBoundsInitialized = false;
            if (assetViewer.RootGameObject.TryGetComponent<Animation>(out var animation))
            {
                var animationClips = animation.GetAllAnimationClips();
                // Iterate through each animation clip and frame to expand the total skeleton bounds
                foreach (var clip in animationClips)
                {
                    animation.clip = clip;
                    var frameInterval = 1f / clip.frameRate;
                    for (var t = 0f; t < clip.length; t += frameInterval)
                    {
                        animation[clip.name].time = t;
                        animation.Sample();
                        var bounds = new Bounds();
                        var initialized = false;
                        foreach (var bone in _bones)
                        {
                            if (!initialized)
                            {
                                bounds.center = bone.position;
                                if (!totalBoundsInitialized)
                                {
                                    totalBounds.center = bone.position;
                                    totalBoundsInitialized = true;
                                }
                                initialized = true;
                            }
                            else
                            {
                                bounds.Encapsulate(bone.position);
                            }
                        }
                        totalBounds.Encapsulate(bounds);
                    }
                }
            }
            // If we have a valid bounding volume, tell the AssetViewer to adjust camera settings
            if (totalBounds.size.magnitude > 0f)
            {
                assetViewer.SetCustomBounds(totalBounds);
            }
        }

        /// <summary>
        /// Ensures that a material is ready for rendering the skeleton lines, and 
        /// if needed, automatically collects transforms as bones from the object hierarchy.
        /// </summary>
        private void Start()
        {
            if (_material == null)
            {
                _material = new Material(Shader.Find("Hidden/ShowSkeleton"));
            }
            if (_bones == null)
            {
                _bones = new List<Transform>();
                var transforms = GetComponentsInChildren<Transform>();
                foreach (var transform in transforms)
                {
                    _bones.Add(transform);
                }
            }
        }
    }
}
