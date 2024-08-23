using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using NLog;
using Torch;

namespace RQABugFixes;

public class Plugin : TorchPluginBase
{
    internal static readonly Logger Log = LogManager.GetLogger("RQABugFixes");
}

static class ReflectionHelper
{
    static T ThrowIfNull<T>(T? obj, string methodName, [CallerMemberName] string callerName = null!)
    {
        if (obj == null) throw new NullReferenceException($"{callerName} returned null looking for {methodName}.");

        return obj;
    }

    public static MethodInfo GetMethod(this Type type, string methodName, bool _public, bool _static)
    {
        return ThrowIfNull(type.GetMethod(methodName, (_public ? BindingFlags.Public : BindingFlags.NonPublic) | (_static ? BindingFlags.Static : BindingFlags.Instance)), methodName);
    }
}
