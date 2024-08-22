using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Havok;
using NLog;
using Sandbox.Engine.Physics;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Torch;
using Torch.Managers.PatchManager;
using Torch.Managers.PatchManager.MSIL;
using VRage.Game.Entity;

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

[PatchShim]
static class MyMotorSuspension_Patches
{
    public static void Patch(PatchContext ctx)
    {
        MethodInfo source;
        MethodInfo prefix;

        source = typeof(MyMotorSuspension).GetMethod("CubeGrid_OnPhysicsChanged", _public: false, _static: false);
        prefix = typeof(MyMotorSuspension_Patches).GetMethod(nameof(Prefix_CubeGrid_OnPhysicsChanged), _public: false, _static: true);
        ctx.GetPattern(source).Prefixes.Add(prefix);

        source = typeof(MyMotorSuspension).GetMethod("CubeGrid_OnHavokSystemIDChanged", _public: false, _static: false);
        prefix = typeof(MyMotorSuspension_Patches).GetMethod(nameof(Prefix_CubeGrid_OnHavokSystemIDChanged), _public: false, _static: true);
        ctx.GetPattern(source).Prefixes.Add(prefix);

        MethodInfo transpiler;

        source = typeof(MyMotorSuspension).GetMethod("CreateConstraint", _public: false, _static: false);
        transpiler = typeof(MyMotorSuspension_Patches).GetMethod(nameof(Transpile_CreateConstraint), _public: false, _static: true);
        ctx.GetPattern(source).Transpilers.Add(transpiler);

        source = typeof(MyMotorSuspension).GetMethod("OnRotorPhysicsChanged", _public: false, _static: false);
        transpiler = typeof(MyMotorSuspension_Patches).GetMethod(nameof(Transpile_OnRotorPhysicsChanged), _public: false, _static: true);
        ctx.GetPattern(source).Transpilers.Add(transpiler);

        source = typeof(MyMotorSuspension).GetMethod("DisposeConstraint", _public: false, _static: false);
        transpiler = typeof(MyMotorSuspension_Patches).GetMethod(nameof(Transpile_DisposeConstraint), _public: false, _static: true);
        ctx.GetPattern(source).Transpilers.Add(transpiler);
    }

    static bool Prefix_CubeGrid_OnPhysicsChanged(MyMotorSuspension __instance, MyEntity obj)
    {
        var baseGridPhysics = __instance.CubeGrid.Physics;

        if (baseGridPhysics != null)
            Prefix_CubeGrid_OnHavokSystemIDChanged(__instance, baseGridPhysics.HavokCollisionSystemID);

        return false;
    }

    static bool Prefix_CubeGrid_OnHavokSystemIDChanged(MyMotorSuspension __instance, int obj)
    {
        var topGridPhysics = __instance.TopGrid?.Physics;

        if (topGridPhysics == null)
            return false;

        var topGridBody = topGridPhysics.RigidBody;

        if (topGridBody == null)
            return false;

        uint collisionFilterInfo = HkGroupFilter.CalcFilterInfo(topGridBody.Layer, obj, 1, 1);

        topGridBody.SetCollisionFilterInfo(collisionFilterInfo);
        MyPhysics.RefreshCollisionFilter(topGridPhysics);

        return false;
    }

    static void TopGrid_OnHavokSystemIDChanged(MyMotorSuspension __instance)
    {
        var baseGridPhysics = __instance.CubeGrid.Physics;

        if (baseGridPhysics != null)
            Prefix_CubeGrid_OnHavokSystemIDChanged(__instance, baseGridPhysics.HavokCollisionSystemID);
    }

    static readonly ConcurrentDictionary<MyMotorSuspension, Action<int>> registeredDelegates = [];

    static void RegisterTopGrid(MyMotorSuspension instance)
    {
        Action<int> action = systemId => TopGrid_OnHavokSystemIDChanged(instance);

        registeredDelegates.TryAdd(instance, action);

        instance.TopGrid.OnHavokSystemIDChanged += action;
    }

    static void DeregisterTopGrid(MyMotorSuspension instance, MyCubeGrid topGrid)
    {
        if (registeredDelegates.TryRemove(instance, out var action))
            topGrid.OnHavokSystemIDChanged -= action;
        else
            Plugin.Log.Error($"Failed to unregister event for suspension block. ID: {instance.EntityId}, Name: {instance.CustomName}, on grid, ID: {instance.CubeGrid?.EntityId}, Name: {instance.CubeGrid?.DisplayName}");
    }

    static void DeregisterTopGrid2(MyMotorSuspension instance, MyEntity rotorGrid)
    {
        if (rotorGrid is MyCubeGrid topGrid)
            DeregisterTopGrid(instance, topGrid);
    }

    static IEnumerable<MsilInstruction> Transpile_CreateConstraint(IEnumerable<MsilInstruction> instructionStream)
    {
        Plugin.Log.Debug($"Patching {nameof(MyMotorSuspension)}.CreateConstraint.");

        const int expectedParts = 1;
        int patchedParts = 0;

        var addOnPhysicsChangedMethod = typeof(MyEntity).GetMethod("add_OnPhysicsChanged", _public: true, _static: false);
        var registerTopGridMethod = typeof(MyMotorSuspension_Patches).GetMethod(nameof(RegisterTopGrid), _public: false, _static: true);

        var instructions = instructionStream.ToArray();

        for (int i = 0; i < instructions.Length; i++)
        {
            var ins = instructions[i];

            yield return ins;

            if (ins.OpCode == OpCodes.Callvirt && ins.Operand is MsilOperandInline<MethodBase> callOperand)
            {
                var callMethod = callOperand.Value;

                if (callMethod == addOnPhysicsChangedMethod)
                {
                    yield return new MsilInstruction(OpCodes.Ldarg_0);
                    yield return new MsilInstruction(OpCodes.Call).InlineValue(registerTopGridMethod);
                    patchedParts++;
                }
            }
        }

        if (patchedParts != expectedParts)
            Plugin.Log.Fatal($"Failed to patch {nameof(MyMotorSuspension)}.CreateConstraint. {patchedParts} out of {expectedParts} code parts matched.");
        else
            Plugin.Log.Debug("Patch successful.");
    }

    static IEnumerable<MsilInstruction> Transpile_OnRotorPhysicsChanged(IEnumerable<MsilInstruction> instructionStream)
    {
        Plugin.Log.Debug($"Patching {nameof(MyMotorSuspension)}.OnRotorPhysicsChanged.");

        const int expectedParts = 1;
        int patchedParts = 0;

        var removeOnPhysicsChangedMethod = typeof(MyEntity).GetMethod("remove_OnPhysicsChanged", _public: true, _static: false);
        var deregisterTopGrid2Method = typeof(MyMotorSuspension_Patches).GetMethod(nameof(DeregisterTopGrid2), _public: false, _static: true);

        var instructions = instructionStream.ToArray();

        for (int i = 0; i < instructions.Length; i++)
        {
            var ins = instructions[i];

            yield return ins;

            if (ins.OpCode == OpCodes.Callvirt && ins.Operand is MsilOperandInline<MethodBase> callOperand)
            {
                var callMethod = callOperand.Value;

                if (callMethod == removeOnPhysicsChangedMethod)
                {
                    yield return new MsilInstruction(OpCodes.Ldarg_0);
                    yield return new MsilInstruction(OpCodes.Ldarg_1);
                    yield return new MsilInstruction(OpCodes.Call).InlineValue(deregisterTopGrid2Method);
                    patchedParts++;
                }
            }
        }

        if (patchedParts != expectedParts)
            Plugin.Log.Fatal($"Failed to patch {nameof(MyMotorSuspension)}.OnRotorPhysicsChanged. {patchedParts} out of {expectedParts} code parts matched.");
        else
            Plugin.Log.Debug("Patch successful.");
    }

    static IEnumerable<MsilInstruction> Transpile_DisposeConstraint(IEnumerable<MsilInstruction> instructionStream)
    {
        Plugin.Log.Debug($"Patching {nameof(MyMotorSuspension)}.DisposeConstraint.");

        const int expectedParts = 1;
        int patchedParts = 0;

        var removeOnPhysicsChangedMethod = typeof(MyEntity).GetMethod("remove_OnPhysicsChanged", _public: true, _static: false);
        var deregisterTopGridMethod = typeof(MyMotorSuspension_Patches).GetMethod(nameof(DeregisterTopGrid), _public: false, _static: true);

        var instructions = instructionStream.ToArray();

        for (int i = 0; i < instructions.Length; i++)
        {
            var ins = instructions[i];

            yield return ins;

            if (ins.OpCode == OpCodes.Callvirt && ins.Operand is MsilOperandInline<MethodBase> callOperand)
            {
                var callMethod = callOperand.Value;

                if (callMethod == removeOnPhysicsChangedMethod)
                {
                    yield return new MsilInstruction(OpCodes.Ldarg_0);
                    yield return new MsilInstruction(OpCodes.Ldarg_1);
                    yield return new MsilInstruction(OpCodes.Call).InlineValue(deregisterTopGridMethod);
                    patchedParts++;
                }
            }
        }

        if (patchedParts != expectedParts)
            Plugin.Log.Fatal($"Failed to patch {nameof(MyMotorSuspension)}.DisposeConstraint. {patchedParts} out of {expectedParts} code parts matched.");
        else
            Plugin.Log.Debug("Patch successful.");
    }
}
