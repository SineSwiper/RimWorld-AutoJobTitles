using HarmonyLib;
using System.Reflection;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;
using UnityEngine;

namespace AutoJobTitles {
    [StaticConstructorOnStartup]
    [HarmonyPatch]
    internal class HarmonyPatches {
        /* Add Randomize button to NamePawn dialog box. Fortunately, the new v1.4 dialog has its other Randomize button
         * vertically aligned with what we want here and the pieces are broken up more. So, this is now a small change,
         * not a method override.
         */

        [HarmonyPatch("Verse.Dialog_NamePawn+NameContext", "MakeRow")]
        [HarmonyPostfix]
        private static void NameContext_MakeRow_Postfix(
            object __instance,
            Pawn pawn,
            float randomizeButtonWidth,
            TaggedString randomizeText,
            RectDivider divider,
            string ___textboxName,
            ref string ___current
        ) {
            if (___textboxName != "Title") return;

            // similar to the latter half of the original MakeRow
            Rect rect = divider.NewCol(randomizeButtonWidth);
            if (!Widgets.ButtonText(rect, randomizeText)) return;

            // if clicked...
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            ___current = Base.Instance.NewJobTitle(pawn);
        }
    }
}