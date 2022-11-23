using System;
using System.Drawing;
using Rage;
using LSPD_First_Response.Mod.Callouts;
using LSPD_First_Response.Mod.API;
using System.Linq;
using System.Collections.Generic;

namespace narcos.Callouts
{
    [CalloutInfo("[N*A*R*C*O*S] Drug Seize", CalloutProbability.Medium)]

    public class DrugSeize : Callout
    {
        private static Vector3[][] psblSpawnLocs =
        {
            // First location is truck placement
            // Second location is cop car
            new Vector3[] { new Vector3(2698.356f, 3298.977f, 54.84503f), new Vector3(2715.468f, 3315.244f, 55.05704f) }, // Mini-mart Gas Station (Senora Fwy)
            new Vector3[] { new Vector3(2544.395f, 4213.384f, 39.68443f), new Vector3(2529.618f, 4211.344f, 39.22934f) }, // Closed cones Gas Station (Grapeseed)
            new Vector3[] { new Vector3(1651.837f, 6413.189f, 28.593f), new Vector3(1674.344f, 6407.127f, 29.86045f) }, // Coastal Gas Station (Senora Fwy)
            new Vector3[] { new Vector3(-2609.172f, 2314.892f, 27.48362f), new Vector3(-2584.16f, 2321.606f, 32.07143f) }, // Brown coast Gas Station
        };
        private static string[][] dialogPossibilities =
        {
            // Odd numbers are officer, even are player (assuming 1-index)
            new string[] {"Plates were expired, and I noticed white powder on the bumper", "Guy was speeding but might be a drug runner - he's got the narcos tattoo", "I got a bad feeling about this guy", "Don't know if there's drugs, but can't be too careful nowadays", "I've seen a lot of trucks like this carry dope" },
            new string[] {"Want me to take point?", "I'll go check it out", "Leave it to me", "I got this", "I'll go ahead", "You stay here"},
            new string[] {"Sounds good", "Thanks", "I got your six", "Yep", "I'm right here"}
        };
        private static string CALLOUT_SHORTNAME = "DrugSeize";
        private static float SITUATION_ACTIVATE_THRESHOLD = 250f;
        private static float DIALOG_RADIUS_THRESHOLD = 15f;
        private static float RUN_DISTANCE_THRESHOLD = 5f;
        private static float DISTANCE_ASSUMED_PED_DISMISSED = 25f;

        private int chosenSpawnIdx;
        private bool calloutRunning;
        private Blip targetBlip;
        private bool targetHasDrugs;
        private bool targetWillRun;
        private Ped suspect;
        private Vehicle susVhc;
        private Ped cop;
        private Vehicle copCar;
        private List<Ped> susBackup;
        private List<Vehicle> susBackupVhc;
        private static LHandle activePursuit;
        private Dictionary<Ped, Blip> guardBlips;


        private static Random rnd = new Random(MathHelper.GetRandomInteger(100, 100000));

        public override bool OnBeforeCalloutDisplayed()
        {
            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Initiating " + CALLOUT_SHORTNAME + " callout...");

            // Figure out where to spawn callout
            chosenSpawnIdx = rnd.Next(0, psblSpawnLocs.Length);
            chosenSpawnIdx = 3;
            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Chose location #" + chosenSpawnIdx + ": (" + psblSpawnLocs[chosenSpawnIdx][0].X + ", " + psblSpawnLocs[chosenSpawnIdx][0].Y + ", " + psblSpawnLocs[chosenSpawnIdx][0].Z + ")");

            calloutRunning = false;
            targetHasDrugs = false;
            targetWillRun = false;
            activePursuit = null;
            susBackup = new List<Ped>();
            susBackupVhc = new List<Vehicle>();

            // Create callout for user
            this.ShowCalloutAreaBlipBeforeAccepting(psblSpawnLocs[chosenSpawnIdx][0], 30f);
            this.AddMinimumDistanceCheck(50f, psblSpawnLocs[chosenSpawnIdx][0]);
            this.CalloutMessage = "Drugs Seizure Assistance";
            this.CalloutPosition = psblSpawnLocs[chosenSpawnIdx][0];

            // Display callout
            Functions.PlayScannerAudioUsingPosition("WE_HAVE_02 CRIME_OFFICER_IN_NEED_OF_ASSISTANCE_01 IN_OR_ON_POSITION", psblSpawnLocs[chosenSpawnIdx][0]);

            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Callout fully initiated!");

            return base.OnBeforeCalloutDisplayed();
        }

        public override bool OnCalloutAccepted()
        {
            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player accepted callout!");

            // Show area and instructions
            targetBlip = new Blip(psblSpawnLocs[chosenSpawnIdx][0], 20f);
            targetBlip.Color = Color.Yellow;
            targetBlip.Alpha = 0.7f;
            targetBlip.EnableRoute(Color.Yellow);

            Game.DisplayNotification("3dtextures", "mpgroundlogo_cops", "Drug Seizure", "~y~Provide Backup", "Officer in need of assistance in a traffic stop involving narcotics.~n~~n~Provide backup to the officer.");
            Functions.PlayScannerAudio("REPORT_RESPONSE_COPY_02 UNITS_RESPOND_CODE_02_01");

            initiateSituation();

            return base.OnCalloutAccepted();
        }

        public override void OnCalloutNotAccepted()
        {
            base.OnCalloutNotAccepted();
            HelperFuncs.ChangeDifficulty(true);
            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player rejected callout.");
        }

        public override void Process()
        {
            base.Process();

            // Sanity check
            if (Game.LocalPlayer.Character.Exists())
            {
                if (Game.LocalPlayer.Character.IsDead)
                {
                    HelperFuncs.ChangeDifficulty(true);
                    Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player has died.");
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
                    bool initialSetup = false;

                    // Wait for player approach and keypress
                    while (calloutRunning)
                    {
                        GameFiber.Yield();
                        if (Game.LocalPlayer.Character.Position.DistanceTo(psblSpawnLocs[chosenSpawnIdx][0]) <= SITUATION_ACTIVATE_THRESHOLD)
                        {
                            if (!initialSetup)
                            {
                                // Spawn truck & cop
                                int roll = rnd.Next(1, 101);
                                targetHasDrugs = false;
                                if(roll <= int.Parse(Main.settingsFile.Read("drugsChance", "DrugSeize")))
                                {
                                    targetHasDrugs = true;
                                }
                                suspect = new Ped(HelperFuncs.GetRandomMexModel(), psblSpawnLocs[chosenSpawnIdx][0], 0);
                                suspect.IsPersistent = true;
                                suspect.KeepTasks = true;
                                cop = new Ped("S_M_Y_COP_01", psblSpawnLocs[chosenSpawnIdx][1].Around(1f, 3f), 0);
                                cop.IsPersistent = true;
                                cop.KeepTasks = true;
                                cop.RelationshipGroup = RelationshipGroup.Cop;
                                susVhc = new Vehicle("POUNDER2", psblSpawnLocs[chosenSpawnIdx][0], 0);
                                susVhc.IsPersistent = true;
                                susVhc.PrimaryColor = Color.White;
                                susVhc.SecondaryColor = Color.White;
                                copCar = new Vehicle("POLICE3", psblSpawnLocs[chosenSpawnIdx][1], 0);
                                copCar.IsPersistent = true;
                                var elights = copCar.EmergencyLighting.Clone();
                                elights.Lights.ToList().ForEach(l => l.Light = true);
                                copCar.EmergencyLightingOverride = elights;
                                copCar.IsSirenOn = true;
                                suspect.WarpIntoVehicle(susVhc, -1);

                                // Fun way to get them to face the same direction
                                cop.Face(suspect);
                                susVhc.Heading = cop.Heading;
                                copCar.Heading = cop.Heading;

                                if (targetHasDrugs)
                                {
                                    if (Main.Difficulty < 6)
                                    {
                                        suspect.Inventory.GiveNewWeapon("WEAPON_PISTOL", 40, true);
                                    }
                                    if (Main.Difficulty < 8)
                                    {
                                        suspect.Inventory.GiveNewWeapon("WEAPON_PISTOL", 80, true);
                                        suspect.Armor = 50;
                                    }
                                    if (Main.Difficulty >= 10)
                                    {
                                        suspect.Inventory.GiveNewWeapon("WEAPON_ASSAULTRIFLE", 60, true);
                                        suspect.Armor = 100;
                                    }
                                    suspect.Metadata.searchPed = "~y~Knife~s~, ~s~Wallet~s~, ~y~Disposable cellphone~s~";
                                    susVhc.Metadata.searchDriver = "A map of Los Santos";
                                    susVhc.Metadata.searchPassenger = "~y~3 disposable cellphones~s~";
                                    susVhc.Metadata.searchTrunk = "~r~Clucking Bell Chicken boxes filled with cocaine~s~";
                                }
                                else
                                {
                                    // randomize
                                }

                                if (!suspect.Exists() || !cop.Exists() || !susVhc.Exists() || !copCar.Exists())
                                {
                                    Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Couldn't initiate situation: " + chosenSpawnIdx);
                                    HelperFuncs.DisplayInstruction("Error spawning creating situation. Notify Phyvolt with this bug. Dismissed!");
                                    End();
                                    break;
                                }

                                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Situation initiated.");

                                initialSetup = true;
                            }
                            if (Game.LocalPlayer.Character.Position.DistanceTo(psblSpawnLocs[chosenSpawnIdx][1]) > DIALOG_RADIUS_THRESHOLD)
                            {
                                HelperFuncs.DisplayInstruction("Approach the ~g~officer~w~ and let them know you're ready.");
                            }
                            else
                            {
                                if (Game.IsKeyDown(Main.mainInteractionKey))
                                {
                                    Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player initiated dialog.");
                                    break;
                                }
                                else
                                {
                                    HelperFuncs.DisplayInstruction("Press ~y~~h~" + Main.mainInteractionKeyStr + "~h~~s~ to start dialog");
                                }
                            }

                        }
                    }
                    // Back-and-forth dialog
                    while (calloutRunning)
                    {
                        GameFiber.Yield();

                        Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Triggering dialog.");
                        targetBlip.DisableRoute();

                        // Initiate dialog
                        int roll = rnd.Next(0, dialogPossibilities[0].Length);
                        Game.DisplaySubtitle("~g~Officer~s~: " + dialogPossibilities[0][roll], Main.DELAY_BETWEEN_DIALOG);
                        GameFiber.Sleep(Main.DELAY_BETWEEN_DIALOG);
                        roll = rnd.Next(0, dialogPossibilities[1].Length);
                        Game.DisplaySubtitle("~b~You~s~: " + dialogPossibilities[1][roll], Main.DELAY_BETWEEN_DIALOG);
                        GameFiber.Sleep(Main.DELAY_BETWEEN_DIALOG);
                        roll = rnd.Next(0, dialogPossibilities[2].Length);
                        Game.DisplaySubtitle("~g~Officer~s~: " + dialogPossibilities[2][roll], Main.DELAY_BETWEEN_DIALOG);

                        Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Dialog complete!");
                        break;
                    }

                    // Determine whether target will run
                    while (calloutRunning)
                    {
                        GameFiber.Yield();
                        HelperFuncs.DisplayInstruction("Approach the ~r~driver~s~ and search the vehicle.");
                        int roll = rnd.Next(1, 101);
                        targetWillRun = false;
                        if(targetHasDrugs && roll <= int.Parse(Main.settingsFile.Read("runChanceIfDrugs", "DrugSeize")))
                        {
                            targetWillRun = true;
                        }
                        if (Game.LocalPlayer.Character.Position.DistanceTo(psblSpawnLocs[chosenSpawnIdx][0]) <= RUN_DISTANCE_THRESHOLD)
                        {
                            if(targetWillRun)
                            {
                                activePursuit = Functions.CreatePursuit();
                                Functions.SetPursuitAsCalledIn(activePursuit);
                                Functions.SetPursuitIsActiveForPlayer(activePursuit, true);
                                Functions.AddPedToPursuit(activePursuit, suspect);
                                PedPursuitAttributes pattr = Functions.GetPedPursuitAttributes(suspect);
                                pattr.AverageFightTime = (int)Math.Floor(60 / Math.Floor((double)Main.Difficulty / 2)); // seconds
                                pattr.AverageSurrenderTime = 600 * (int)Math.Floor((double)Main.Difficulty / 2); // seconds
                                pattr.BurstTireMaxDrivingSpeedMult = 0.1f;
                                pattr.BurstTireSurrenderMult = 0.05f;
                                pattr.MaxDrivingSpeed = 70f;
                                pattr.MinDrivingSpeed = 20f;
                                pattr.SurrenderChanceCarBadlyDamaged = 30f;
                                pattr.SurrenderChanceTireBurst = 5f;
                                pattr.SurrenderChanceTireBurstAndCrashed = 30f;
                                pattr.CanUseCars = true;
                                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Suspect decided to run");
                                if (targetBlip.Exists()) targetBlip.Delete();
                                HelperFuncs.DispatchCartelBackup(psblSpawnLocs[chosenSpawnIdx][0], ref susBackup, ref susBackupVhc, ref guardBlips);
                            }
                            break;
                        }
                    }

                    // Check if suspect subdued or was innocent
                    while (calloutRunning)
                    {
                        GameFiber.Yield();
                        if(targetWillRun)
                        {
                            if (activePursuit == null || !Functions.IsPursuitStillRunning(activePursuit) || !suspect.IsAlive || suspect.IsCuffed)
                            {
                                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player resolved callout!");
                                GameFiber.Sleep(2000);
                                Game.DisplayNotification("~g~Code 4~s~. Narcotics were found and suspect was subdued.");
                                HelperFuncs.ChangeDifficulty(false);
                                End();
                                break;
                            }
                        }
                        else
                        {
                            HelperFuncs.DisplayInstruction("~r~Arrest~s~ the suspect on reasonable grounds or ~g~dismiss them~s~.");
                            if (!suspect.IsAlive || suspect.IsCuffed)
                            {
                                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player resolved callout!");
                                GameFiber.Sleep(2000);
                                if (targetHasDrugs) Game.DisplayNotification("~g~Code 4~s~. Suspect was subdued.");
                                else Game.DisplayNotification("~g~Code 4~s~. Suspect was subdued but ~y~ the DA has questions ~s~ and wants to see your report.");
                                HelperFuncs.ChangeDifficulty(false);
                                End();
                                break;
                            }
                            
                            if(!suspect.Exists() || suspect.DistanceTo(psblSpawnLocs[chosenSpawnIdx][0]) > DISTANCE_ASSUMED_PED_DISMISSED)
                            {
                                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player resolved callout!");
                                GameFiber.Sleep(2000);
                                Game.DisplayNotification("~g~Code 4~s~. No illegal narcotics were found and suspect was let go.");
                                End();
                                break;
                            }
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
                if (targetBlip.Exists()) targetBlip.Delete();
                if (suspect.Exists()) suspect.Dismiss();
                if (cop.Exists()) cop.Dismiss();
                if (susVhc.Exists()) susVhc.Dismiss();
                if (copCar.Exists()) copCar.Dismiss();
                foreach(var susBackupPed in susBackup)
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
