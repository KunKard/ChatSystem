// 最小 UnityEngine 桩：只为在 Unity 之外编译并运行 ChatSystem 的 Data / Runtime 层。
// 这不是工程的一部分（.logiccheck 以点号开头，Unity 会忽略该目录）。
using System;

namespace UnityEngine
{
    public class Object
    {
        public string name;
    }

    public class ScriptableObject : Object
    {
    }

    public class Sprite : Object
    {
    }

    public static class Debug
    {
        public static void Log(object message) => Console.WriteLine("    [log]   " + message);
        public static void LogWarning(object message, Object context = null) => Console.WriteLine("    [WARN]  " + message);
        public static void LogError(object message, Object context = null) => Console.WriteLine("    [ERROR] " + message);
    }

    public static class Mathf
    {
        public static float Clamp(float value, float min, float max)
            => value < min ? min : (value > max ? max : value);

        public static int Clamp(int value, int min, int max)
            => value < min ? min : (value > max ? max : value);

        public static float Max(float a, float b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class CreateAssetMenuAttribute : Attribute
    {
        public string menuName;
        public string fileName;
        public int order;
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string tooltip) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class SerializeFieldAttribute : Attribute
    {
    }

    /// <summary>
    /// 桩里没有资源库，一律返回 null。
    /// </summary>
    /// <remarks>
    /// 这不是"为了让编译过去而糊弄一下" —— 返回 null 正是"工程里还没配资源库"
    /// 时的真实语义，所以调用方的判空分支反倒成了被验到的那一条。
    /// </remarks>
    public static class Resources
    {
        public static T Load<T>(string path) where T : Object => null;
    }
}
