using UnityEngine;
namespace Hakaton.Expeditions
{
    public sealed class WorldImpact:MonoBehaviour
    {
        public ParticleSystem particles;
        public Light flash;
        public float lifetime=.65f;
        float age;
        public void SetColour(Color color){var main=particles.main;main.startColor=color;flash.color=color;particles.Play();}
        void Update(){age+=Time.deltaTime;flash.intensity=Mathf.Max(0,2*(1-age/.3f));if(age>=lifetime)Destroy(gameObject);}
    }
}
