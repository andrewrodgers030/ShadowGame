using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ShadowFrit
{
    /// Look-dev helper: starts the Animator in a chosen state; in Play mode press 1-4 to switch
    /// every previewed character between clips (Tab cycles).
    [RequireComponent(typeof(Animator))]
    public class AnimationPreview : MonoBehaviour
    {
        public string startState = "Walking";
        public string[] states = { "Walking", "Running", "Reaping_Swing", "Dead" };
        public float crossFade = 0.15f;

        Animator _anim;
        int _index;

        void Start()
        {
            _anim = GetComponent<Animator>();
            _index = Mathf.Max(0, System.Array.IndexOf(states, startState));
            _anim.Play(startState, 0, Random.value);
        }

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return;
            for (int i = 0; i < states.Length && i < 9; i++)
                if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) Switch(i);
            if (kb.tabKey.wasPressedThisFrame) Switch((_index + 1) % states.Length);
#endif
        }

        void Switch(int i)
        {
            _index = i;
            _anim.CrossFadeInFixedTime(states[i], crossFade);
        }
    }
}
