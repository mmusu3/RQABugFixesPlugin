using System;
using System.Text;
using Torch.Managers.PatchManager;
using VRage.Utils;

namespace RQABugFixes;

// Fix Torch exception during startup
[PatchShim]
static class TorchLogPatch_Patch
{
    public static void Patch(PatchContext ctx)
    {
        var source = Type.GetType("Torch.Patches.KeenLogPatch, Torch")!.GetMethod("PrepareLog", _public: false, _static: true);
        var target = typeof(TorchLogPatch_Patch).GetMethod(nameof(Prefix), _public: false, _static: true);

        ctx.GetPattern(source).Prefixes.Add(target);
    }

    static bool Prefix(MyLog log, ref StringBuilder __field__tmpStringBuilder,
        Func<MyLog, int, int> __field__getIndentByThread, ref StringBuilder __result)
    {
        __result = PrepareLog(log, ref __field__tmpStringBuilder, __field__getIndentByThread);
        return false;
    }

    static StringBuilder PrepareLog(MyLog log, ref StringBuilder _tmpStringBuilder,
        Func<MyLog, int, int> _getIndentByThread)
    {
        _tmpStringBuilder ??= new StringBuilder();
        _tmpStringBuilder.Clear();

        int threadId = Environment.CurrentManagedThreadId;
        int indent = 0;

        if (log.LogEnabled)
            indent = _getIndentByThread(log, threadId);

        _tmpStringBuilder.Append(' ', indent * 3);

        return _tmpStringBuilder;
    }
}
