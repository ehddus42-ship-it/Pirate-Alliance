using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AcRoguelike.Liminal;
using UnityEditor;
using UnityEngine;

namespace AcRoguelike.Liminal.EditorTests
{
    /// <summary>
    /// Edit Mode checks for the augment rules (no scene needed): catalog integrity, the common/character pool, offer
    /// rolls, conflicting picks, stack limits, 효율적인 검술's combo cap, 예금's payouts and the melee hook maths.
    /// Play Mode coverage (a cleared combat room pauses and offers cards) is in LiminalPlayValidation.
    /// </summary>
    public static class AugmentValidation
    {
        const string ReportPath = "Documentation/Augments/augment-validation.json";

        [Serializable] sealed class Report { public bool passed; public List<string> checks = new List<string>(); public string error; }

        [MenuItem("AC Roguelike/Liminal/Validate Augments")]
        public static void RunMenu()
        {
            var report = Run();
            if (report.passed) Debug.Log("Augment validation passed:\n" + string.Join("\n", report.checks));
            else Debug.LogError("Augment validation failed: " + report.error);
        }

        /// <summary>Batch entry: -executeMethod AcRoguelike.Liminal.EditorTests.AugmentValidation.RunBatch</summary>
        public static void RunBatch() => EditorApplication.Exit(Run().passed ? 0 : 1);

        static Report Run()
        {
            var report = new Report();
            var host = new GameObject("AugmentValidationHunter");
            try
            {
                var augments = host.AddComponent<HunterAugments>();
                augments.Initialize(null);
                string hunter = HunterCharacter.TestHunterId;

                var all = AugmentCatalog.All;
                Require(all.Select(a => a.id).Distinct().Count() == all.Count, "Augment ids must be unique.");
                Require(all.All(a => !string.IsNullOrWhiteSpace(a.title) && !string.IsNullOrWhiteSpace(a.description)), "Every augment needs a title and a description.");
                Require(all.Count(a => a.IsCommon) >= 8, "The eight common augments are missing.");
                report.checks.Add($"Catalog: {all.Count} augments, unique ids, texts present.");

                // The test hunter sees only common augments; a character augment joins only its own hunter's pool.
                const string probeId = "validation_probe_character";
                if (AugmentCatalog.Find(probeId) == null)
                    AugmentCatalog.Register(new AugmentDefinition(probeId, "검증용", "검증용 전용 증강", characterId: "validation_hunter"));
                var probe = AugmentCatalog.Find(probeId);
                Require(!AugmentCatalog.IsOfferable(probe, augments, hunter), "A character augment was offered to another hunter.");
                Require(AugmentCatalog.IsOfferable(probe, augments, "validation_hunter"), "A character augment was not offered to its own hunter.");
                report.checks.Add("Pool: common augments for everyone, character augments only for their hunter.");

                var random = new System.Random(7);
                for (int i = 0; i < 200; i++)
                {
                    var offers = AugmentCatalog.Roll(augments, hunter, LiminalRunDirector.AugmentChoices, random);
                    Require(offers.Count == LiminalRunDirector.AugmentChoices, "A fresh run must deal three cards.");
                    Require(offers.Select(o => o.id).Distinct().Count() == offers.Count, "An offer repeated a card.");
                    Require(offers.All(o => o.IsCommon), "The test hunter was offered a character augment.");
                }
                report.checks.Add("Rolls: 200 deals of three different common cards.");

                // Uniqueness still applies even though conflicting effects stay in the pool.
                augments.Acquire(AugmentCatalog.Find(AugmentCatalog.AutoSupport));
                Require(!AugmentCatalog.IsOfferable(AugmentCatalog.Find(AugmentCatalog.AutoSupport), augments, hunter), "알아하쇼 was offered twice.");
                report.checks.Add("알아하쇼 stays unique.");

                foreach (string critId in new[] { AugmentCatalog.FxWarrior, AugmentCatalog.CountingStar })
                {
                    CheckConflictingPair(augments, hunter, AugmentCatalog.Steadfast, critId);
                    CheckConflictingPair(augments, hunter, critId, AugmentCatalog.Steadfast);
                }
                report.checks.Add("Conflicting picks: 우직하게 / FX전사 and 우직하게 / Counting Star are dealt and acquired in both orders (160 deals); crit stays 0%, damage ×1.3, duplicates stay excluded.");

                // 효율적인 검술 trims two swings per pick and stops appearing once no swing would remain (6-swing combo).
                augments.ResetRun();
                var swordplay = AugmentCatalog.Find(AugmentCatalog.EfficientSwordplay);
                Require(AugmentCatalog.IsOfferable(swordplay, augments, hunter), "효율적인 검술 missing from a fresh pool.");
                augments.Acquire(swordplay);
                Require(AugmentCatalog.IsOfferable(swordplay, augments, hunter), "효율적인 검술 should still appear while two swings would remain.");
                augments.Acquire(swordplay);
                Require(!AugmentCatalog.IsOfferable(swordplay, augments, hunter), "효율적인 검술 appeared although it would remove every swing.");
                report.checks.Add("효율적인 검술: offered at 6 and 4 swings, never when it would leave none.");

                // FX전사 and 선물거래: crit chance +30%, random crit and counter multipliers inside their ranges.
                augments.ResetRun();
                augments.Acquire(AugmentCatalog.Find(AugmentCatalog.FxWarrior));
                augments.Acquire(AugmentCatalog.Find(AugmentCatalog.FuturesWarrior));
                Require(Mathf.Approximately(augments.CritChance(.1f), .4f), "FX전사 must add 30% crit chance.");
                for (int i = 0; i < 500; i++)
                {
                    float crit = augments.CritMultiplier(1.5f), counter = augments.CounterMultiplier(1.5f);
                    Require(crit >= HunterAugments.FxCritMin && crit <= HunterAugments.FxCritMax, "FX전사 crit multiplier out of 0.1-3.");
                    Require(counter >= HunterAugments.FuturesMin && counter <= HunterAugments.FuturesMax, "선물거래 counter multiplier out of 0.1-3.5.");
                }
                report.checks.Add("FX전사: crit +30%, crit ×0.1-3. 선물거래: counter ×0.1-3.5.");

                // 예금: first pick pays 20 at every later pick; second pick pays 20 once; third pick pays 35 once.
                Require(DepositRun(augments, 1) == 2 * HunterAugments.DepositPerPick, "예금 as the first augment must pay 20 at picks 2 and 3.");
                Require(DepositRun(augments, 2) == HunterAugments.DepositSecond, "예금 as the second augment must pay 20 once.");
                Require(DepositRun(augments, 3) == HunterAugments.DepositThird, "예금 as the third augment must pay 35 once.");
                report.checks.Add("예금: 1st pick → +20 at picks 2 and 3 (40), 2nd pick → +20, 3rd pick → +35.");

                augments.ResetRun();
                Require(augments.PickCount == 0 && !augments.Has(AugmentCatalog.FxWarrior), "ResetRun kept augments.");
                report.passed = true;
            }
            catch (Exception exception) { report.error = exception.Message; }
            finally
            {
                AugmentCatalog.Unregister("validation_probe_character");
                UnityEngine.Object.DestroyImmediate(host);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
            return report;
        }

        /// <summary>Offers and acquires a conflicting pair without changing its effects or uniqueness.</summary>
        static void CheckConflictingPair(HunterAugments augments, string hunter, string heldId, string nextId)
        {
            augments.ResetRun();
            var held = AugmentCatalog.Find(heldId);
            var next = AugmentCatalog.Find(nextId);
            augments.Acquire(held);
            bool appeared = false;
            var random = new System.Random(19);
            for (int i = 0; i < 40; i++)
            {
                var offers = AugmentCatalog.Roll(augments, hunter, LiminalRunDirector.AugmentChoices, random);
                Require(offers.Count == 3 && offers.Select(o => o.id).Distinct().Count() == offers.Count,
                    "A conflicting build must still get three different cards.");
                Require(!offers.Contains(held), $"Unique held augment {heldId} was offered again.");
                if (offers.Contains(next)) appeared = true;
            }
            Require(appeared, $"Conflicting augment {nextId} never appeared while holding {heldId}.");
            augments.Acquire(next);
            Require(augments.Has(heldId) && augments.Has(nextId), "A conflicting pick removed an earlier augment.");
            Require(augments.CritChance(.1f) == 0 && Mathf.Approximately(augments.DamageMultiplier(null, false), HunterAugments.SteadfastMultiplier),
                "우직하게 must still fix crit at 0% and multiply damage by 1.3 in either pick order.");
            Require(!AugmentCatalog.IsOfferable(held, augments, hunter) && !AugmentCatalog.IsOfferable(next, augments, hunter),
                "Allowing conflicting effects must not allow duplicate unique augments.");
        }

        /// <summary>Runs three picks with 예금 at `depositPick` (filler augments elsewhere) and returns the stones paid.</summary>
        static int DepositRun(HunterAugments augments, int depositPick)
        {
            augments.ResetRun();
            var filler = new[] { AugmentCatalog.Yarara, AugmentCatalog.CountingStar, AugmentCatalog.FuturesWarrior };
            int next = 0;
            for (int pick = 1; pick <= 3; pick++)
                augments.Acquire(AugmentCatalog.Find(pick == depositPick ? AugmentCatalog.Deposit : filler[next++]));
            return augments.DepositPaid;
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
