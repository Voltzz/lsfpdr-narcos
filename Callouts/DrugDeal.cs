using System;
using System.Drawing;
using Rage;
using LSPD_First_Response.Mod.Callouts;
using LSPD_First_Response.Mod.API;
using System.Collections.Generic;

namespace narcos.Callouts
{
    [CalloutInfo("[N*A*R*C*O*S] Drug Deal", CalloutProbability.High)]

    public class DrugDeal : Callout
    {
        public Vector3 spawnPoint;
        public Blip myBlip;
        public Ped mySuspect;
        public string mySuspectGender;
        public Ped druggie;
        public bool animationActivatedP1, animationActivatedP2, animationActivatedP3;
        private bool calloutRunning;
        private static string CALLOUT_SHORTNAME = "DrugDeal";
        private static LHandle activePursuit = null;
        private static float MAX_DISTANCE_SPAWN = 500f;
        private static float MAX_DRUGGIE_DIST_SPAWN = 20f;
        private static float SITUATION_ACTIVATE_THRESHOLD = 80f;
        private static float COP_NOTICED_AT_DISTANCE = 25f;
        private static Random rnd = new Random(MathHelper.GetRandomInteger(100, 100000));
        private List<Ped> susBackup;
        private List<Vehicle> susBackupVhc;
        private bool isSuspectArmed = false;
        private Dictionary<Ped, Blip> guardBlips;

        public override bool OnBeforeCalloutDisplayed()
        {
            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Initiating " + CALLOUT_SHORTNAME + " callout...");

            // Figure out where to spawn callout
            spawnPoint = HelperFuncs.FindBestPositionForPed(Game.LocalPlayer.Character.Position, MAX_DISTANCE_SPAWN);
            if (spawnPoint == Vector3.Zero)
            {
                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Couldn't find safe ped place near player, aborting callout initiation.");
                return false;
            }

            // Spawn drug deal props
            mySuspect = new Ped(spawnPoint);
            mySuspectGender = mySuspect.IsMale ? "male" : "female";

            Vector3 druggieSpawnPoint = HelperFuncs.FindBestPositionForPed(mySuspect.Position, MAX_DRUGGIE_DIST_SPAWN);
            if (druggieSpawnPoint == Vector3.Zero)
            {
                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Couldn't find another safe ped place near suspect, aborting callout initiation.");
                return false;
            }
            druggie = new Ped(druggieSpawnPoint);

            druggie.Face(mySuspect);
            mySuspect.Face(druggie);
            animationActivatedP1 = false;
            animationActivatedP2 = false;
            animationActivatedP3 = false;
            mySuspect.IsPersistent = true;
            druggie.IsPersistent = true;
            mySuspect.BlockPermanentEvents = true;
            druggie.BlockPermanentEvents = true;
            mySuspect.CanAttackFriendlies = true;
            activePursuit = null;
            isSuspectArmed = false;
            calloutRunning = false;
            susBackup = new List<Ped>();
            susBackupVhc = new List<Vehicle>();
            guardBlips = new Dictionary<Ped, Blip>();

            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Suspect & druggie have been placed.");

            // Sanity check before showing callout
            if (!mySuspect.Exists() || !druggie.Exists()) return false;

            // Create callout for user
            this.ShowCalloutAreaBlipBeforeAccepting(spawnPoint, 20f);
            this.AddMinimumDistanceCheck(50f, spawnPoint);
            this.CalloutMessage = "Possible Drug Dealing";
            this.CalloutPosition = spawnPoint;

            // Display callout
            Functions.PlayScannerAudioUsingPosition("CITIZENS_REPORT_04 CRIME_DISTURBING_THE_PEACE_02 IN_OR_ON_POSITION", this.spawnPoint);

            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Callout fully initiated!");

            return base.OnBeforeCalloutDisplayed();
        }

        public override bool OnCalloutAccepted()
        {
            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player accepted callout!");

            // Show suspect and instructions
            myBlip = mySuspect.AttachBlip();
            myBlip.Color = Color.Yellow;
            myBlip.Scale = 0.6f;
            myBlip.EnableRoute(Color.Yellow);
            Game.DisplayNotification("Citizens report a ~r~" + mySuspectGender + "~s~ handing out suspicious packages.~n~~n~Monitor the situation & confront the suspect.");
            Functions.PlayScannerAudio("REPORT_RESPONSE_COPY_04 UNITS_RESPOND_CODE_02_02");

            initiateSituation();

            return base.OnCalloutAccepted();
        }

        public override void OnCalloutNotAccepted()
        {
            base.OnCalloutNotAccepted();
            HelperFuncs.ChangeDifficulty(true);
            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player rejected callout.");

            if (mySuspect.Exists()) mySuspect.Delete();
            if (druggie.Exists()) druggie.Delete();
            if (myBlip.Exists()) myBlip.Delete();
        }

        public override void Process()
        {
            base.Process();

            // Sanity check
            if (Game.LocalPlayer.Character.Exists())
            {
                if (Game.LocalPlayer.Character.IsDead)
                {
                    Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player has died.");
                    HelperFuncs.ChangeDifficulty(true);
                    GameFiber.StartNew(End);
                }
            }
            else
            {
                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player no longer exists.");
                GameFiber.StartNew(End);
            }
        }

        private void initiateSituation()
        {
            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Initating situation...");
            calloutRunning = true;
            GameFiber.StartNew(delegate
            {
                try
                {
                    // Setup situation
                    while (calloutRunning)
                    {
                        GameFiber.Yield();

                        // Plant drugs (DIRTY COP!) and weapons
                        isSuspectArmed = (rnd.Next(1, 101) < int.Parse(Main.settingsFile.Read("isSuspectArmedProbability", "DrugDeal")));
                        if (isSuspectArmed)
                        {
                            if (Main.Difficulty < 6)
                            {
                                mySuspect.Inventory.GiveNewWeapon("WEAPON_PISTOL", 40, true);
                            }
                            if (Main.Difficulty < 8)
                            {
                                mySuspect.Inventory.GiveNewWeapon("WEAPON_PISTOL", 80, true);
                                mySuspect.Armor = 50;
                            }
                            if (Main.Difficulty >= 10)
                            {
                                mySuspect.Inventory.GiveNewWeapon("WEAPON_ASSAULTRIFLE", 60, true);
                                mySuspect.Armor = 100;
                            }
                            mySuspect.Metadata.searchPed = "~y~Knife~s~, ~r~1 baggie of heroin~s~";
                            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Suspect is armed.");
                        } else
                        {
                            mySuspect.Metadata.searchPed = "~y~Knife~s~, ~r~1 baggie of heroin~s~";
                            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Suspect is NOT armed.");
                        }
                        mySuspect.Metadata.hasGunPermit = false;
                        druggie.Metadata.stpDrugsDetected = true;

                        druggie.Face(mySuspect);
                        mySuspect.Face(druggie);

                        mySuspect.Tasks.PlayAnimation("amb@world_human_drug_dealer_hard@male@idle_a", "idle_a", 1.0f, AnimationFlags.Loop);

                        Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Situation initiated!");
                        break;
                    }

                    // Trigger animation when player is close
                    while (calloutRunning)
                    {
                        GameFiber.Yield();
                        if (Game.LocalPlayer.Character.Position.DistanceTo(spawnPoint) <= SITUATION_ACTIVATE_THRESHOLD)
                        {
                            if (!animationActivatedP1)
                            {
                                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player is nearby, activating situation...");
                                animationActivatedP1 = true;
                                druggie.Face(mySuspect);
                                mySuspect.Face(druggie);
                                druggie.Tasks.GoToOffsetFromEntity(mySuspect, 1f, 0f, 1f);
                                
                                Game.DisplaySubtitle("Confront the ~y~suspect~w~.", 4000);
                                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Situation activated!");
                            }
                            break;
                        }
                    }

                    // Chain animation when first animation is complete
                    while (calloutRunning)
                    {
                        GameFiber.Yield();
                        if (druggie.Position.DistanceTo(mySuspect) <= 2f)
                        {
                            if (!animationActivatedP2)
                            {
                                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Druggie has reached suspect, activating animation 2...");
                                animationActivatedP2 = true;
                                mySuspect.Face(druggie);
                                druggie.Tasks.Clear();
                                druggie.Face(mySuspect);
                                druggie.Tasks.PlayAnimation("amb@world_human_bum_standing@depressed@idle_a", "idle_a", 1.0f, AnimationFlags.Loop);
                                HelperFuncs.DisplayInstruction("Confront the ~y~suspect~w~.");
                                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Animations complete!");
                            }
                            break;
                        }
                        if (Game.LocalPlayer.Character.Position.DistanceTo(spawnPoint) <= COP_NOTICED_AT_DISTANCE)
                        {
                            druggie.Tasks.Clear();
                            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player arrived before druggie did.");
                            break;
                        }
                    }

                    // Final animation
                    while (calloutRunning)
                    {
                        GameFiber.Yield();
                        if (activePursuit == null)
                        {
                            if (Game.LocalPlayer.Character.Position.DistanceTo(spawnPoint) <= COP_NOTICED_AT_DISTANCE)
                            {
                                druggie.Tasks.ClearImmediately();
                                druggie.Tasks.Flee(Game.LocalPlayer.Character, 1000f, 30000);

                                activePursuit = Functions.CreatePursuit();
                                Functions.SetPursuitAsCalledIn(activePursuit);
                                Functions.SetPursuitIsActiveForPlayer(activePursuit, true);
                                Functions.AddPedToPursuit(activePursuit, mySuspect);
                                PedPursuitAttributes pattr = Functions.GetPedPursuitAttributes(mySuspect);
                                pattr.AverageFightTime = (int)Math.Floor(30 / Math.Floor((double)Main.Difficulty / 2)); // seconds
                                pattr.AverageSurrenderTime = 120 * (int)Math.Floor((double)Main.Difficulty / 2); // seconds
                                pattr.CanUseCars = true;
                                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player arrived, everyone fleeing...");
                                HelperFuncs.DispatchCartelBackup(spawnPoint, ref susBackup, ref susBackupVhc, ref guardBlips, 6);
                            }
                            else
                            {
                                HelperFuncs.DisplayInstruction("Confront the ~y~suspect~w~.");
                            }
                        } else
                        {
                            break;
                        }
                    }

                    // Wait for callout resolution
                    while (calloutRunning)
                    {
                        GameFiber.Yield();
                        if (activePursuit == null || !Functions.IsPursuitStillRunning(activePursuit))
                        {
                            GameFiber.Sleep(3000);
                            GameFiber.Yield();
                            GameFiber.Sleep(3000);
                            if (mySuspect.IsAlive && !mySuspect.IsCuffed)
                            {
                                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player failed at callout!");
                                GameFiber.Sleep(3000);
                                Game.DisplayNotification("Pursuit was lost and ~r~suspect is still at large~s~. No further units necessary, a BOLO will follow.");
                                HelperFuncs.ChangeDifficulty(true);
                            }
                            else
                            {
                                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player resolved callout!");
                                GameFiber.Sleep(3000);
                                Game.DisplayNotification("~g~Code 4~s~. Suspect was subdued. All Clear.");
                                HelperFuncs.ChangeDifficulty(false);
                            }

                            End();
                            break;
                        }
                        foreach (Ped guard in susBackup)
                        {
                            if (!guard.Exists() || !guard.IsAlive || guard.IsCuffed)
                            {
                                if (guardBlips.ContainsKey(guard) && guardBlips[guard].Exists()) guardBlips[guard].Delete();
                                if (guard.Exists()) guard.Dismiss();
                            }
                        }
                    }
                }
                catch (System.Threading.ThreadAbortException e)
                {
                    Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Received force end from LSPDFR, ending callout...");
                    Game.DisplayNotification("Callout was ~r~abandoned~s~.");
                    HelperFuncs.ChangeDifficulty(true);
                    End();
                }
                catch (Exception e)
                {
                    if (calloutRunning)
                    {
                        Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + e.ToString());
                        Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + Main.PLUGIN_FULL_NAME + " had a critical error but was handled gracefully.");
                        Game.DisplayNotification(Main.PLUGIN_FULL_NAME + " had a critical error but was handled gracefully. Send Phyvolt your log to investigate.");
                        End();
                    }
                }
            });
        }

        public override void End()
        {
            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Ending callout...");

            try
            {
                calloutRunning = false;
                if (mySuspect.Exists()) mySuspect.Dismiss();
                if (druggie.Exists()) druggie.Dismiss();
                if (myBlip.Exists()) myBlip.Delete();
                foreach (var susBackupPed in susBackup)
                {
                    if (susBackupPed.Exists())
                    {
                        if (guardBlips.ContainsKey(susBackupPed) && guardBlips[susBackupPed].Exists())
                        {
                            guardBlips[susBackupPed].Delete();
                        }
                        susBackupPed.Dismiss();
                    }
                }
                foreach (var susVhc in susBackupVhc)
                {
                    if (susVhc.Exists()) susVhc.Dismiss();
                }
                susBackup.Clear();
                susBackupVhc.Clear();
            } catch (Exception e)
            {
                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + e.ToString());
                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + Main.PLUGIN_FULL_NAME + " could not clean up properly. Your game could be unstable.");
            }
            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Ended callout!");

            base.End();
        }
    }
}
