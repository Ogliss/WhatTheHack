﻿using Harmony;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace WhatTheHack.Harmony
{
    [HarmonyPatch(typeof(Gizmo_CaravanInfo), "GizmoOnGUI")]
    class Gizmo_CaravanInfo_GizmoOnGUI
    {
        static void Postfix(Gizmo_CaravanInfo __instance)
        {
            int numMechanoids = 0;
            float fuelAmount = 0f;
            float fuelConsumption = 0f;
            int numPlatforms = 0;
            float daysOfFuel = 0;
            Caravan caravan = Traverse.Create(__instance).Field("caravan").GetValue<Caravan>();
            StringBuilder daysOfFuelReason = new StringBuilder();

            foreach (Thing thing in caravan.AllThings)
            {
                if (thing.def.race != null && thing.def.race.IsMechanoid)
                {
                    numMechanoids += thing.stackCount;
                }
                if (thing.def == ThingDefOf.Chemfuel)
                {
                    fuelAmount += thing.stackCount;
                }
                if (thing.def == ThingDefOf.MinifiedThing)
                {
                    if (thing.GetInnerIfMinified().def == WTH_DefOf.WTH_PortableChargingPlatform)
                    {
                        numPlatforms += thing.stackCount;
                    }

                }
            }
            Utilities.CalcDaysOfFuel(numMechanoids, fuelAmount, ref fuelConsumption, numPlatforms, ref daysOfFuel, daysOfFuelReason);
        }
    }
}