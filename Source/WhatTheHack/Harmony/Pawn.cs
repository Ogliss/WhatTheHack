﻿using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using WhatTheHack.Buildings;
using WhatTheHack.Duties;
using WhatTheHack.Needs;
using WhatTheHack.Storage;

namespace WhatTheHack.Harmony
{

    [HarmonyPatch(typeof(Pawn), "Kill")]
    static class Pawn_Kill
    {
        static void Prefix(Pawn __instance)
        {

            if (__instance.RaceProps.IsMechanoid)
            {
                if (__instance.relations == null)
                {
                    __instance.relations = new Pawn_RelationsTracker(__instance);
                }
                __instance.RemoveRemoteControlLink();
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), "DropAndForbidEverything")]
    class Pawn_DropAndForbidEverything
    {
        static bool Prefix(Pawn __instance)
        {
            if (__instance.RaceProps.IsMechanoid && !__instance.Dead)
            {
                return false;
            }
            return true;
        }
    }


    [HarmonyPatch(typeof(Pawn), "CurrentlyUsableForBills")]
    static class Pawn_CurrentlyUsableForBills
    {
        static void Postfix(Pawn __instance, ref bool __result)
        {
            Bill bill = __instance.health.surgeryBills.FirstShouldDoNow;
            if (!__instance.RaceProps.IsMechanoid)
            {
                return;
            }

            if(bill != null && bill.recipe.HasModExtension<DefModExtension_Recipe>() && __instance.InteractionCell.IsValid)
            {
                if (bill.recipe.GetModExtension<DefModExtension_Recipe>().requireBed == false || __instance.OnHackingTable())
                {
                    __result = true;
                }
                else
                {
                    __result = false;
                }
            }
        }
    }

    
    [HarmonyPatch(typeof(Pawn), "get_IsColonistPlayerControlled")]
    public class Pawn_get_IsColonistPlayerControlled
    {
        public static bool Prefix(Pawn __instance, ref bool __result)
        {
            if (__instance.HasReplacedAI() || (__instance.RaceProps.IsMechanoid &&
                __instance.RemoteControlLink() != null &&
                !__instance.RemoteControlLink().Drafted &&
                (float)__instance.pather.Destination.Cell.DistanceToSquared(__instance.RemoteControlLink().Position) <= 30f * 30f))
            {
                __result = true;
                return false;
            }
            return true;
        }
    }

    /*
    [HarmonyPatch]
    public static class Pawn_GetGizmos_Transpiler {
        static MethodBase TargetMethod()
        {
            var predicateClass = typeof(Pawn).GetNestedTypes(AccessTools.all)
                .FirstOrDefault(t => t.FullName.Contains("c__Iterator2"));
            return predicateClass.GetMethods(AccessTools.all).FirstOrDefault(m => m.Name.Contains("MoveNext"));
        }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var instructionsList = new List<CodeInstruction>(instructions);
            for (var i = 0; i < instructionsList.Count - 1; i++)
            {
                CodeInstruction instruction = instructionsList[i];

                if (instructionsList[i].operand == typeof(Pawn).GetMethod("get_IsColonistPlayerControlled"))
                {
                    //yield return new CodeInstruction(OpCodes.Call, typeof(Pawn).GetMethod("CanTakeOrder"));//Injected code     
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Extensions), "CanTakeOrder"));//Injected code     

                }
                else
                {
                    yield return instruction;
                }
            }
        }
    }
    */


    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    public class Pawn_GetGizmos
    {
        public static void Postfix(ref IEnumerable<Gizmo> __result, Pawn __instance)
        {
            List<Gizmo> gizmoList = __result.ToList();
            ExtendedDataStorage store = Base.Instance.GetExtendedDataStorage();
            bool isCreatureMine = __instance.Faction != null && __instance.Faction.IsPlayer;

            if (store == null || !isCreatureMine)
            {
                return;
            }
            if (__instance.IsHacked())
            {
                AddHackedPawnGizmos(__instance, ref gizmoList, store);
            }

            __result = gizmoList;
        }

        private static void AddHackedPawnGizmos(Pawn __instance, ref List<Gizmo> gizmoList, ExtendedDataStorage store)
        {
            ExtendedPawnData pawnData = store.GetExtendedDataFor(__instance);
            gizmoList.Add(CreateGizmo_SearchAndDestroy(__instance, pawnData));
            gizmoList.Add(CreateGizmo_AutoRecharge(__instance, pawnData));
            HediffSet hediffSet = __instance.health.hediffSet;
            if (hediffSet.HasHediff(WTH_DefOf.WTH_RepairModule))
            {
                gizmoList.Add(CreateGizmo_SelfRepair(__instance, pawnData));
            }
            if(hediffSet.HasHediff(WTH_DefOf.WTH_RepairModule) && hediffSet.HasHediff(WTH_DefOf.WTH_RepairArm))
            {
                gizmoList.Add(CreateGizmo_Repair(__instance, pawnData));
            }

        }

        private static Gizmo CreateGizmo_SearchAndDestroy(Pawn __instance, ExtendedPawnData pawnData)
        {
            string disabledReason = "";
            bool disabled = false;
            if (__instance.Downed)
            {
                disabled = true;
                disabledReason = "WTH_Reason_MechanoidDowned".Translate();
            }
            else if (pawnData.shouldAutoRecharge)
            {
                Need_Power powerNeed = __instance.needs.TryGetNeed(WTH_DefOf.WTH_Mechanoid_Power) as Need_Power;
                if (powerNeed != null && powerNeed.CurCategory >= PowerCategory.LowPower)
                {
                    disabled = true;
                    disabledReason = "WTH_Reason_PowerLow".Translate();
                }
            }
            Gizmo gizmo = new Command_Toggle
            {
                defaultLabel = "WTH_Gizmo_SearchAndDestroy_Label".Translate(),
                defaultDesc = "WTH_Gizmo_SearchAndDestroy_Description".Translate(),
                disabled = disabled,
                disabledReason = disabledReason,
                icon = ContentFinder<Texture2D>.Get(("UI/" + "Enable_SD"), true),
                isActive = () => pawnData.isActive,
                toggleAction = () =>
                {
                    pawnData.isActive = !pawnData.isActive;
                    if (pawnData.isActive)
                    {
                        if (__instance.GetLord() == null || __instance.GetLord().LordJob == null)
                        {
                            LordMaker.MakeNewLord(Faction.OfPlayer, new LordJob_SearchAndDestroy(), __instance.Map, new List<Pawn> { __instance });
                        }
                        __instance.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        if (__instance.relations == null) //Added here to fix existing saves. 
                        {
                            __instance.relations = new Pawn_RelationsTracker(__instance);
                        }
                    }
                    else
                    {
                        __instance.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        Building_BaseMechanoidPlatform closestAvailablePlatform = Utilities.GetAvailableMechanoidPlatform(__instance, __instance);
                        if (closestAvailablePlatform != null)
                        {
                            Job job = new Job(WTH_DefOf.WTH_Mechanoid_Rest, closestAvailablePlatform);
                            __instance.jobs.TryTakeOrderedJob(job);
                        }
                    }
                }
            };
            return gizmo;
        }
        private static Gizmo CreateGizmo_AutoRecharge(Pawn __instance, ExtendedPawnData pawnData)
        {
            Gizmo gizmo = new Command_Toggle
            {
                defaultLabel = "WTH_Gizmo_AutoRecharge_Label".Translate(),
                defaultDesc = "WTH_Gizmo_AutoRecharge_Description".Translate(),
                icon = ContentFinder<Texture2D>.Get(("UI/" + "AutoRecharge"), true),
                isActive = () => pawnData.shouldAutoRecharge,
                toggleAction = () =>
                {
                    pawnData.shouldAutoRecharge = !pawnData.shouldAutoRecharge;
                }
            };
            return gizmo;
        }

        private static Gizmo CreateGizmo_SelfRepair(Pawn __instance, ExtendedPawnData pawnData)
        {
            CompRefuelable compRefuelable = __instance.GetComp<CompRefuelable>();
            Need_Power powerNeed = __instance.needs.TryGetNeed<Need_Power>();

            if(compRefuelable == null)
            {
                Log.Message("compRefuelable is null");
            }
            if(powerNeed == null)
            {
                Log.Message("powerNeed is null");
            }
            float powerDrain = 50f;
            float fuelConsumption = 5f;
            bool alreadyRepairing = __instance.health.hediffSet.HasHediff(WTH_DefOf.WTH_Repairing);
            bool needsMorePower = powerNeed.CurLevel < powerDrain;
            bool needsMoreFuel = compRefuelable.Fuel < fuelConsumption;
            bool notActicated = !__instance.IsActivated();

            bool isDisabled = alreadyRepairing || needsMorePower || needsMoreFuel || notActicated;
            string disabledReason = "";
            if (isDisabled)
            {
                if (alreadyRepairing)
                {
                    disabledReason = "WTH_Reason_AlreadyRepairing".Translate();
                }
                else if (needsMorePower)
                {
                    disabledReason = "WTH_Reason_NeedsMorePower".Translate(new object[] {powerDrain});
                }
                else if (needsMoreFuel)
                {
                    disabledReason = "WTH_Reason_NeedsMoreFuel".Translate(new object[] { fuelConsumption });
                }
                else if (notActicated)
                {
                    disabledReason = "WTH_Reason_NotActivated".Translate();
                }
            }

            Gizmo gizmo = new Command_Action
            {
                defaultLabel = "WTH_Gizmo_SelfRepair_Label".Translate(),
                defaultDesc = "WTH_Gizmo_SelfRepair_Description".Translate(),
                icon = ContentFinder<Texture2D>.Get(("Things/" + "Mote_HealingCrossGreen"), true),
                disabled = isDisabled,
                disabledReason = disabledReason,
                action = delegate {
                    compRefuelable.ConsumeFuel(fuelConsumption);
                    powerNeed.CurLevel -= powerDrain;
                    __instance.health.AddHediff(WTH_DefOf.WTH_Repairing);
                }
            };
            return gizmo;
        }

        private static Gizmo CreateGizmo_Repair(Pawn __instance, ExtendedPawnData pawnData)
        {
            CompRefuelable compRefuelable = __instance.GetComp<CompRefuelable>();
            Need_Power powerNeed = __instance.needs.TryGetNeed<Need_Power>();

            if (compRefuelable == null)
            {
                Log.Message("compRefuelable is null");
            }
            if (powerNeed == null)
            {
                Log.Message("powerNeed is null");
            }
            float powerDrain = 50f; //TODO store somewhere else
            float fuelConsumption = 5f;//TODO store somewhere else
            bool alreadyRepairing = __instance.health.hediffSet.HasHediff(WTH_DefOf.WTH_Repairing);
            bool needsMorePower = powerNeed.CurLevel < powerDrain;
            bool needsMoreFuel = compRefuelable.Fuel < fuelConsumption;
            bool notActicated = !__instance.IsActivated();

            bool isDisabled = needsMorePower || needsMoreFuel || notActicated;
            string disabledReason = "";
            if (isDisabled)
            {
                if (needsMorePower)
                {
                    disabledReason = "WTH_Reason_NeedsMorePower".Translate(new object[] { powerDrain });
                }
                else if (needsMoreFuel)
                {
                    disabledReason = "WTH_Reason_NeedsMoreFuel".Translate(new object[] { fuelConsumption });
                }
                else if (notActicated)
                {
                    disabledReason = "WTH_Reason_NotActivated".Translate();
                }
            }

            Gizmo gizmo = new Command_Target
            {
                defaultLabel = "WTH_Gizmo_Mech_Repair_Label".Translate(),
                defaultDesc = "WTH_Gizmo_Mech_Repair_Description".Translate(),
                icon = ContentFinder<Texture2D>.Get(("Things/" + "Mote_HealingCrossBlue"), true), //TODO: other icon
                disabled = isDisabled,
                targetingParams = GetTargetingParametersForRepairing(),
                disabledReason = disabledReason,
                action = delegate(Thing target) {
                    if(target is Pawn)
                    {
                        Pawn mech = (Pawn)target;
                        compRefuelable.ConsumeFuel(fuelConsumption);
                        powerNeed.CurLevel -= powerDrain;
                        mech.health.AddHediff(WTH_DefOf.WTH_Repairing);
                    }

                }
        };
            return gizmo;
        }
        private static TargetingParameters GetTargetingParametersForRepairing()
        {
            return new TargetingParameters
            {
                canTargetPawns = true,
                canTargetBuildings = false,
                mapObjectTargetsMustBeAutoAttackable = false,
                validator = delegate (TargetInfo targ)
                {
                    if (!targ.HasThing)
                    {
                        return false;
                    }
                    Pawn pawn = targ.Thing as Pawn;
                    return pawn != null && !pawn.Downed && pawn.IsHacked() && pawn.health != null && !pawn.health.hediffSet.HasHediff(WTH_DefOf.WTH_Repairing);
                }
            };
        }
    }
}
