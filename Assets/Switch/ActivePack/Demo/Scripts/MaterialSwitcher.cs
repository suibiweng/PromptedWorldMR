using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace BigBlit.ActivePack.Buttons
{
    public class MaterialSwitcher : MonoBehaviour
    {
        [SerializeField] Material[] _materialsSet1 = null;
        [SerializeField] Material[] _materialsSet2 = null;

        [SerializeField] Transform[] _rootsSet1 = null;
        [SerializeField] Transform[] _rootsSet2 = null;

        List<Renderer[]> _setsCache1 = new List<Renderer[]>();
        List<Renderer[]> _setsCache2 = new List<Renderer[]>();

        private int _selSetId;

        private void Start() {
            cacheSets();
        }

        void cacheSets() {
            _setsCache1.Clear();
            _setsCache2.Clear();

            if (_rootsSet1 == null || _rootsSet2 == null || _materialsSet1 == null || _materialsSet2 == null)
                return;

            foreach (var root in _rootsSet1)
                addToCache(root, ref _setsCache1);
            foreach (var root in _rootsSet2)
                addToCache(root, ref _setsCache2);
        }

        void addToCache(Transform transform, ref List<Renderer[]> set) {
            Renderer[] renderers = transform.GetComponentsInChildren<Renderer>();
            if (renderers != null && renderers.Length > 0)
                set.Add(renderers);
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.Q)) {
                selectNextSet();
            }
        }

        void selectNextSet() {
            int max = _materialsSet1.Length - 1;
            if (max < 0)
                return;

            _selSetId++;
            if (_selSetId >= max)
                _selSetId = 0;

            foreach (var set in _setsCache1) {
                foreach (var renderer in set) {
                    renderer.sharedMaterial = _materialsSet1[_selSetId];
                }
            }

            max = _materialsSet2.Length - 1;
            if (max < 0)
                return;

            foreach (var set in _setsCache2) {
                int id = Random.Range(0, max);
                foreach (var renderer in set) {
                    renderer.sharedMaterial = _materialsSet2[_selSetId];
                }
            }

        }
        void shuffleMaterials() {
            int max = _materialsSet1.Length - 1;
            if (max < 0)
                return;

            foreach (var set in _setsCache1) {
                int id = Random.Range(0, max);
                foreach (var renderer in set) {
                    renderer.sharedMaterial = _materialsSet1[id];
                }
            }

            max = _materialsSet2.Length - 1;
            if (max < 0)
                return;

            foreach (var set in _setsCache2) {
                int id = Random.Range(0, max);
                foreach (var renderer in set) {
                    renderer.sharedMaterial = _materialsSet2[id];
                }
            }
        }
    }
}