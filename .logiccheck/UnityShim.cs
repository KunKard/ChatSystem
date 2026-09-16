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
}
