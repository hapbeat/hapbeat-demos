using Hapbeat;
using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>Separate resource prefab preserves manually authored scene haptic bindings.</summary>
    public sealed class BallImpactBindings : MonoBehaviour
    {
        public HapbeatEventBinding[] Bindings;
        public HapbeatUnityEventTrigger Find(DemoHapticEvent evt)
        {
            foreach (var binding in Bindings)
                if (binding.Event == evt) return binding.Trigger;
            return null;
        }
    }
}
