#if UNITY_EDITOR
using System;
using UnityEngine;

namespace Hapbeat.Boxing
{
    // Editor-only test code in a runtime assembly so Unity can actually attach/tick the behaviour.
    // It is never included in the player build or saved in a scene.
    [DefaultExecutionOrder(-300)]
    public sealed class BoxingSmokeDriver : MonoBehaviour
    {
        public Action Sample;
        private void Update() => Sample?.Invoke();
    }
}
#endif
