using HarmonyLib;
using System.Reflection;
using System.Collections.Generic;
using RimWorld;
using Verse;
using UnityEngine;

namespace AutoJobTitles {
    [StaticConstructorOnStartup]
    [HarmonyPatch]
    internal class HarmonyPatches {
        /* Expand NamePawn dialog box to be larger and include the Randomize button
         * 
         * Sadly, I can't just transpile what I want here, so this is a complete override.  As it usually turns out,
         * I like expansive changes better than the original transpiler idea, anyway.
         */

        [HarmonyPatch(typeof(Dialog_NamePawn), nameof(Dialog_NamePawn.DoWindowContents))]
        // This may be an complete override, but if anybody wants to add another prefix, it will default to run before this.
        [HarmonyPriority(Priority.Last)]
        [HarmonyPrefix]
        private static bool DoWindowContents_Override(Dialog_NamePawn __instance, Rect inRect, Pawn ___pawn, ref string ___curName, ref string ___curTitle) {
            // NOTE: This is modeled more from Dialog_GiveName than Dialog_NamePawn, because I like the increased size

            bool flag = false;
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return) {
                flag = true;
                Event.current.Use();
            }

            // [Reflection prep] this.CurPawnName (getter)
            MethodInfo CurPawnNameGetter = AccessTools.DeclaredPropertyGetter(typeof(Dialog_NamePawn), "CurPawnName");

            // Size constants
            float lineHeight  = 35;
            float buttonWidth = inRect.width / 2f - 100;
            float fieldWidth  = inRect.width / 2f + 10;
            
            // Full name string
            Text.Font = GameFont.Medium;
            Name curPawnName = (Name)CurPawnNameGetter.Invoke(__instance, new object[] {});
            string label = curPawnName.ToString().Replace(" '' ", " ");
            if      (___curTitle == ""  ) label = label + ", " + ___pawn.story.TitleDefaultCap;
            else if (___curTitle != null) label = label + ", " + ___curTitle.CapitalizeFirst();

            float y = -5; // XXX: yeah, this is weird, but we do it, anyway to squeeze all of the vertical space...
            Widgets.Label( new Rect(0, y, inRect.width, inRect.height), label);

            // Name input field / randomize button
            Text.Font = GameFont.Small;

            y += Text.CalcHeight(label, inRect.width) + 10;
            if (
                Widgets.ButtonText( new Rect(fieldWidth + 10, y, buttonWidth, lineHeight), "Randomize".Translate() )
            ) {
                if (___pawn.RaceProps != null && ___pawn.RaceProps.Animal) {
                    ___curName = PawnBioAndNameGenerator.GeneratePawnName(___pawn).ToStringShort;
                }
                else {
                    NameTriple genTriple = (NameTriple) PawnBioAndNameGenerator.GeneratePawnName( ___pawn, NameStyle.Full, ((NameTriple)curPawnName).Last );
                    ___curName = Rand.Bool ? genTriple.First : genTriple.Nick;
                }
            }
            string newName = Widgets.TextField( new Rect(0, y, fieldWidth, lineHeight), ___curName);
            if (newName.Length < 16 && CharacterCardUtility.ValidNameRegex.IsMatch(newName)) ___curName = newName;

            // Job title field / randomize button
            if (___curTitle != null) {
                y += lineHeight + 5;
                if (
                    Widgets.ButtonText( new Rect(fieldWidth + 10, y, buttonWidth, lineHeight), "Randomize".Translate() )
                ) ___curTitle = Base.Instance.NewJobTitle(___pawn);
                string newTitle = Widgets.TextField(new Rect(0, y, fieldWidth, lineHeight), ___curTitle);
                if (newTitle.Length < 30 && CharacterCardUtility.ValidNameRegex.IsMatch(newTitle)) ___curTitle = newTitle;
            }

            // OK button
            buttonWidth /= 2;
            Rect rect1 = new Rect( inRect.width - buttonWidth, inRect.height - lineHeight, buttonWidth, lineHeight );

            if ( !(Widgets.ButtonText(rect1, "OK".Translate()) | flag) ) return false;

            // Validate input
            if (string.IsNullOrEmpty(___curName)) ___curName = ((NameTriple) ___pawn.Name).First;
            curPawnName  = (Name)CurPawnNameGetter.Invoke(__instance, new object[] {});
            ___pawn.Name = curPawnName;
            if (___pawn.story != null) ___pawn.story.Title = ___curTitle;

            Find.WindowStack.TryRemove(__instance);
            Messages.Message(
                ___pawn.def.race.Animal ? "AnimalGainsName".Translate(___curName) : "PawnGainsName".Translate(___curName, ___pawn.story.Title, ___pawn.Named("PAWN")).AdjustedFor(___pawn),
                ___pawn, MessageTypeDefOf.PositiveEvent, false
            );

            // Always skip the original method
            return false;
        }
    }
}