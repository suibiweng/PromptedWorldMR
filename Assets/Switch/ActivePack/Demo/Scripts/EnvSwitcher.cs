using UnityEngine;
using System.Collections;
using UnityEngine.Assertions;

namespace BigBlit.ActivePack.Buttons
{
    public class EnvSwitcher : MonoBehaviour
    {

        [SerializeField] Material[] _skyBoxes = null;
  
        [SerializeField] ReflectionProbe _probe = null;

        private int _activeEnvId;

        private void Start() {
            Assert.IsNotNull(_probe);
            _probe.RenderProbe();
            DynamicGI.UpdateEnvironment();
        }

        private void Update() {
            if (_skyBoxes.Length == 0)
                return;

            if(Input.GetKeyDown(KeyCode.E)) {
                _activeEnvId++;
                if (_activeEnvId >= _skyBoxes.Length)
                    _activeEnvId = 0;
                RenderSettings.skybox = _skyBoxes[_activeEnvId];
                _probe.RenderProbe();
                DynamicGI.UpdateEnvironment();
            }
        } 
    }
}
