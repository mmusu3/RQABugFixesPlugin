using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Torch.Managers.PatchManager;
using Torch.Managers.PatchManager.MSIL;

namespace RQABugFixes;

[PatchShim]
static class MyBatteryBlock_Patches
{
    public static void Patch(PatchContext ctx)
    {
        MethodInfo source;
        MethodInfo transpiler;

        source = typeof(MyBatteryBlock).GetMethod(nameof(MyBatteryBlock.UpdateAfterSimulation100), _public: true, _static: false);
        transpiler = typeof(MyBatteryBlock_Patches).GetMethod(nameof(Transpile_UpdateAfterSimulation100), _public: false, _static: true);
        ctx.GetPattern(source).Transpilers.Add(transpiler);
    }

    // When using the Torch Concealment plugin, the delta time for battery blocks on concealed
    // grids will become very large. Once a battery block is unconcealed this causes some
    // batteries to instantly drain all their power depending on the power needs at the
    // time the grid is unconcealed. This patch caps the maximum delta time to avoid this.
    //
    // -- Replace the code that looks like this: --
    //
    //public override void UpdateAfterSimulation100()
    //{
    //    base.UpdateAfterSimulation100();
    //
    //    float num = (float)m_lastUpdateTime;
    //    m_lastUpdateTime = MySession.Static.GameplayFrameCounter;
    //
    //    if (!base.IsFunctional)
    //        return;
    //
    //    UpdateMaxOutputAndEmissivity();
    //
    //    float timeDeltaMs = ((float)MySession.Static.GameplayFrameCounter - num) * (1f / 60f) * 1000f;
    //
    //    .....
    //
    // -- With this: --
    //
    //public override void UpdateAfterSimulation100()
    //{
    //    base.UpdateAfterSimulation100();
    //
    //    float num;
    //
    //    DeltaTimeHelper(ref m_lastUpdateTime, out num);
    //
    //    if (!base.IsFunctional)
    //        return;
    //
    //    UpdateMaxOutputAndEmissivity();
    //
    //    float timeDeltaMs = num * (1f / 60f) * 1000f;
    //
    //    .....
    //
    static IEnumerable<MsilInstruction> Transpile_UpdateAfterSimulation100(IEnumerable<MsilInstruction> instructionStream)
    {
        Plugin.Log.Debug($"Patching {nameof(MyBatteryBlock)}.UpdateAfterSimulation100.");

        const int expectedParts = 2;
        int patchedParts = 0;

        var lastUpdateTimeField = typeof(MyBatteryBlock).GetField("m_lastUpdateTime", BindingFlags.Instance | BindingFlags.NonPublic);
        var sessionStaticProperty = typeof(MySession).GetProperty(nameof(MySession.Static), BindingFlags.Static | BindingFlags.Public);
        var frameCounterProperty = typeof(MySession).GetProperty(nameof(MySession.GameplayFrameCounter), BindingFlags.Instance | BindingFlags.Public);
        var deltaTimeHelperMethod = typeof(MyBatteryBlock_Patches).GetMethod(nameof(DeltaTimeHelper), BindingFlags.Static | BindingFlags.NonPublic);

        var pattern1 = new OpCode[] { OpCodes.Ldarg_0, OpCodes.Ldfld, OpCodes.Conv_R4, OpCodes.Stloc_0, OpCodes.Ldarg_0, OpCodes.Call, OpCodes.Callvirt, OpCodes.Stfld };
        var pattern2 = new OpCode[] { OpCodes.Call, OpCodes.Callvirt, OpCodes.Conv_R4, OpCodes.Ldloc_0, OpCodes.Sub };

        var instructions = instructionStream.ToArray();

        for (int i = 0; i < instructions.Length; i++)
        {
            var ins = instructions[i];

            if (PatchHelper.MatchOPCodes(instructions, i, pattern1))
            {
                if (instructions[i + 1].Operand is MsilOperandInline<FieldInfo> fieldOp
                    && fieldOp.Value == lastUpdateTimeField
                    && instructions[i + 5].Operand is MsilOperandInline<MethodBase> callOp
                    && callOp.Value == sessionStaticProperty.GetMethod
                    && instructions[i + 6].Operand is MsilOperandInline<MethodBase> callOp2
                    && callOp2.Value == frameCounterProperty.GetMethod
                    && instructions[i + 7].Operand is MsilOperandInline<FieldInfo> fieldOp2
                    && fieldOp2.Value == lastUpdateTimeField)
                {
                    yield return new MsilInstruction(OpCodes.Ldarg_0);
                    yield return new MsilInstruction(OpCodes.Ldflda).InlineValue(lastUpdateTimeField);
                    yield return new MsilInstruction(OpCodes.Ldloca_S).InlineValue(new MsilLocal(0));
                    yield return new MsilInstruction(OpCodes.Call).InlineValue(deltaTimeHelperMethod);
                    patchedParts++;
                    // Skip 8 instructions
                    i += 7;
                    continue;
                }
            }

            if (PatchHelper.MatchOPCodes(instructions, i, pattern2))
            {
                if (ins.Operand is MsilOperandInline<MethodBase> callOp
                    && callOp.Value == sessionStaticProperty.GetMethod
                    && instructions[i + 1].Operand is MsilOperandInline<MethodBase> callOp2
                    && callOp2.Value == frameCounterProperty.GetMethod)
                {
                    yield return new MsilInstruction(OpCodes.Ldloc_0);
                    patchedParts++;
                    // Skip 5 instructions
                    i += 4;
                    continue;
                }
            }

            yield return ins;
        }

        if (patchedParts != expectedParts)
            Plugin.Log.Fatal($"Failed to patch {nameof(MyBatteryBlock)}.UpdateAfterSimulation100. {patchedParts} out of {expectedParts} code parts matched.");
        else
            Plugin.Log.Debug("Patch successful.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void DeltaTimeHelper(ref int lastUpdateTime, out float frameCountDelta)
    {
        int time = MySession.Static.GameplayFrameCounter;
        frameCountDelta = (float)(time - lastUpdateTime);

        if (frameCountDelta > 200)
            frameCountDelta = 100;

        lastUpdateTime = time;
    }
}
