using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Torch.Managers.PatchManager;
using Torch.Managers.PatchManager.MSIL;
using VRage.Sync;

namespace RQABugFixes;

[PatchShim]
static class MyCubeGrid_Patches
{
    public static void Patch(PatchContext ctx)
    {
        MethodInfo source;
        MethodInfo transpiler;

        source = typeof(MyCubeGrid).GetMethod("InitInternal", _public: false, _static: false);
        transpiler = typeof(MyCubeGrid_Patches).GetMethod(nameof(Transpile_InitInternal), _public: false, _static: true);
        ctx.GetPattern(source).Transpilers.Add(transpiler);
    }

    static IEnumerable<MsilInstruction> Transpile_InitInternal(IEnumerable<MsilInstruction> instructionStream)
    {
        Plugin.Log.Debug($"Patching {nameof(MyCubeGrid)}.InitInternal.");

        const int expectedParts = 1;
        int patchedParts = 0;

        var systemsInitMethod = typeof(MyCubeGridSystems).GetMethod(nameof(MyCubeGridSystems.Init), _public: true, _static: false);
        var handBrakeSyncField = typeof(MyCubeGrid).GetField("m_handBrakeSync", BindingFlags.Instance | BindingFlags.NonPublic);
        var isParkedProperty = typeof(MyCubeGrid).GetProperty(nameof(MyCubeGrid.IsParked), BindingFlags.Instance | BindingFlags.Public);
        var setLocalValueMethod = typeof(Sync<bool, SyncDirection.BothWays>).GetMethod(nameof(Sync<bool, SyncDirection.BothWays>.SetLocalValue), BindingFlags.Instance | BindingFlags.Public);

        var instructions = instructionStream.ToArray();

        for (int i = 0; i < instructions.Length; i++)
        {
            var ins = instructions[i];

            yield return ins;

            if (ins.OpCode == OpCodes.Callvirt && ins.Operand is MsilOperandInline<MethodBase> callOperand)
            {
                var callMethod = callOperand.Value;

                if (callMethod == systemsInitMethod)
                {
                    yield return new MsilInstruction(OpCodes.Ldarg_0);
                    yield return new MsilInstruction(OpCodes.Ldfld).InlineValue(handBrakeSyncField);
                    yield return new MsilInstruction(OpCodes.Ldarg_0);
                    yield return new MsilInstruction(OpCodes.Callvirt).InlineValue(isParkedProperty.GetMethod);
                    yield return new MsilInstruction(OpCodes.Callvirt).InlineValue(setLocalValueMethod);
                    patchedParts++;
                }
            }
        }

        if (patchedParts != expectedParts)
            Plugin.Log.Fatal($"Failed to patch {nameof(MyCubeGrid)}.InitInternal. {patchedParts} out of {expectedParts} code parts matched.");
        else
            Plugin.Log.Debug("Patch successful.");
    }
}
