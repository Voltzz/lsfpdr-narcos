using System;
using System.Drawing;
using Rage;
using LSPD_First_Response.Mod.Callouts;
using LSPD_First_Response.Mod.API;
using System.Collections.Generic;
using RAGENativeUI.Elements;
using RAGENativeUI;
using System.Linq;

namespace narcos.Callouts
{
    [CalloutInfo("[N*A*R*C*O*S] Drug Bust", CalloutProbability.Low)]

    public class DrugBust : Callout
    {
        private static Vector3[] psblSpwnPts = {
            new Vector3(292.2537f, -225.0075f, 52.97149f), // Inner city (Alta)
            new Vector3(915.3353f, -2153.865f, 29.51985f), // Weird industrial place (Cypress Flats)
            new Vector3(2674.698f, 3266.435f, 54.40443f), // Gas Station Mini-Mart (Senora Fwy)
            new Vector3(1963.597f, 5184.757f, 47.41104f), // Farmhouse area (Grapeseed)
            new Vector3(-17.72083f, 6304.347f, 30.49691f), // Clucking Bell Farms (Great Ocean Hwy)
            new Vector3(506.4282f, 2611.515f, 41.93968f), // Really small town's house (Harmony)
            new Vector3(1611.936f, 3573.456f, 34.53814f), // Abandoned complex outside of town
        };
        private static Vector3[][] guardSpwnLocs = {
            // First location is initial guard placement
            // Second is where additional guards will initially spawn
            // Third is destination left of player facing initial position
            // Fourth is destination right of player facing initial position
            new Vector3[] { new Vector3(292.2537f, -225.0075f, 52.97149f), new Vector3(253.5485f, -194.463f, 54.13042f), new Vector3(286.8022f, -222.9964f, 52.97149f), new Vector3(298.2079f, -226.7906f, 52.9711f),},
            new Vector3[] { new Vector3 (916.0668f, -2153.485f, 29.51985f), new Vector3(899.2725f, -2137.338f, 29.50291f), new Vector3(915.6213f, -2158.576f, 29.51985f), new Vector3 (916.3984f, -2149.693f, 29.51985f), },
            new Vector3[] { new Vector3 (2675.666f, 3265.785f, 54.40443f), new Vector3 (2661.409f, 3276.853f, 54.30164f), new Vector3(2673.359f, 3262.605f, 54.25671f), new Vector3(2677.933f, 3269.971f, 54.25671f), },
            new Vector3[] { new Vector3 (1966.011f, 5182.864f, 47.41104f), new Vector3 (1953.357f, 5190.183f, 47.71411f), new Vector3(1963.49f, 5179.363f, 47.41104f), new Vector3(1972.248f, 5179.345f, 47.41104f), },
            new Vector3[] { new Vector3 (-18.71879f, 6305.939f, 30.49661f), new Vector3 (3.038635f, 6271.801f, 31.94991f), new Vector3(-10.14606f, 6309.991f, 30.50008f), new Vector3(-24.37787f, 6303.261f, 30.49432f), },
            new Vector3[] { new Vector3 (505.9676f, 2613.464f, 41.93968f), new Vector3 (506.1577f, 2594.013f, 41.93968f), new Vector3(513.6105f, 2613.765f, 41.93968f), new Vector3(496.8881f, 2612.336f, 41.93968f), },
            new Vector3[] { new Vector3 (1613.017f, 3574.146f, 34.53814f), new Vector3 (1603.538f, 3568.835f, 37.91381f), new Vector3(1614.639f, 3571.615f, 37.97082f), new Vector3(1611.532f, 3576.373f, 37.97082f), },
        };
        private static int[] guardBackupArmyMult = { 0, 0, 1, 2, 1, 0, 3 };
        private static string[] dialogPlayer =
        {
            "Let me through",
            "This is your last warning",
        };
        private static string[][] dialogPossibilities =
        {
            new string[] { "No one goes in or out", "Back away, slowly", "You got a death wish, officer?", "This isn't the place for you, man", "What do you want?", "Off limits, badge or no badge", "Nah, you aint gettin in", "Aw hell na" },
            new string[] { "You got a warrant?", "Or what?", "You want keys to my house too?", "Donut shop is around the corner", "Turn around and walk away", "How about no?", "Ain't gonna happen", "You really think you're comin in?"}, // Response to "Let me through"
            new string[] { "One more step and I'll blow your head clean", "We aint messing around. Come closer and you're dead.", "Last chance to walk away homeboy", "I ain't gonna say it twice", "You gon' make me shoot you", "Hey boys, we got a hero here!" }, // Response to "This is your last warning"
        };
        private static string CALLOUT_SHORTNAME = "DrugBust";
        private static Random rnd = new Random(MathHelper.GetRandomInteger(100, 100000));
        private static float SITUATION_ACTIVATE_THRESHOLD = 100f;
        private static float DIALOG_RADIUS_THRESHOLD = 8f;
        private static float ATTACK_RADIUS_THRESHOLD = 3f;
        private static float FORENSICS_SPAWN_DISTANCE = 80f;
        private static int SWITCH_ANIMATION_TIME_INVESTIGATE = 7; // seconds
        private int numInitialGuards;
        private int numAddtlGuards;
        private int secureAreaTime;
        private List<Ped> guards;
        private Dictionary<Ped, Blip> guardBlips;
        private int chosenSpawnIdx;
        private bool calloutRunning;
        private Blip targetBlip;
        private Ped forensics;
        private Ped forensics2;
        private Vehicle forensicsVhc;
        private Blip forensicBlip;
        private TextTimerBar timerBar;
        private TimerBarPool timerBarPool;
        private TimeSpan targetTimeForensics;
        private bool playerInitiatedAttack;
        private bool guardsInitiatedAttack;
        private bool allGuardsSpawned;
        private List<Ped> backup;
        private List<Vehicle> backupVhc;

        public override bool OnBeforeCalloutDisplayed()
        {
            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Initiating " + CALLOUT_SHORTNAME + " callout...");
            numInitialGuards = int.Parse(Main.settingsFile.Read("numInitialGuards", "DrugBust"));
            numAddtlGuards = int.Parse(Main.settingsFile.Read("numAddtlGuards", "DrugBust"));
            secureAreaTime = int.Parse(Main.settingsFile.Read("secureAreaTime", "DrugBust"));

            // Figure out where to spawn callout
            chosenSpawnIdx = rnd.Next(0, psblSpwnPts.Length);
            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Chose location #" + chosenSpawnIdx + ": (" + psblSpwnPts[chosenSpawnIdx].X + ", " + psblSpwnPts[chosenSpawnIdx].Y + ", " + psblSpwnPts[chosenSpawnIdx].Z + ")");

            calloutRunning = false;
            playerInitiatedAttack = false;
            guardsInitiatedAttack = false;
            allGuardsSpawned = false;
            guards = new List<Ped>();
            guardBlips = new Dictionary<Ped, Blip>();
            backup = new List<Ped>();
            backupVhc = new List<Vehicle>();

            // Create callout for user
            this.ShowCalloutAreaBlipBeforeAccepting(psblSpwnPts[chosenSpawnIdx], 30f);
            this.AddMinimumDistanceCheck(50f, psblSpwnPts[chosenSpawnIdx]);
            this.CalloutMessage = "Suspected Drug Stash";
            this.CalloutPosition = psblSpwnPts[chosenSpawnIdx];

            // Display callout
            Functions.PlayScannerAudioUsingPosition("ATTENTION_ALL_UNITS_01 WE_HAVE_02 CRIME_DISTURBING_THE_PEACE_02 IN_OR_ON_POSITION", psblSpwnPts[chosenSpawnIdx]);

            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Callout fully initiated!");

            return base.OnBeforeCalloutDisplayed();
        }

        public override bool OnCalloutAccepted()
        {
            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player accepted callout!");

            // Show area and instructions
            targetBlip = new Blip(psblSpwnPts[chosenSpawnIdx], 15f);
            targetBlip.Color = Color.Yellow;
            targetBlip.Alpha = 0.6f;
            targetBlip.EnableRoute(Color.Yellow);

            Game.DisplayNotification("3dtextures", "mpgroundlogo_cops", "Drug Stash", "~y~Investigate", "Officers believe there may be drugs stashed in the area.~n~~n~Investigate and confiscate any narcotics.");
            Functions.PlayScannerAudio("REPORT_RESPONSE_COPY_03 UNITS_RESPOND_CODE_02_02");

            initiateSituation();

            return base.OnCalloutAccepted();
        }

        public override void OnCalloutNotAccepted()
        {
            base.OnCalloutNotAccepted();
            HelperFuncs.ChangeDifficulty(true);

            foreach (Ped guard in guards)
            {
                if (guard.Exists())
                {
                    guard.Dismiss();
                }
            }

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

        private void monitorPlayerAttack()
        {
            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Started ped attacked monitor");
            GameFiber.StartNew(delegate
            {
                try
                {
                    while(calloutRunning && !playerInitiatedAttack && !guardsInitiatedAttack)
                    {
                        GameFiber.Yield();
                        playerInitiatedAttack = false;
                        foreach(Ped guard in guards)
                        {
                            if(guard.HasBeenDamagedBy(Game.LocalPlayer.Character))
                            {
                                playerInitiatedAttack = true;
                            }
                        }
                        if(playerInitiatedAttack)
                        {
                            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player attacked guards, force weapons free!");
                            Game.DisplaySubtitle("~r~Guard:~w~ WE'RE UNDER ATTACK!", 4000);
                            HelperFuncs.DisplayInstruction("Eliminate all ~r~threats~s~.");
                            targetBlip.DisableRoute();

                            if (guards.Count < numInitialGuards + numAddtlGuards)
                            {
                                spawnAddtlGuards();
                            }

                            while (!allGuardsSpawned)
                            {
                                GameFiber.Yield();
                            }

                            foreach (Ped guard in guards)
                            {
                                if (guard.Exists())
                                {
                                    guard.Tasks.ClearImmediately();
                                    guard.Tasks.FightAgainst(Game.LocalPlayer.Character);
                                    Blip guardBlip = new Blip(guard);
                                    guardBlip.IsFriendly = false;
                                    guardBlips.Add(guard, guardBlip);
                                }
                            }
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                    Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "FYI: Ped monitor died. Exception: " + e.ToString());
                }
            });
        }

        private void spawnAddtlGuards()
        {
            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Spawning additional guards");
            for (int i = 0; i < numAddtlGuards / 2; i++)
            {
                Ped guard = HelperFuncs.CreateArmedGangster(guardSpwnLocs[chosenSpawnIdx][1], 0f);
                guards.Add(guard);
                guard.RelationshipGroup = Main.gangGroup;
                sendGuardToLocAndAim(guard, guardSpwnLocs[chosenSpawnIdx][2]);

            }
            for (int i = 0; i < numAddtlGuards / 2 + numAddtlGuards % 2; i++)
            {
                Ped guard = HelperFuncs.CreateArmedGangster(guardSpwnLocs[chosenSpawnIdx][1], 0f);
                guards.Add(guard);
                guard.RelationshipGroup = Main.gangGroup;
                sendGuardToLocAndAim(guard, guardSpwnLocs[chosenSpawnIdx][3]);

            }
            for(int i = 0; i < numAddtlGuards*guardBackupArmyMult[chosenSpawnIdx]*(int)Math.Floor((double)Main.Difficulty / 2); i++)
            {
                Ped guard = HelperFuncs.CreateArmedGangster(guardSpwnLocs[chosenSpawnIdx][1], 0f);
                guards.Add(guard);
                guard.RelationshipGroup = Main.gangGroup;
            }
            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "All guards spawned.");
            allGuardsSpawned = true;
        }

        private void sendGuardToLocAndAim(Ped guard, Vector3 targetLoc)
        {
            GameFiber.StartNew(delegate
            {
                try
                {
                    while(!allGuardsSpawned)
                    {
                        GameFiber.Yield();
                    }
                    if (!playerInitiatedAttack && !guardsInitiatedAttack)
                    {
                        guard.Tasks.GoToWhileAiming(targetLoc, Game.LocalPlayer.Character, 0f, 15f, false, FiringPattern.FullAutomatic).WaitForCompletion();
                        guard.Tasks.AimWeaponAt(Game.LocalPlayer.Character, 5000000);
                    }
                } catch (Exception e)
                {
                    Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Controlling additional guards failed. Exception: " + e.ToString());
                }
            });
        }

        private void initiateSituation()
        {
            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Initating situation...");
            calloutRunning = true;
            guardsInitiatedAttack = false;
            monitorPlayerAttack();
            GameFiber.StartNew(delegate
            {
                try
                {
                    bool initialSetup = false;
                    // Wait for player approach and keypress
                    while (calloutRunning && !playerInitiatedAttack)
                    {
                        GameFiber.Yield();
                        if (Game.LocalPlayer.Character.Position.DistanceTo(psblSpwnPts[chosenSpawnIdx]) <= SITUATION_ACTIVATE_THRESHOLD)
                        {
                            if (!initialSetup)
                            {
                                // Spawn initial guards
                                for (int i = 0; i < numInitialGuards; i++)
                                {
                                    guards.Add(HelperFuncs.CreateArmedGangster(guardSpwnLocs[chosenSpawnIdx][0], 90f));
                                }
                                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Initial guards have been placed");

                                // Animate initial guards
                                foreach (Ped guard in guards)
                                {
                                    guard.RelationshipGroup = Main.gangGroup;
                                    guard.Face(Game.LocalPlayer.Character);
                                }
                                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Situation setup successfully.");

                                initialSetup = true;
                            }
                            if (Game.LocalPlayer.Character.Position.DistanceTo(psblSpwnPts[chosenSpawnIdx]) > DIALOG_RADIUS_THRESHOLD) {
                                HelperFuncs.DisplayInstruction("Approach the ~r~guards~w~ and ask to enter.");
                            } else {
                                foreach (Ped guard in guards)
                                {
                                    guard.Face(Game.LocalPlayer.Character);
                                }
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
                    // Back-and-forth dialog & extra guards
                    while (calloutRunning && !playerInitiatedAttack)
                    {
                        GameFiber.Yield();

                        spawnAddtlGuards();

                        Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Triggering dialog.");
                        targetBlip.DisableRoute();

                        // Initiate dialog
                        int roll = rnd.Next(0,dialogPossibilities[0].Length);
                        Game.DisplaySubtitle("~r~Guard~s~: " + dialogPossibilities[0][roll], Main.DELAY_BETWEEN_DIALOG);
                        GameFiber.Sleep(Main.DELAY_BETWEEN_DIALOG);
                        Game.DisplaySubtitle("~b~You~s~: " + dialogPlayer[0], Main.DELAY_BETWEEN_DIALOG);
                        GameFiber.Sleep(Main.DELAY_BETWEEN_DIALOG);
                        roll = rnd.Next(0, dialogPossibilities[1].Length);
                        Game.DisplaySubtitle("~r~Guard~s~: " + dialogPossibilities[1][roll], Main.DELAY_BETWEEN_DIALOG);
                        GameFiber.Sleep(Main.DELAY_BETWEEN_DIALOG);
                        Game.DisplaySubtitle("~b~You~s~: " + dialogPlayer[1], Main.DELAY_BETWEEN_DIALOG);
                        GameFiber.Sleep(Main.DELAY_BETWEEN_DIALOG);
                        roll = rnd.Next(0, dialogPossibilities[2].Length);
                        Game.DisplaySubtitle("~r~Guard~s~: " + dialogPossibilities[2][roll], Main.DELAY_BETWEEN_DIALOG);

                        Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Dialog complete, waiting for player to make a decision...");
                        break;
                    }

                    // Weapons free
                    while (calloutRunning && !playerInitiatedAttack)
                    {
                        GameFiber.Yield();
                        HelperFuncs.DisplayInstruction("Attempt to ~y~enter~s~, ~r~attack~s~, or ~g~walk away~s~ and abandon the callout.");
                        if (Game.LocalPlayer.Character.Position.DistanceTo(psblSpwnPts[chosenSpawnIdx]) <= ATTACK_RADIUS_THRESHOLD)
                        {
                            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player attempting to enter. Weapons free!");
                            HelperFuncs.DisplayInstruction("Eliminate all ~r~threats~s~.");
                            guardsInitiatedAttack = true;
                            while (!allGuardsSpawned)
                            {
                                GameFiber.Yield();
                            }
                            foreach (Ped guard in guards)
                            {
                                if (guard.Exists())
                                {
                                    guard.Tasks.ClearImmediately();
                                    guard.Tasks.FightAgainst(Game.LocalPlayer.Character);
                                    Blip guardBlip = new Blip(guard);
                                    guardBlip.IsFriendly = false;
                                    guardBlips.Add(guard, guardBlip);
                                }
                            }
                            break;
                        } else if (Game.LocalPlayer.Character.Position.DistanceTo(psblSpwnPts[chosenSpawnIdx]) > SITUATION_ACTIVATE_THRESHOLD)
                        {
                            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player gave up and walked away. Ending callout...");
                            HelperFuncs.DisplayInstruction("~r~You lied~s~ and told dispatch Code 4, no drugs found.");
                            HelperFuncs.ChangeDifficulty(true);
                            End();
                            break;
                        }
                    }

                    // Wait for all guards dead or arrested
                    while (calloutRunning)
                    {
                        GameFiber.Yield();
                        bool guardsLeft = false;
                        foreach (Ped guard in guards)
                        {
                            if (!guard.Exists() || !guard.IsAlive || guard.IsCuffed)
                            {
                                if(guardBlips.ContainsKey(guard) && guardBlips[guard].Exists()) guardBlips[guard].Delete();
                                if(guard.Exists()) guard.Dismiss();
                            }
                            else
                            {
                                guardsLeft = true;
                            }
                        }
                        if (!guardsLeft)
                        {
                            break;
                        }
                        HelperFuncs.DisplayInstruction("Eliminate all ~r~threats~s~.");
                    }

                    // Spawn forensics and send en route
                    while (calloutRunning)
                    {
                        GameFiber.Yield();
                        Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "All guards have been subdued. Dispatching forensics...");
                        GameFiber.Sleep(2000);
                        HelperFuncs.DisplayInstruction("Crime Scene Investigation is en route. ~r~Secure the area~s~ until evidence gathering is completed and~r~ ensure the path is clear~s~ for the CSI truck.");

                        forensics = new Ped("S_M_M_DOCTOR_01", psblSpwnPts[chosenSpawnIdx].Around(FORENSICS_SPAWN_DISTANCE), 0);
                        forensics.IsPersistent = true;
                        forensics.BlockPermanentEvents = true;
                        forensics.KeepTasks = true;
                        forensics.RelationshipGroup = RelationshipGroup.Medic;
                        forensics2 = new Ped("S_M_M_DOCTOR_01", psblSpwnPts[chosenSpawnIdx].Around(FORENSICS_SPAWN_DISTANCE), 0);
                        forensics2.IsPersistent = true;
                        forensics2.BlockPermanentEvents = true;
                        forensics2.KeepTasks = true;
                        forensics2.RelationshipGroup = RelationshipGroup.Medic;
                        forensicsVhc = new Vehicle("POLICET",  World.GetNextPositionOnStreet(psblSpwnPts[chosenSpawnIdx].Around(FORENSICS_SPAWN_DISTANCE)), 0);
                        forensicsVhc.IsPersistent = true;
                        forensicsVhc.PrimaryColor = Color.White;
                        forensicsVhc.SecondaryColor = Color.White;
                        var elights = forensicsVhc.EmergencyLighting.Clone();
                        elights.Lights.ToList().ForEach(l => l.Light = true);
                        forensicsVhc.EmergencyLightingOverride = elights;
                        forensicsVhc.IsSirenOn = true;
                        forensicBlip = new Blip(forensics);
                        forensicBlip.Color = Color.White;
                        forensics.WarpIntoVehicle(forensicsVhc, -1);
                        forensics2.WarpIntoVehicle(forensicsVhc, 0);
                        forensics.Tasks.DriveToPosition(psblSpwnPts[chosenSpawnIdx].Around(5f), 15f, VehicleDrivingFlags.Emergency);

                        if(!forensics.Exists() || !forensics2.Exists() || !forensicsVhc.Exists())
                        {
                            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Couldn't create forensics guy and/or vehicle with spawn point: " + chosenSpawnIdx + " and distance: " + FORENSICS_SPAWN_DISTANCE);
                            HelperFuncs.DisplayInstruction("Error spawning CSI. Notify Phyvolt with this bug. Dismissed!");
                            End();
                            break;
                        }

                        Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Forensics spawned. Currently at: " + forensics.Position.X + ", " + forensics.Position.Y + ", " + forensics.Position.Z);

                        HelperFuncs.DispatchCartelBackup(psblSpwnPts[chosenSpawnIdx], ref backup, ref backupVhc, ref guardBlips);

                        break;
                    }

                    // Dispatch forensics
                    while (calloutRunning)
                    {
                        GameFiber.Yield();

                        if(forensics.Position.DistanceTo(psblSpwnPts[chosenSpawnIdx]) <= 35f)
                        {
                            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Forensics arrived.");
                            timerBarPool = new TimerBarPool();
                            timerBar = new TextTimerBar("KEEP AREA SECURE", "00:00");
                            timerBar.TextStyle = timerBar.TextStyle.With(font: TextFont.ChaletLondonFixedWidthNumbers);
                            timerBarPool.Add(timerBar);
                            targetTimeForensics = TimeSpan.FromMilliseconds(Game.GameTime + secureAreaTime);
                            timerBar.Text = TimeSpan.FromMilliseconds((targetTimeForensics.TotalMilliseconds - Game.GameTime)).ToString(@"mm\:ss");
                            forensics.Tasks.Clear();
                            forensics.Tasks.LeaveVehicle(forensicsVhc, LeaveVehicleFlags.None);
                            forensics2.Tasks.Clear();
                            forensics2.Tasks.LeaveVehicle(forensicsVhc, LeaveVehicleFlags.None);
                            HelperFuncs.DisplayInstruction("CSI has arrived. ~r~Make sure~s~ no civilian is contaminating the area and~r~ clear the path~s~ for CSI.");
                            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Switching forensic task to take notes");
                            forensics.Tasks.PlayAnimation("amb@code_human_wander_clipboard@male@base", "base", 1.0f, AnimationFlags.Loop);
                            forensics2.Tasks.Wander();
                            break;
                        }
                        if(!forensics.Exists() || !forensics2.Exists() || !forensics.IsAlive || !forensics2.IsAlive)
                        {
                            Game.DisplayNotification("Crime Investigators were ~r~killed in action~s~ on route to the destination!");
                            break;
                        }

                        foreach (Ped guard in backup)
                        {
                            if (!guard.Exists() || !guard.IsAlive || guard.IsCuffed)
                            {
                                if (guardBlips.ContainsKey(guard) && guardBlips[guard].Exists()) guardBlips[guard].Delete();
                                if (guard.Exists()) guard.Dismiss();
                            }
                        }

                        HelperFuncs.DisplayInstruction("Crime Scene Investigation is en route. ~r~Secure the area~s~ until evidence gathering is completed and~r~ ensure the path is clear~s~ for the CSI truck.");
                    }

                    // Wait for timer & ensure player is guarding location
                    ushort forensicsState = 0;
                    bool forensicsDone = false;
                    int lastSwitchTime = 0;
                    while (calloutRunning)
                    {
                        GameFiber.Yield();

                        if (!forensics.Exists() || !forensics2.Exists() || !forensics.IsAlive || !forensics2.IsAlive)
                        {
                            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "CSI died, ending callout!");
                            GameFiber.Sleep(1000);
                            HelperFuncs.DisplayInstruction("~r~Mission failed~s~. CSI has been KIA. The National Guard is on the way to lock down the location. You are dismissed.");
                            HelperFuncs.ChangeDifficulty(true);
                            End();
                            break;
                        }

                        timerBar.Text = TimeSpan.FromMilliseconds((targetTimeForensics.TotalMilliseconds - Game.GameTime)).ToString(@"mm\:ss");
                        timerBarPool.Draw();

                        int timeInSeconds = (int)Math.Round(Game.GameTime / (double)1000);
                        if (timeInSeconds % SWITCH_ANIMATION_TIME_INVESTIGATE == 0 && lastSwitchTime != timeInSeconds)
                        {
                            lastSwitchTime = timeInSeconds;
                            if (forensicsState == 0)
                            {
                                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Switching forensic task to wander");
                                forensics.Tasks.Clear();
                                forensics.Tasks.Wander();
                                forensics2.Tasks.Clear();
                                forensics2.Tasks.StandStill(5000);
                                forensics2.Tasks.PlayAnimation("amb@code_human_wander_clipboard@male@idle_a", "idla_a", 1.0f, AnimationFlags.Loop);
                                forensicsState = 1;
                            }
                            else if(forensicsState == 1)
                            {
                                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Switching forensic task to take notes");
                                forensics.Tasks.Clear();
                                forensics.Tasks.StandStill(5000);
                                forensics.Tasks.PlayAnimation("amb@code_human_wander_clipboard@male@idle_a", "idle_a", 1.0f, AnimationFlags.Loop);
                                forensics2.Tasks.Clear();
                                forensics2.Tasks.FollowNavigationMeshToPosition(psblSpwnPts[chosenSpawnIdx], 0f, forensics2.DistanceTo(psblSpwnPts[chosenSpawnIdx]) > 20f ? 2f : 1f, 10f);
                                forensicsState = 2;
                            } 
                            else if(forensicsState == 2)
                            {
                                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Switching forensic task to return to scene");
                                forensics.Tasks.Clear();
                                forensics.Tasks.FollowNavigationMeshToPosition(psblSpwnPts[chosenSpawnIdx], 0f, forensics.DistanceTo(psblSpwnPts[chosenSpawnIdx]) > 20f ? 2f : 1f, 10f);
                                forensics2.Tasks.Clear();
                                forensics2.Tasks.PlayAnimation("amb@code_human_police_investigate@idle_a", "idle_a", 1.0f, AnimationFlags.Loop);
                                forensicsState = 3;
                            }
                            else if (forensicsState == 3)
                            {
                                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Switching forensic task to investigate");
                                forensics.Tasks.Clear();
                                forensics.Tasks.PlayAnimation("amb@code_human_police_investigate@idle_a", "idle_a", 1.0f, AnimationFlags.Loop);
                                forensics2.Tasks.Clear();
                                forensics2.Tasks.Wander();
                                forensicsState = 0;
                            }
                        }

                        if ((targetTimeForensics - TimeSpan.FromMilliseconds(Game.GameTime)) <= TimeSpan.FromMilliseconds(5000) && !forensicsDone)
                        {
                            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Forensics is complete.");
                            forensics2.Tasks.Clear();
                            forensics2.Tasks.EnterVehicle(forensicsVhc, 0, 10f).WaitForCompletion();
                            forensics.Tasks.Clear();
                            forensics.Tasks.EnterVehicle(forensicsVhc, -1, 10f).WaitForCompletion();
                            forensics.Tasks.DriveToPosition(forensicsVhc, psblSpwnPts[chosenSpawnIdx].Around(500f), 15f, VehicleDrivingFlags.Normal, 500f);
                            forensicsDone = true;
                        }
                        if ((targetTimeForensics - TimeSpan.FromMilliseconds(Game.GameTime)) <= TimeSpan.Zero)
                        {
                            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player resolved callout!");
                            GameFiber.Sleep(1000);
                            HelperFuncs.DisplayInstruction("~g~All clear from CSI~s~. DEA will handle the rest. You are dismissed.");
                            HelperFuncs.ChangeDifficulty(false);
                            End();
                            break;
                        }
                        foreach (Ped guard in backup)
                        {
                            if (!guard.Exists() || !guard.IsAlive || guard.IsCuffed)
                            {
                                if (guardBlips.ContainsKey(guard) && guardBlips[guard].Exists()) guardBlips[guard].Delete();
                                if (guard.Exists()) guard.Dismiss();
                            }
                        }
                        HelperFuncs.DisplayInstruction("CSI is investigating. ~r~Make sure~s~ no civilian is contaminating the area. and~r~ clear the path~s~ for CSI");
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
                        HelperFuncs.DisplayInstruction((Main.PLUGIN_FULL_NAME + " had a critical error but was handled gracefully. Send Phyvolt your log to investigate."));
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
                foreach(Ped guard in guards)
                {
                    if (guard.Exists())
                    {
                        if(guardBlips.ContainsKey(guard) && guardBlips[guard].Exists())
                        {
                            guardBlips[guard].Delete();
                        }
                        guard.Dismiss();
                    }
                }
                if (targetBlip.Exists()) targetBlip.Delete();
                if (forensicBlip.Exists()) forensicBlip.Delete();
                if (forensics.Exists()) forensics.Dismiss();
                if (forensics2.Exists()) forensics.Dismiss();
                if (forensicsVhc.Exists()) forensicsVhc.Dismiss();
                foreach (var susBackupPed in backup)
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
                foreach (var susVhc in backupVhc)
                {
                    if (susVhc.Exists()) susVhc.Dismiss();
                }
                backup.Clear();
                backupVhc.Clear();
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
