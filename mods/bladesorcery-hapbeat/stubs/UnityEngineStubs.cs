// Compile-check stubs — NOT shipped and NOT part of the mod DLL.
//
// These are the minimum UnityEngine surfaces the mod touches, so that `dotnet build` on
// the compile-check project can verify the mod's own code without a Blade & Sorcery
// installation. The real types come from the game's UnityEngine*.dll at mod build time.
//
// Only members the mod actually uses are declared. Bodies are inert.

namespace UnityEngine
{
    public struct Vector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public float sqrMagnitude
        {
            get { return x * x + y * y + z * z; }
        }

        public static Vector3 operator -(Vector3 a, Vector3 b)
        {
            return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        }
    }

    public class Transform
    {
        public Vector3 position;
    }

    public static class Time
    {
        public static float time;
        public static float unscaledTime;
        public static float deltaTime;
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
    }
}
