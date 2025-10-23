using System;
using TriLibCore.Interfaces;
using TriLibCore.Mappers;
using UnityEngine;

namespace TriLibCore.Samples
{
    /// <summary>
    /// A custom <see cref="UserPropertiesMapper"/> that forwards user properties 
    /// (attached to model GameObjects) to a user-specified callback.
    /// </summary>
    public class SampleUserPropertiesMapper : UserPropertiesMapper
    {
        /// <summary>
        /// A callback invoked for each user property discovered during model loading.
        /// </summary>
        /// <remarks>
        /// This <see cref="Action{GameObject, String, Object}"/> receives the following parameters:
        /// <list type="bullet">
        /// <item>
        /// <description><c>GameObject</c>: The GameObject to which the user property is attached.</description>
        /// </item>
        /// <item>
        /// <description><c>string</c>: The property’s name.</description>
        /// </item>
        /// <item>
        /// <description><c>object</c>: The property’s value (may be various types, such as <see cref="float"/>, <see cref="int"/>, <see cref="string"/>, etc.).</description>
        /// </item>
        /// </list>
        /// </remarks>
        public Action<GameObject, string, object> OnUserDataProcessed;

        /// <summary>
        /// Overrides the default TriLib user property processing to invoke 
        /// the <see cref="OnUserDataProcessed"/> callback (if not null).
        /// </summary>
        /// <param name="assetLoaderContext">Contains context about the current model loading process.</param>
        /// <param name="gameObject">The <see cref="GameObject"/> that owns the current user property.</param>
        /// <param name="propertyName">The name of the user property.</param>
        /// <param name="propertyValue">The value of the user property, potentially in various types.</param>
        public override void OnProcessUserData(
            AssetLoaderContext assetLoaderContext,
            GameObject gameObject,
            string propertyName,
            object propertyValue)
        {
            if (OnUserDataProcessed != null)
            {
                OnUserDataProcessed(gameObject, propertyName, propertyValue);
            }
        }
    }
}
