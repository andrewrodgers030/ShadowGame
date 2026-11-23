using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ShadowFrit
{
    /// Look-dev camera: right-mouse drag to orbit, scroll to zoom, middle-mouse drag to pan.
    public class LookDevOrbit : MonoBehaviour
    {
        public Vector3 target = new Vector3(0f, 1f, 0f);
        public float distance = 4f;
        public float yaw;
        public float pitch = 10f;
        public float orbitSpeed = 0.25f;
        public float zoomSpeed = 0.002f;
        public float panSpeed = 0.004f;

        void LateUpdate()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 delta = mouse.delta.ReadValue();
            if (mouse.rightButton.isPressed)
            {
                yaw += delta.x * orbitSpeed;
                pitch = Mathf.Clamp(pitch - delta.y * orbitSpeed, -20f, 80f);
            }
            if (mouse.middleButton.isPressed)
                target += (-transform.right * delta.x - transform.up * delta.y) * panSpeed * distance;
            distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y * zoomSpeed * distance, 0.8f, 30f);
#endif
            Apply();
        }

        public void Apply()
        {
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(target + rot * new Vector3(0f, 0f, -distance), rot);
        }
    }
}
