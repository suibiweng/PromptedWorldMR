using UnityEngine;

namespace TriLibCore.Samples
{
    /// <summary>
    /// Manages avatar movement and camera positioning in TriLib samples.
    /// This class extends <see cref="AbstractInputSystem"/> to handle user input,
    /// allowing the character to move and rotate smoothly, as well as updating the 
    /// camera’s position and orientation relative to the avatar.
    /// </summary>
    public class AvatarController : AbstractInputSystem
    {
        /// <summary>
        /// A singleton reference to the active <see cref="AvatarController"/> in the scene.
        /// Set in <see cref="Awake"/> to ensure only one active instance is present.
        /// </summary>
        public static AvatarController Instance { get; private set; }

        /// <summary>
        /// Maximum movement speed (in Unity units per second) that the avatar can reach.
        /// </summary>
        private const float MaxSpeed = 2f;

        /// <summary>
        /// Avatar acceleration (in Unity units per second) applied each frame 
        /// when the user provides movement input.
        /// </summary>
        private const float Acceleration = 5f;

        /// <summary>
        /// Rate at which the avatar slows down (in Unity units per second) 
        /// when there is no user input (applies friction to movement).
        /// </summary>
        private const float Friction = 2f;

        /// <summary>
        /// The rotation speed factor used to smoothly rotate the avatar toward the movement direction.
        /// </summary>
        private const float RotationSpeed = 60f;

        /// <summary>
        /// The <see cref="CharacterController"/> responsible for handling collisions 
        /// and movement for the avatar.
        /// </summary>
        public CharacterController CharacterController;

        /// <summary>
        /// The <see cref="Animator"/> that handles animation playback and blending for this avatar.
        /// </summary>
        public Animator Animator;

        /// <summary>
        /// The root <see cref="GameObject"/> representing the loaded avatar model.
        /// This can be replaced at runtime if a new avatar is loaded.
        /// </summary>
        public GameObject InnerAvatar;

        /// <summary>
        /// The camera’s offset from the avatar’s position, used to maintain a consistent view.
        /// Calculated during <see cref="Awake"/> and updated in <see cref="Update"/> to reflect rotation.
        /// </summary>
        private Vector3 _cameraOffset;

        /// <summary>
        /// The avatar’s current movement speed, dynamically updated each frame 
        /// based on <see cref="Acceleration"/>, <see cref="Friction"/>, and user input.
        /// </summary>
        private float _speed;

        /// <summary>
        /// A vertical offset to position the camera at the avatar’s head level, 
        /// derived from the <see cref="CharacterController"/>'s height.
        /// </summary>
        private Vector3 _cameraHeightOffset;

        /// <summary>
        /// Tracks the instantaneous velocity for smooth avatar rotation 
        /// in <c>Mathf.SmoothDampAngle</c>.
        /// </summary>
        private float _currentVelocity;

        /// <summary>
        /// Initializes the singleton instance and calculates the camera offsets 
        /// based on the avatar’s height. Called automatically when the script instance awakens.
        /// </summary>
        private void Awake()
        {
            Instance = this;

            // Determine where the camera should pivot from the avatar’s head level
            _cameraHeightOffset = new Vector3(0f, CharacterController.height * 0.8f, 0f);

            // Store the initial offset between the camera and the avatar
            _cameraOffset = Camera.main.transform.position - transform.position;
        }

        /// <summary>
        /// Handles user input and updates the avatar’s position, rotation, speed, and animator parameters.
        /// Also updates the camera position and orientation to stay behind the avatar.
        /// </summary>
        private void Update()
        {
            // Gather input along the horizontal and vertical axes
            var input = new Vector3(GetAxis("Horizontal"), 0f, GetAxis("Vertical"));

            // Transform input according to the camera’s orientation, ignoring vertical component
            var direction = Camera.main.transform.TransformDirection(input);
            direction.y = 0f;
            direction.Normalize();

            // Determine the avatar’s target orientation based on input direction
            var targetEulerAngles = direction.magnitude > 0
                ? Quaternion.LookRotation(direction).eulerAngles
                : transform.rotation.eulerAngles;

            // Smoothly rotate the avatar toward the movement direction
            var eulerAngles = transform.rotation.eulerAngles;
            eulerAngles.y = Mathf.SmoothDampAngle(
                eulerAngles.y,
                targetEulerAngles.y,
                ref _currentVelocity,
                Time.deltaTime * RotationSpeed * input.magnitude
            );
            transform.rotation = Quaternion.Euler(eulerAngles);

            // Update speed based on acceleration or friction
            _speed += input.magnitude * (Acceleration * MaxSpeed) * Time.deltaTime;
            _speed -= Friction * MaxSpeed * Time.deltaTime;
            _speed = Mathf.Clamp(_speed, 0f, MaxSpeed);

            // Move the avatar using the CharacterController
            CharacterController.SimpleMove(transform.forward * _speed);

            // Update the animator with the normalized speed
            Animator.SetFloat("SpeedFactor", _speed / MaxSpeed);

            // Adjust the camera based on the user’s camera angle (from AssetViewerBase) and stored offsets
            var pivotedPosition =
                Quaternion.AngleAxis(AvatarLoader.Instance.CameraAngle.x, Vector3.up) *
                Quaternion.AngleAxis(-AvatarLoader.Instance.CameraAngle.y, Vector3.right) *
                _cameraOffset;

            Camera.main.transform.position = transform.position + _cameraHeightOffset + pivotedPosition;
            Camera.main.transform.LookAt(transform.position + _cameraHeightOffset);
        }
    }
}
