using UnityEngine;
using UnityEngine.UI;

namespace TriLibCore.Samples
{
    /// <summary>
    /// Demonstrates how to load and cancel model loading process.
    /// </summary>
    public class CancelLoadingSample : MonoBehaviour
    {
        /// <summary>
        /// Reference to the UI element shown when cancellation is in progress.
        /// </summary>
        [SerializeField]
        private GameObject _cancellingText;

        /// <summary>
        /// Reference to the UI button used to cancel model loading.
        /// </summary>
        [SerializeField]
        private GameObject _cancelLoadingButton;

        /// <summary>
        /// Holds the current <see cref="AssetLoaderContext"/> used to control the loading process.
        /// </summary>
        private AssetLoaderContext _context;

        /// <summary>
        /// Reference to the UI button used to initiate model loading.
        /// </summary>
        [SerializeField]
        private GameObject _loadModelButton;

        /// <summary>
        /// Reference to the UI text element that displays loading progress.
        /// </summary>
        [SerializeField]
        private Text _progressText;

        /// <summary>
        /// Cancels the current model loading operation, if any.
        /// </summary>
        public void CancelLoading()
        {
            if (_context == null || _context.CancellationTokenSource.IsCancellationRequested)
            {
                return;
            }
            _context.CancellationTokenSource.Cancel();
            _cancelLoadingButton.SetActive(false);
            _cancellingText.SetActive(true);
            _progressText.gameObject.SetActive(false);
        }

        /// <summary>
        /// Starts the asynchronous model loading process using the file picker.
        /// </summary>
        public void LoadModel()
        {
            if (_context != null)
            {
                return;
            }
            var assetLoaderFilePicker = AssetLoaderFilePicker.Create();
            assetLoaderFilePicker.LoadModelFromFilePickerAsync(
                "Load Model",
                onProgress: OnProgress,
                onMaterialsLoad: OnMaterialsLoad,
                onError: OnError
            );
            _loadModelButton.SetActive(false);
            _cancelLoadingButton.SetActive(true);
            _progressText.gameObject.SetActive(true);
        }

        /// <summary>
        /// Cleans up UI and internal state after loading or cancellation.
        /// </summary>
        private void CleanUp()
        {
            _context = null;
            _loadModelButton.SetActive(true);
            _cancelLoadingButton.SetActive(false);
            _cancellingText.SetActive(false);
            _progressText.gameObject.SetActive(false);
        }

        /// <summary>
        /// Handles any error that occurs during the model loading process.
        /// </summary>
        /// <param name="error">The error information.</param>
        private void OnError(IContextualizedError error)
        {
            Debug.LogError(error.ToString());
            CleanUp();
        }

        /// <summary>
        /// Called once the materials of the model are loaded.
        /// </summary>
        /// <param name="context">The loader context containing the loaded model information.</param>
        private void OnMaterialsLoad(AssetLoaderContext context)
        {
            CleanUp();
        }

        /// <summary>
        /// Called periodically to report loading progress.
        /// Stores the loader context when received for the first time.
        /// </summary>
        /// <param name="context">The loader context.</param>
        /// <param name="progress">The current loading progress (0 to 1).</param>
        private void OnProgress(AssetLoaderContext context, float progress)
        {
            if (_context == null)
            {
                _context = context;
            }
            _progressText.text = string.Format("Loading progress: {0:P}", progress);
        }
    }
}
