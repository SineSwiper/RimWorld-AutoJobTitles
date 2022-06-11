using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

// Job title job code (job job job)
namespace AutoJobTitles {
    [DefOf]
    public static class ThinkTreeDefOf {
        public static ThinkTreeDef AJT_ChangeJobTitle;
    }

    [DefOf]
    public static class JobDefOf {
        public static JobDef AJT_ChangingJobTitle;
    }

    public class ThinkNode_ConditionalWantsTitleChange : ThinkNode_Conditional {
        protected override bool Satisfied (Pawn pawn) => pawn.IsFreeNonSlaveColonist && pawn.story != null && pawn.story.title.NullOrEmpty();
    }

    public class JobGiver_ChangeJobTitle : ThinkNode_JobGiver {
        protected override Job TryGiveJob (Pawn pawn) {
            if (pawn.story == null) return null;

            Job newJob = JobMaker.MakeJob(JobDefOf.AJT_ChangingJobTitle);
            newJob.expiryInterval = JobDriver_ChangeTitle.thinkingTotal * 2;
            return newJob;
        }
    }

    public class JobDriver_ChangeTitle : JobDriver {
        private float thinkingDone = 0f;

        internal const int thinkingTotal = 120;

        public override bool TryMakePreToilReservations(bool errorOnFailed) {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils() {
            Toil thinking = new Toil {
                initAction = delegate { thinkingDone = 0f; },
                tickAction = delegate {
                    thinkingDone += 1f;
                    if (thinkingDone > thinkingTotal) ReadyForNextToil();
                },
                defaultCompleteMode = ToilCompleteMode.Never
            };
            thinking.WithProgressBar(TargetIndex.A, () => thinkingDone / thinkingTotal);
            yield return thinking;

            yield return Toils_General.Do(delegate {
                // Do the name change thing
                string newTitle = Base.Instance.NewJobTitle(pawn);
                if (newTitle == "") return;
                pawn.story.title = newTitle;

                FleckMaker.ThrowMetaIcon( pawn.Position, pawn.Map, FleckDefOf.IncapIcon );

                // Announce the change
                Messages.Message(
                    text:        "AJT_PawnGainsTitle".Translate( pawn.Name.ToStringShort, pawn.story.Title, pawn.Named("PAWN") ).AdjustedFor(pawn),
                    lookTargets: pawn,
                    def:         MessageTypeDefOf.PositiveEvent,
                    historical:  false
                );
            });
        }
        
    }
}
