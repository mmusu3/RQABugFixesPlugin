using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using NLog;
using Torch;
using Torch.Managers.PatchManager.MSIL;

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

static class PatchHelper
{
    internal static bool MatchOPCodes(MsilInstruction[] instructions, int start, OpCode[] opcodes)
    {
        if (instructions.Length < start + opcodes.Length)
            return false;

        for (int i = 0; i < opcodes.Length; i++)
        {
            if (instructions[start + i].OpCode != opcodes[i])
                return false;
        }

        return true;
    }
}
