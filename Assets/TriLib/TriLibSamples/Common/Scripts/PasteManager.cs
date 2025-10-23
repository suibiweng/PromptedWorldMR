using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TriLibCore.Samples
{
    /// <summary>
    /// Provides paste functionality to WebGL builds via a singleton manager.
    /// This class automatically sets itself up in WebGL to handle text paste events,
    /// allowing them to be inserted into Unity's <see cref="InputField"/> components.
    /// </summary>
    public class PasteManager : MonoBehaviour
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>
        /// Interop method for setting up WebGL-specific paste handling.
        /// </summary>
        [DllImport("__Internal")]
        private static extern void PasteManagerSetup();
#endif

        /// <summary>
        /// Gets the PasteManager singleton instance.
        /// If no instance is found in the scene, one will be created automatically.
        /// </summary>
        public static PasteManager Instance { get; private set; }

        /// <summary>
        /// Checks if a PasteManager instance already exists.
        /// If none is found, a new <see cref="GameObject"/> with a PasteManager is created.
        /// </summary>
        public static void CheckInstance()
        {
            if (Instance == null)
            {
                Instance = new GameObject("PasteManager").AddComponent<PasteManager>();
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>
        /// On startup (WebGL builds only), calls <c>PasteManagerSetup()</c> for browser-specific configuration.
        /// </summary>
        private void Start()
        {
            PasteManagerSetup();
        }
#endif

        /// <summary>
        /// Called when the user pastes the specified text within the WebGL build.
        /// Inserts the pasted text into a currently selected <see cref="InputField"/>, if available.
        /// </summary>
        /// <param name="value">The text value that was pasted.</param>
        public void Paste(string value)
        {
            var currentCurrentSelectedGameObject = EventSystem.current.currentSelectedGameObject;
            if (currentCurrentSelectedGameObject != null)
            {
                var inputField = currentCurrentSelectedGameObject.GetComponentInChildren<InputField>();
                if (inputField != null)
                {
                    // Insert the pasted text at the selection anchor position
                    var newText = $"{inputField.text.Substring(0, inputField.selectionAnchorPosition)}"
                                  + $"{value}"
                                  + $"{inputField.text.Substring(inputField.selectionFocusPosition)}";
                    inputField.text = newText;
                }
            }
        }
    }
}
