using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace BigBlit.ActivePack.Buttons
{
    public class ParticlesController : MonoBehaviour
    {
        private ParticleSystem _ps;

        private void Awake() {
            _ps = GetComponent<ParticleSystem>();
        }

        public float RateOverTime {
            set {
                var emission = _ps.emission;
                emission.rateOverTime = value;
            }
        }

        public float StartSpeed {
            set {
                var main = _ps.main;
                main.startSpeed = value;
            }
        }
    }
}