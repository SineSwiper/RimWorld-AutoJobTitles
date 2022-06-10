using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using HugsLib;
using HugsLib.Settings;
using Verse;
using Verse.AI;
using Verse.Grammar;
using UnityEngine;

namespace AutoJobTitles {
    [StaticConstructorOnStartup]
    public class Base : ModBase {
        public override string ModIdentifier {
            get { return "AutoJobTitles"; }
        }
        public static Base Instance { get; private set; }
        public static bool IsDebug  { get; private set; }
        
        public static RulePackDef TitleNamerDef { get; private set; }
        public static RulePackDef PartsNamerDef { get; private set; }

        internal HugsLib.Utils.ModLogger ModLogger { get; private set; }

        public Base() {
            Instance    = this;
            ModLogger   = Logger;
            IsDebug     = false;
        }

        internal Dictionary<string, SettingHandle> config = new Dictionary<string, SettingHandle>();

        // Please load Harmony stuff
        // (This is required for HugsLib.ModBase.ApplyHarmonyPatches)
        public override void DefsLoaded() {
            ProcessSettings();

            // Cache these for later use
            TitleNamerDef = DefDatabase<RulePackDef>.GetNamed("AJT_Namer_JobTitle");
            PartsNamerDef = DefDatabase<RulePackDef>.GetNamed("AJT_Namer_JobTitleParts");
        }

        public void ProcessSettings () {
            // Hidden config version entry
            Version currentVer    = Instance.GetVersion();
            string  currentVerStr = currentVer.ToString();

            config["ConfigVersion"] = Settings.GetHandle<string>("ConfigVersion", "", "", currentVerStr);
            var configVerSetting = (SettingHandle<string>)config["ConfigVersion"];
            configVerSetting.DisplayOrder = 0;
            configVerSetting.NeverVisible = true;

            string  configVerStr = configVerSetting.Value;
            Version configVer    = new Version(configVerStr);
            
            var settingNames = new List<string> {
                "FactorWorkSettings",
                "MinDoublePassionSkillLevel",
                "MinSinglePassionSkillLevel",
                "MinNoPassionSkillLevel",
                "MinExpertSkillLevel",
            };
            var iDefaults = new Dictionary<string, int> {
                { "MinDoublePassionSkillLevel", 5  },
                { "MinSinglePassionSkillLevel", 6  },
                { "MinNoPassionSkillLevel",     7  },
                { "MinExpertSkillLevel",        12 },
            };
            
            int order = 1;
            foreach (string sName in settingNames) {
                bool isHeader = sName.Contains("Header");

                if (sName == "BlankHeader") {
                    // No translations here
                    config[sName] = Settings.GetHandle<bool>(sName + order, "", "", false);
                }
                else if (sName.Contains("SkillLevel")) {
                    config[sName] = Settings.GetHandle<int>(
                        settingName:  sName,
                        title:        ("AJT_" + sName + "_Title").Translate(),
                        description:  ("AJT_" + sName + "_Description").Translate(),
                        defaultValue: iDefaults[sName],
                        validator:    Validators.IntRangeValidator(0, 20)
                    );
                    config[sName].CustomDrawer = rect => {
                        return DrawUtility.CustomDrawer_Slider(
                            rect, (SettingHandle<int>)config[sName], false, 0, 20, 1
                        );
                    };
                }
                else {
                    config[sName] = Settings.GetHandle<bool>(
                        settingName:  sName,
                        title:        string.Concat(
                            isHeader ? "<size=15><b>" : "",
                            ("AJT_" + sName + "_Title").Translate(),
                            isHeader ? "</b></size>" : ""
                        ),
                        description:  ("AJT_" + sName + "_Description").Translate(),
                        defaultValue: !isHeader
                    );
                }

                var setting = config[sName];
                setting.DisplayOrder = order;

                if (isHeader) {
                    // No real settings here; just for display
                    setting.Unsaved = true;
                    setting.CustomDrawer = rect => { return false; };
                }

                order++;
            }

            // Set the new config value to the current version
            configVer                             = currentVer;
            configVerStr = configVerSetting.Value = currentVerStr;
        }

        internal Dictionary<SkillDef, string> skillToShortName = new Dictionary<SkillDef, string> {
            { SkillDefOf.Shooting,     "shoot_" },
            { SkillDefOf.Melee,        "melee_" },
            { SkillDefOf.Construction, "constr" },
            { SkillDefOf.Mining,       "miner_" },
            { SkillDefOf.Cooking,      "cook__" },
            { SkillDefOf.Plants,       "plants" },
            { SkillDefOf.Animals,      "animal" },
            { SkillDefOf.Crafting,     "crafts" },
            { SkillDefOf.Artistic,     "artist" },
            { SkillDefOf.Medicine,     "medic_" },
            { SkillDefOf.Social,       "social" },
            { SkillDefOf.Intellectual, "intell" },
        };

        public string NewJobTitle (Pawn pawn) {
            // Cache WorkGivers for this pawn
            var workSettings = pawn.workSettings;
            var workGivers   = workSettings.WorkGiversInOrderNormal;
            int topPriority  = workGivers.Select( wg => workSettings.GetPriority(wg.def.workType) ).Where( p => p > 0 ).Min();
            
            bool factorWorkSettings = ((SettingHandle<bool>)config["FactorWorkSettings"]).Value;

            HashSet<SkillRecord> workBasedSkills =
                workGivers.
                Select    ( wg  => wg.def.workType ).
                Where     ( wtd => !factorWorkSettings || (workSettings.GetPriority(wtd) is int p && p > 0 && p <= topPriority) ).
                SelectMany( wtd => wtd.relevantSkills ).Distinct().
                Select    ( sd  => pawn.skills.GetSkill(sd) ).
                Where     ( sr  => sr != null && skillToShortName.ContainsKey(sr.def) ).
                ToHashSet()
            ;

            int minDPLevel     = ((SettingHandle<int>)config["MinDoublePassionSkillLevel"]).Value;
            int minSPLevel     = ((SettingHandle<int>)config["MinSinglePassionSkillLevel"]).Value;
            int minNPLevel     = ((SettingHandle<int>)config["MinNoPassionSkillLevel"    ]).Value;
            int minExpertLevel = ((SettingHandle<int>)config["MinExpertSkillLevel"       ]).Value;

            // Shooting and Melee are skills for combat, not basic work (usually), so we'll just add these in,
            // and let the level sorting figure it out.  They still need a passion or be good at it, though.
            SkillRecord shooting = pawn.skills.GetSkill(SkillDefOf.Shooting);
            SkillRecord melee    = pawn.skills.GetSkill(SkillDefOf.Melee);
            if (shooting.passion != Passion.None || shooting.Level >= minExpertLevel) workBasedSkills.Add(shooting);
            if (melee   .passion != Passion.None || melee   .Level >= minExpertLevel) workBasedSkills.Add(melee);

            // Prep up parts of the request to add in pawn rules/constants
            GrammarRequest requestTemplate = new GrammarRequest();
            requestTemplate.Rules.AddRange( GrammarUtility.RulesForPawn("PAWN", pawn, requestTemplate.Constants) );

            // Figure out the skill list
            int count     = 0; // use this so often that I prefer the smaller varname over skillList.Count
            var skillList = new List<string> {};
            var isExpert  = new Dictionary<string, bool> {};
            foreach ( SkillRecord skill in workBasedSkills.OrderByDescending( sr => sr.Level ) ) {
                // Figure out if it's a skill worth looking at
                if (skill.Level < minDPLevel) continue;
                if (skill.Level < minSPLevel && skill.passion != Passion.Major) continue;
                if (skill.Level < minNPLevel && skill.passion == Passion.None)  continue;

                string shortName = skillToShortName[skill.def];
                bool   expert    = skill.Level >= minExpertLevel;

                // If we're going for more than 3, they better be an expert or double passion for it
                if (count > 3 && !(expert || skill.passion == Passion.Major)) continue;
                
                skillList.Add(shortName);
                isExpert[shortName] = expert;
                count++;

                // Hard cap at 5... the permutation loop grows exponentially
                if (count >= 5) break;
            }

            // Special case for noskill
            if (count <= 0) {
                GrammarRequest request = new GrammarRequest();
                request.Includes .Add     ( TitleNamerDef );
                request.Rules    .AddRange( requestTemplate.Rules );
                request.Constants.AddRange( requestTemplate.Constants );
                request.Constants.Add     ( "count", count.ToString() );

                return GrammarResolver.Resolve(
                    request:     request,
                    rootKeyword: "r_title",
                    capitalizeFirstSentence: true
                );
            }

            /* The RulesPackDef is capable of a lot of different things, but it cannot set constants as words
             * are being picked.  Because different adjective/noun permutations are dependent on which skills
             * were picked, we need something more robust.  For example, if a Medical noun is picked, we don't
             * want a Medical adjective as well.
             * 
             * So, we use this special permutation loop to figure out a bunch of well-formed titles, and then
             * randomly pick from the list.
             */

            var titles = new Dictionary<string, float> {};
            foreach ( IEnumerable<string> skillPermutation in skillList.DifferentPermutations(count) ) {
                List<string> permSkillList = skillPermutation.ToList();

                // Not only are there different permutations, but there are different points where we can split
                // between adjectives and nouns.
                for (int i = 0; i < count; i++) {  // stops just short of the last record, because all adjective doesn't make sense
                    List<string>  adjSkills = permSkillList.GetRange(0, i);  // can be nothing
                    List<string> nounSkills = permSkillList.GetRange(i, count - i);

                    Rule_String  adjRule = ResolveJobTitlePart( "adj", requestTemplate,  adjSkills, isExpert);
                    Rule_String nounRule = ResolveJobTitlePart("noun", requestTemplate, nounSkills, isExpert);
                    if (nounRule == null) continue;

                    GrammarRequest request = new GrammarRequest();
                    request.Includes .Add     ( TitleNamerDef );
                    request.Rules    .AddRange( requestTemplate.Rules );
                    request.Constants.AddRange( requestTemplate.Constants );
                    request.Constants.Add     ( "count", count.ToString() );

                    if (adjRule != null) request.Rules.Add(adjRule);
                    request.Rules.Add(nounRule);

                    string title = GrammarResolver.Resolve(
                        debugLabel: "TITLE",
                        forceLog:   true,

                        request:     request,
                        rootKeyword: "r_title",
                        capitalizeFirstSentence: true
                    );

                    // Give more weight to higher skill and more complicated titles
                    // XXX: We don't actually know if a skill#_noun gave us a #-Skill noun
                    double weight = 1 + Math.Pow(adjSkills.Count, 1.5) + Math.Pow(nounSkills.Count, 1.75) + (title.Length / 20f);

                    titles.Add( title, (float)weight );
                }
            }

            // Take out the current job title, since the user probably hit the Randomize button
            if (pawn.story != null) titles.Remove(pawn.story.TitleCap);

            return titles.Keys.RandomElementByWeightWithFallback(
                t => titles[t],
                pawn.story?.TitleCap  // zero results?
            );
        }

        public Rule_String ResolveJobTitlePart (string partType, GrammarRequest requestTemplate, List<string> partSkillList, Dictionary<string, bool> expertList) {
            int count = partSkillList.Count;
            
            if (count <= 0 || count > 3) return null;  // no parts beyond 3 yet

            GrammarRequest request = new GrammarRequest();
            request.Includes .Add     ( PartsNamerDef );
            request.Rules    .AddRange( requestTemplate.Rules );
            request.Constants.AddRange( requestTemplate.Constants );
            request.Constants.AddRange( partSkillList.ToDictionary( keySelector: s => s,             elementSelector: s => "1" ) );
            request.Constants.AddRange( partSkillList.ToDictionary( keySelector: s => "expert_" + s, elementSelector: s => expertList[s] ? "1" : "0" ) );
                    
            string keyword = string.Format("skill{0}_{1}", count, partType);
            string output  = GrammarResolver.Resolve(
                request:     request,
                rootKeyword: keyword,
                capitalizeFirstSentence: false
            );

            return new Rule_String(keyword, output);
        }
    }

    public static class SetHelpers {
        public static IEnumerable<IEnumerable<T>> DifferentPermutations<T>(this IEnumerable<T> elements, int len) {
            return len == 1 ?
                elements.Select( t => new T[] { t } ) :
                elements.
                DifferentPermutations(len - 1).
                SelectMany(
                    collectionSelector: t        => elements.Where( e => !t.Contains(e) ),
                    resultSelector:     (t1, t2) => t1.Concat(new T[] { t2 })
                )
            ;
        }

        public static IEnumerable<IEnumerable<T>> DifferentCombinations<T>(this IEnumerable<T> elements, int len) {
            return len == 0 ?
                new[] { new T[0] } :
                elements.SelectMany( (e, i) => 
                    elements.
                    Skip  ( i + 1 ).
                    DifferentCombinations(len - 1).
                    Select( c => (new[] {e}).Concat(c) )
                )
            ;
        }
    }

    public static class DrawUtility {
        public static bool CustomDrawer_Slider(Rect rect, SettingHandle<int> slider, bool def_isPercentage, float def_min, float def_max, float roundTo = -1) {
            int labelWidth = 50;

            Rect sliderPortion = new Rect(rect);
            sliderPortion.width -= labelWidth;

            Rect labelPortion = new Rect(rect) {
                width    = labelWidth,
                position = new Vector2(sliderPortion.position.x + sliderPortion.width + 5f, sliderPortion.position.y + 4f)
            };

            sliderPortion = sliderPortion.ContractedBy(2f);

            if (def_isPercentage)
                Widgets.Label(labelPortion, (Mathf.Round(slider.Value * 100f)).ToString("F0") + "%");
            else if (roundTo >= 1)
                Widgets.Label(labelPortion, slider.Value.ToString("N0"));
            else 
                Widgets.Label(labelPortion, slider.Value.ToString("F2"));

            int val = (int)Widgets.HorizontalSlider(
                rect:       sliderPortion,
                value:      slider.Value,
                leftValue:  def_min,
                rightValue: def_max,
                middleAlignment: true,
                roundTo:    roundTo
            );

            bool change = slider.Value != val;
            slider.Value = val;
            return change;
        }        
    }

    // Job title job code (job job job)
    public class ThinkNode_ConditionalWantsTitleChange : ThinkNode_Conditional {
        protected override bool Satisfied (Pawn pawn) => pawn.IsFreeNonSlaveColonist && pawn.story != null && pawn.story.title.NullOrEmpty();
    }

    public class JobGiver_ChangeJobTitle : ThinkNode_JobGiver {
        protected override Job TryGiveJob (Pawn pawn) {
            if (pawn.story == null) return null;

            // Do the name change thing
            string newTitle = Base.Instance.NewJobTitle(pawn);
            if (newTitle == "") return null;
            pawn.story.title = newTitle;

            // Announce the change
            Messages.Message(
                // FIXME: Make our own translate text
                text:        "PawnGainsName".Translate( pawn.Name.ToStringShort, pawn.story.Title, pawn.Named("PAWN") ).AdjustedFor(pawn),
                lookTargets: pawn,
                def:         MessageTypeDefOf.PositiveEvent,
                historical:  false
            );

            Job newJob = JobMaker.MakeJob( DefDatabase<JobDef>.GetNamed("AJT_ChangingJobTitle") );
            newJob.expiryInterval = 30;  // half-a-second
            return newJob;
        }
    }

}
