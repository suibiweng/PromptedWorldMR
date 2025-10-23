#pragma warning disable 649
using System.Collections.Generic;
using TriLibCore.Extensions;
using UnityEngine;
using UnityEngine.UI;
namespace TriLibCore.Samples
{
    /// <summary>
    /// Demonstrates how to load a 3D model using the built-in TriLib file picker and retrieve 
    /// custom user properties embedded within the model. Displays the retrieved properties 
    /// in the Unity UI.
    /// </summary>
    public class UserPropertiesLoadingSample : MonoBehaviour
    {
        /// <summary>
        /// Caches user properties of type <see cref="float"/>, keyed by "[GameObjectName].[PropertyName]".
        /// </summary>
        private Dictionary<string, float> _floatValues;

        /// <summary>
        /// Caches user properties of type <see cref="int"/>, keyed by "[GameObjectName].[PropertyName]".
        /// </summary>
        private Dictionary<string, int> _intValues;

        /// <summary>
        /// Caches user properties of type <see cref="Vector2"/>, keyed by "[GameObjectName].[PropertyName]".
        /// </summary>
        private Dictionary<string, Vector2> _vector2Values;

        /// <summary>
        /// Caches user properties of type <see cref="Vector3"/>, keyed by "[GameObjectName].[PropertyName]".
        /// </summary>
        private Dictionary<string, Vector3> _vector3Values;

        /// <summary>
        /// Caches user properties of type <see cref="Vector4"/>, keyed by "[GameObjectName].[PropertyName]".
        /// </summary>
        private Dictionary<string, Vector4> _vector4Values;

        /// <summary>
        /// Caches user properties of type <see cref="Color"/>, keyed by "[GameObjectName].[PropertyName]".
        /// </summary>
        private Dictionary<string, Color> _colorValues;

        /// <summary>
        /// Caches user properties of type <see cref="bool"/>, keyed by "[GameObjectName].[PropertyName]".
        /// </summary>
        private Dictionary<string, bool> _boolValues;

        /// <summary>
        /// Caches user properties of type <see cref="string"/>, keyed by "[GameObjectName].[PropertyName]".
        /// </summary>
        private Dictionary<string, string> _stringValues;

        /// <summary>
        /// Holds a reference to the last loaded model's root <see cref="GameObject"/>.
        /// When loading a new model, any previously loaded GameObject is destroyed.
        /// </summary>
        private GameObject _loadedGameObject;

        /// <summary>
        /// A reference to a UI Button used to start the file-picker and load a model.
        /// </summary>
        [SerializeField]
        private Button _loadModelButton;

        /// <summary>
        /// A UI Text component used to display real-time loading progress to the user.
        /// </summary>
        [SerializeField]
        private Text _progressText;

        /// <summary>
        /// A UI Text component used to display the user properties associated with the loaded model.
        /// </summary>
        [SerializeField]
        private Text _propertiesText;

        /// <summary>
        /// Cached set of TriLib loader options, including a custom <see cref="UserPropertiesMapper"/> 
        /// to capture user properties from the loaded model.
        /// </summary>
        private AssetLoaderOptions _assetLoaderOptions;

        /// <summary>
        /// Callback invoked by the custom <c>SampleUserPropertiesMapper</c> for every user property 
        /// found on the loaded model’s <see cref="GameObject"/>. Populates internal dictionaries 
        /// based on property type.
        /// </summary>
        /// <param name="gameObject">The <see cref="GameObject"/> that owns the user property.</param>
        /// <param name="propertyName">The user property’s name.</param>
        /// <param name="propertyValue">The user property’s value, which may be float, int, Vector2, etc.</param>
        private void OnUserDataProcessed(GameObject gameObject, string propertyName, object propertyValue)
        {
            var propertyKey = $"{gameObject.name}.{propertyName}";
            Debug.Log($"Found property for [{gameObject.name}] ({propertyName}:{propertyValue})");
            switch (propertyValue)
            {
                case float floatValue:
                    if (!_floatValues.ContainsKey(propertyKey))
                    {
                        _floatValues.Add(propertyKey, floatValue);
                    }
                    break;
                case int intValue:
                    if (!_intValues.ContainsKey(propertyKey))
                    {
                        _intValues.Add(propertyKey, intValue);
                    }
                    break;
                case Vector2 vector2Value:
                    if (!_vector2Values.ContainsKey(propertyKey))
                    {
                        _vector2Values.Add(propertyKey, vector2Value);
                    }
                    break;
                case Vector3 vector3Value:
                    if (!_vector3Values.ContainsKey(propertyKey))
                    {
                        _vector3Values.Add(propertyKey, vector3Value);
                    }
                    break;
                case Vector4 vector4Value:
                    if (!_vector4Values.ContainsKey(propertyKey))
                    {
                        _vector4Values.Add(propertyKey, vector4Value);
                    }
                    break;
                case Color colorValue:
                    if (!_colorValues.ContainsKey(propertyKey))
                    {
                        _colorValues.Add(propertyKey, colorValue);
                    }
                    break;
                case bool boolValue:
                    if (!_boolValues.ContainsKey(propertyKey))
                    {
                        _boolValues.Add(propertyKey, boolValue);
                    }
                    break;
                case string stringValue:
                    if (!_stringValues.ContainsKey(propertyKey))
                    {
                        _stringValues.Add(propertyKey, stringValue);
                    }
                    break;
            }
        }

        /// <summary>
        /// Initiates a file-picker dialog for selecting a model to load, constructing 
        /// custom TriLib <see cref="AssetLoaderOptions"/> if necessary, and registering 
        /// event handlers for progress, errors, and completion.
        /// </summary>
        /// <remarks>
        /// The file-picker interface will allow the user to select a model file at runtime. 
        /// Once selected, TriLib begins loading and triggers corresponding events 
        /// (<see cref="OnBeginLoad"/>, <see cref="OnProgress"/>, <see cref="OnLoad"/>, 
        /// <see cref="OnMaterialsLoad"/>, and <see cref="OnError"/>).
        /// </remarks>
        public void LoadModel()
        {
            var assetLoaderOptions = CreateAssetLoaderOptions();
            var assetLoaderFilePicker = AssetLoaderFilePicker.Create();
            assetLoaderFilePicker.LoadModelFromFilePickerAsync(
                title: "Select a Model file",
                onLoad: OnLoad,
                onMaterialsLoad: OnMaterialsLoad,
                onProgress: OnProgress,
                onBeginLoad: OnBeginLoad,
                onError: OnError,
                wrapperGameObject: null,
                assetLoaderOptions: assetLoaderOptions
            );
        }

        /// <summary>
        /// Creates and configures <see cref="AssetLoaderOptions"/>, attaching a custom 
        /// <see cref="SampleUserPropertiesMapper"/> to capture user property data via 
        /// <see cref="OnUserDataProcessed"/>.
        /// </summary>
        /// <returns>A configured <see cref="AssetLoaderOptions"/> instance.</returns>
        private AssetLoaderOptions CreateAssetLoaderOptions()
        {
            if (_assetLoaderOptions == null)
            {
                _assetLoaderOptions = AssetLoader.CreateDefaultLoaderOptions(false, true);

                var userPropertiesMapper = ScriptableObject.CreateInstance<SampleUserPropertiesMapper>();
                userPropertiesMapper.OnUserDataProcessed += OnUserDataProcessed;
                _assetLoaderOptions.UserPropertiesMapper = userPropertiesMapper;
            }
            return _assetLoaderOptions;
        }

        /// <summary>
        /// Event handler invoked when the user begins loading a model (i.e., once a file has been selected).
        /// Initializes or clears the property dictionaries, updates the UI, and locks the load button 
        /// until the process completes or is canceled.
        /// </summary>
        /// <param name="filesSelected">Indicates whether the user has chosen a file.</param>
        private void OnBeginLoad(bool filesSelected)
        {
            // Reset dictionaries
            _floatValues = new Dictionary<string, float>();
            _intValues = new Dictionary<string, int>();
            _vector2Values = new Dictionary<string, Vector2>();
            _vector3Values = new Dictionary<string, Vector3>();
            _vector4Values = new Dictionary<string, Vector4>();
            _colorValues = new Dictionary<string, Color>();
            _boolValues = new Dictionary<string, bool>();
            _stringValues = new Dictionary<string, string>();

            // Update UI
            _loadModelButton.interactable = !filesSelected;
            _progressText.enabled = filesSelected;
            _progressText.text = string.Empty;
        }

        /// <summary>
        /// Event handler invoked if an error occurs during model loading.
        /// Logs the error details for debugging.
        /// </summary>
        /// <param name="obj">Contains error context and the original exception.</param>
        private void OnError(IContextualizedError obj)
        {
            Debug.LogError($"An error occurred while loading your model: {obj.GetInnerException()}");
        }

        /// <summary>
        /// Event handler for reporting model-loading progress, which can be displayed on-screen.
        /// </summary>
        /// <param name="assetLoaderContext">Provides context about the loading process.</param>
        /// <param name="progress">A value from 0.0 to 1.0 representing the loading progress.</param>
        private void OnProgress(AssetLoaderContext assetLoaderContext, float progress)
        {
            _progressText.text = $"Progress: {progress:P}";
        }

        /// <summary>
        /// Event handler invoked once textures and materials have finished loading and the model is fully ready.
        /// At this point, user properties have already been processed, so they can be displayed via <see cref="ListProperties"/>.
        /// </summary>
        /// <remarks>
        /// If <see cref="assetLoaderContext.RootGameObject"/> is <c>null</c>, the model may have failed to load.
        /// </remarks>
        /// <param name="assetLoaderContext">Contains references to the loaded model’s root <see cref="GameObject"/>.</param>
        private void OnMaterialsLoad(AssetLoaderContext assetLoaderContext)
        {
            if (assetLoaderContext.RootGameObject != null)
            {
                Debug.Log("Model fully loaded.");
                ListProperties();
            }
            else
            {
                Debug.Log("Model could not be loaded.");
            }
            // Re-enable UI elements
            _loadModelButton.interactable = true;
            _progressText.enabled = false;
        }

        /// <summary>
        /// Compiles the collected user properties into a formatted text block, separating each 
        /// data type for clarity, and updates the on-screen <see cref="_propertiesText"/>.
        /// </summary>
        private void ListProperties()
        {
            var text = string.Empty;

            // String properties
            if (_stringValues.Count > 0)
            {
                text += "<b>String</b>\n";
                foreach (var kvp in _stringValues)
                {
                    text += $"{kvp.Key}=\"{kvp.Value}\"\n";
                }
            }

            // Float properties
            if (_floatValues.Count > 0)
            {
                text += "\n<b>Float</b>\n";
                foreach (var kvp in _floatValues)
                {
                    text += $"{kvp.Key}={kvp.Value}\n";
                }
            }

            // Integer properties
            if (_intValues.Count > 0)
            {
                text += "\n<b>Integer</b>\n";
                foreach (var kvp in _intValues)
                {
                    text += $"{kvp.Key}={kvp.Value}\n";
                }
            }

            // Boolean properties
            if (_boolValues.Count > 0)
            {
                text += "\n<b>Boolean</b>\n";
                foreach (var kvp in _boolValues)
                {
                    text += $"{kvp.Key}={kvp.Value}\n";
                }
            }

            // Vector2 properties
            if (_vector2Values.Count > 0)
            {
                text += "\n<b>Vector2</b>\n";
                foreach (var kvp in _vector2Values)
                {
                    text += $"{kvp.Key}={kvp.Value}\n";
                }
            }

            // Vector3 properties
            if (_vector3Values.Count > 0)
            {
                text += "\n<b>Vector3</b>\n";
                foreach (var kvp in _vector3Values)
                {
                    text += $"{kvp.Key}={kvp.Value}\n";
                }
            }

            // Vector4 properties
            if (_vector4Values.Count > 0)
            {
                text += "\n<b>Vector4</b>\n";
                foreach (var kvp in _vector4Values)
                {
                    text += $"{kvp.Key}={kvp.Value}\n";
                }
            }

            // Color properties
            if (_colorValues.Count > 0)
            {
                text += "\n<b>Color</b>\n";
                foreach (var kvp in _colorValues)
                {
                    // Embed color tags around the text for a visual effect
                    text += "<color=#" + ColorUtility.ToHtmlStringRGB(kvp.Value) + ">";
                    text += $"{kvp.Key}={kvp.Value}\n";
                    text += "</color>";
                }
            }

            _propertiesText.text = string.IsNullOrEmpty(text)
                ? "The model has no user properties"
                : $"<b>Model User Properties</b>\n\n{text}";
        }

        /// <summary>
        /// Event handler invoked once the model’s meshes and hierarchy are loaded 
        /// (but before textures and materials are fully processed).
        /// Destroys the previously loaded model, if any, and fits the camera view 
        /// to the newly loaded model’s bounds.
        /// </summary>
        /// <param name="assetLoaderContext">Contains references to the loaded model’s root <see cref="GameObject"/>.</param>
        private void OnLoad(AssetLoaderContext assetLoaderContext)
        {
            // Clean up the previously loaded GameObject
            if (_loadedGameObject != null)
            {
                Destroy(_loadedGameObject);
            }

            _loadedGameObject = assetLoaderContext.RootGameObject;
            if (_loadedGameObject != null && Camera.main != null)
            {
                Camera.main.FitToBounds(assetLoaderContext.RootGameObject, 4f);
            }
        }
    }
}
