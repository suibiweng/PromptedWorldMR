/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using Meta.XR.Samples;
using UnityEngine;

namespace Meta.XR.MRUtilityKitSamples.Basic
{
    [MetaCodeSample("MRUKSample-Basic")]
    public class WorldLockTint : MonoBehaviour
    {
        // Show a red tint on the effect mesh when world locking is inactive
        private static readonly Color InactiveColor = new Color(0.8666667f, 0.145098f, 0.1930136f);
        private bool? _wasWorldLockActive;
        private Color _defaultColor;

        private void ChangeEffectMeshObjectsColor(bool worldLockActive)
        {
            var effectMeshObjects = GetComponent<EffectMesh>().EffectMeshObjects;
            foreach (var effectMeshObject in effectMeshObjects)
            {
                effectMeshObject.Value.effectMeshGO.GetComponent<Renderer>().material.color = worldLockActive ? _defaultColor : InactiveColor;
            }
        }

        void Update()
        {
            if (_wasWorldLockActive == null)
            {
                _defaultColor = GetComponent<EffectMesh>().MeshMaterial.color;
            }

            bool worldLockActive = MRUK.Instance.IsWorldLockActive;
            if (worldLockActive != _wasWorldLockActive)
            {
                ChangeEffectMeshObjectsColor(worldLockActive);
                _wasWorldLockActive = worldLockActive;
            }
        }
    }
}
