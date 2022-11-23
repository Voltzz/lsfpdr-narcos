using System;
using System.Drawing;
using Rage;
using LSPD_First_Response.Mod.Callouts;
using LSPD_First_Response.Mod.API;
using System.Linq;
using System.Collections.Generic;

namespace narcos.Callouts
{
    [CalloutInfo("[N*A*R*C*O*S] Drug Shipment", CalloutProbability.Medium)]

    public class DrugShipment : Callout
    {
        private static string[] psblCargoHolder =
        {
            // Either "plane" or "container" or "none"
            "plane",
            "container",
            "none",
            "container",
        };
        private static Vector3[][] psblSpawnLocs =
        {
            // 0th location is where plane or container will spawn, Zero if not needed
            // 1st location is where cars will spawn
            // 2nd location is the heading direction for all cars
            // 3rd location is where people will spawn
            new Vector3[] { new Vector3(1554.217f, 3156.719f, 39.65f), new Vector3(1574.626f, 3177.344f, 39.65f), new Vector3(1581.633f, 3184.44f, 39.65f), new Vector3(1585.925f, 3167.97f, 39.65f) }, // Dirt airfield
            new Vector3[] { Vector3.Zero, new Vector3(2544.395f, 4213.384f, 39.68443f), new Vector3(2715.468f, 3315.244f, 55.05704f), new Vector3(2529.618f, 4211.344f, 39.22934f) }, // aa
            new Vector3[] { Vector3.Zero, new Vector3(1651.837f, 6413.189f, 28.593f), new Vector3(2715.468f, 3315.244f, 55.05704f), new Vector3(1674.344f, 6407.127f, 29.86045f) }, // aa
            new Vector3[] { Vector3.Zero, new Vector3(-2609.172f, 2314.892f, 27.48362f), new Vector3(2715.468f, 3315.244f, 55.05704f), new Vector3(-2584.16f, 2321.606f, 32.07143f) }, // aa
        };
        private static uint[][] psblHeadings =
        {
            new uint[] { 0, 0 },
            new uint[] { 1, 1 },
            new uint[] { 2, 2 },
            new uint[] { 2, 2 },
        };
        private static string[][] dialogPossibilities =
        {
            // Odd numbers are player, even are customs (assuming 1-index)
            new string[] {"What's the situation?", "What's going on?", "Do we have a problem here?" },
            new string[] {"These gentlemen are refusing an inspection", "We can't let them leave until we search their cargo", "They are not allowing us to inspect their cargo"},
            new string[] {"I'll take care of it", "I'll take a look, you standby", "Alright, I'm going to check it out", "Let me handle this"}
        };
        private string[][] possibleAnimations =
        {
            new string[] { "amb@world_human_hang_out_street@male_c@idle_a", "idle_a" },
            new string[] { "amb@world_human_hang_out_street@male_c@idle_b", "idle_d" },
            new string[] { "amb@world_human_hang_out_street@male_a@idle_a", "idle_a" },
            new string[] { "amb@world_human_drug_dealer_hard@male@idle_a", "idle_a" },
            new string[] { "amb@world_human_drug_dealer_hard@male@idle_b", "idle_d" },
        };
        private static string CALLOUT_SHORTNAME = "DrugShipment";
        private static float SITUATION_ACTIVATE_THRESHOLD = 250f;
        private static float DIALOG_RADIUS_THRESHOLD = 5f;
        private static float RUN_DISTANCE_THRESHOLD = 4f;
        private static float APPROX_CAR_WIDTH = 4f; // to stack cars side by side

        private int chosenSpawnIdx;
        private bool calloutRunning;
        private Blip targetBlip;
        private bool targetHasDrugs;
        private List<Ped> suspects;
        private List<Vehicle> susVhcs;
        private Ped customsOfficer;
        private Entity cargoHolder;
        private int numCars;
        private static LHandle activePursuit;
        private List<Ped> backup;
        private List<Vehicle> backupVhc;
        private bool playerInitiatedAttack;
        private bool autoInitiatedPursuit;
        private bool allSusInVhc;
        private Dictionary<Ped, Blip> guardBlips;


        private static Random rnd = new Random(MathHelper.GetRandomInteger(100, 100000));

        public override bool OnBeforeCalloutDisplayed()
        {
            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Initiating " + CALLOUT_SHORTNAME + " callout...");

            // Figure out where to spawn callout
            chosenSpawnIdx = rnd.Next(0, psblSpawnLocs.Length);
            chosenSpawnIdx = 0;
            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Chose location #" + chosenSpawnIdx + ": (" + psblSpawnLocs[chosenSpawnIdx][0].X + ", " + psblSpawnLocs[chosenSpawnIdx][0].Y + ", " + psblSpawnLocs[chosenSpawnIdx][0].Z + ")");

            calloutRunning = false;
            targetHasDrugs = false;
            autoInitiatedPursuit = false;
            activePursuit = null;
            suspects = new List<Ped>();
            susVhcs = new List<Vehicle>();
            numCars = int.Parse(Main.settingsFile.Read("numCars", "DrugShipment"));
            allSusInVhc = false;
            backup = new List<Ped>();
            backupVhc = new List<Vehicle>();
            guardBlips = new Dictionary<Ped, Blip>();

            // Create callout for user
            this.ShowCalloutAreaBlipBeforeAccepting(psblSpawnLocs[chosenSpawnIdx][0], 40f);
            this.AddMinimumDistanceCheck(50f, psblSpawnLocs[chosenSpawnIdx][0]);
            this.CalloutMessage = "Possible Drug Shipment";
            this.CalloutPosition = psblSpawnLocs[chosenSpawnIdx][0];

            // Display callout
            Functions.PlayScannerAudioUsingPosition("WE_HAVE_01 CRIME_RESIST_ARREST_01 IN_OR_ON_POSITION", psblSpawnLocs[chosenSpawnIdx][0]);

            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Callout fully initiated!");

            return base.OnBeforeCalloutDisplayed();
        }

        public override bool OnCalloutAccepted()
        {
            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player accepted callout!");

            // Show area and instructions
            targetBlip = new Blip(psblSpawnLocs[chosenSpawnIdx][0], 30f);
            targetBlip.Color = Color.Yellow;
            targetBlip.Alpha = 0.7f;
            targetBlip.EnableRoute(Color.Yellow);

            Game.DisplayNotification("3dtextures", "mpgroundlogo_cops", "Drug Shipment", "~y~Investigate", "Customs report a shipment refusing inspection. Drugs may be present.~n~~n~Investigate & take appropriate action.");
            Functions.PlayScannerAudio("REPORT_RESPONSE_COPY_03 UNITS_RESPOND_CODE_02_01");

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
                    while (calloutRunning && !playerInitiatedAttack && !autoInitiatedPursuit)
                    {
                        GameFiber.Yield();
                        playerInitiatedAttack = false;
                        if (Game.LocalPlayer.Character.IsShooting)
                        {
                            playerInitiatedAttack = true;
                        }
                        else
                        {
                            foreach (Ped sus in suspects)
                            {
                                if (sus.HasBeenDamagedBy(Game.LocalPlayer.Character))
                                {
                                    playerInitiatedAttack = true;
                                }
                            }
                            foreach (Vehicle vhc in susVhcs)
                            {
                                if (vhc.HasBeenDamagedBy(Game.LocalPlayer.Character))
                                {
                                    playerInitiatedAttack = true;
                                }
                            }
                        }
                        if (playerInitiatedAttack)
                        {
                            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player attacked suspects, force resolution!");
                            targetBlip.DisableRoute();

                            if (targetHasDrugs)
                            {
                                activePursuit = Functions.CreatePursuit();
                                Functions.SetPursuitAsCalledIn(activePursuit);
                                Functions.SetPursuitIsActiveForPlayer(activePursuit, true);
                                customsOfficer.Tasks.ReactAndFlee(suspects[0]);
                                sendSussesToVehicle();
                                while (!allSusInVhc)
                                {
                                    GameFiber.Yield();
                                }
                                foreach (var sus in suspects)
                                {
                                    if (!sus.Exists() || !sus.IsAlive || sus.IsCuffed) continue;
                                    Functions.AddPedToPursuit(activePursuit, sus);
                                    PedPursuitAttributes pattr = Functions.GetPedPursuitAttributes(sus);
                                    pattr.AverageFightTime = (int)Math.Floor(60 / Math.Floor((double)Main.Difficulty / 2)); // seconds
                                    pattr.AverageSurrenderTime = 600 * (int)Math.Floor((double)Main.Difficulty / 2); // seconds
                                    pattr.BurstTireMaxDrivingSpeedMult = 0.1f;
                                    pattr.BurstTireSurrenderMult = 0.05f;
                                    pattr.MaxDrivingSpeed = 100f;
                                    pattr.MinDrivingSpeed = 30f;
                                    pattr.SurrenderChanceCarBadlyDamaged = 10f;
                                    pattr.SurrenderChanceTireBurst = 5f;
                                    pattr.SurrenderChanceTireBurstAndCrashed = 10f;
                                    pattr.CanUseCars = true;
                                }
                                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Suspects are on the run!");
                                if (targetBlip.Exists()) targetBlip.Delete();
                                HelperFuncs.DispatchCartelBackup(psblSpawnLocs[chosenSpawnIdx][0], ref backup, ref backupVhc, ref guardBlips);
                            }
                            else
                            {
                                foreach (var sus in suspects)
                                {
                                    sus.Tasks.ClearImmediately();
                                    sus.Tasks.ReactAndFlee(Game.LocalPlayer.Character);
                                    Game.DisplaySubtitle("~b~Customs~s~: I'm not paid enough for this!", Main.DELAY_BETWEEN_DIALOG);
                                }
                                customsOfficer.Tasks.ReactAndFlee(Game.LocalPlayer.Character);
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

        private void sendSussesToVehicle()
        {
            if (allSusInVhc) return;
            GameFiber.StartNew(delegate
            {
                var vehicleIndex = 0;
                var taskList = new List<Task>();
                var pedToVhc = new Dictionary<Ped, Vehicle>();
                foreach (var sus in suspects)
                {
                    Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Processing vehicle assignment " + vehicleIndex + " / " + susVhcs.Count());
                    if (!sus.Exists() || !sus.IsAlive || sus.IsCuffed) continue;
                    if (!sus.IsInAnyVehicle(false))
                    {
                        Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Assigning ped #" + sus.Metadata.pedTracker + " to car #" + susVhcs[vehicleIndex].Metadata.carTracker);
                        taskList.Add(sus.Tasks.EnterVehicle(susVhcs[vehicleIndex], 0, 20f));
                        pedToVhc.Add(sus, susVhcs[vehicleIndex]);
                        vehicleIndex++;
                    }
                }
                while(taskList.Count > 0)
                {
                    GameFiber.Yield();
                    var task = taskList[0];
                    if ((task.Status == TaskStatus.None && task.Ped.IsInAnyVehicle(true)) || !task.Ped.IsAlive || !task.Ped.Exists())
                    {
                        Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Removing ped #" + task.Ped.Metadata.pedTracker + " task because they died or entered car #" + pedToVhc[task.Ped].Metadata.carTracker);
                        taskList.Remove(task);
                        continue;
                    }
                    else if (task.Status == TaskStatus.Interrupted || task.Status == TaskStatus.None)
                    {
                        Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Reassigning ped #" + task.Ped.Metadata.pedTracker + " to car #" + pedToVhc[task.Ped].Metadata.carTracker);
                        Ped peddy = task.Ped;
                        taskList.Remove(task);
                        peddy.Tasks.Clear();
                        if(pedToVhc[task.Ped].Exists())
                            taskList.Add(peddy.Tasks.EnterVehicle(pedToVhc[task.Ped], 0, 20f));
                    }
                }
                allSusInVhc = true;
            });
        }

        private bool isPlayerNearAnySpecialEntities()
        {
            if(Game.LocalPlayer.Character.Position.DistanceTo(psblSpawnLocs[chosenSpawnIdx][0]) <= RUN_DISTANCE_THRESHOLD
               || Game.LocalPlayer.Character.Position.DistanceTo(psblSpawnLocs[chosenSpawnIdx][1]) <= RUN_DISTANCE_THRESHOLD)
            {
                return true;
            }
            foreach(var sus in suspects)
            {
                if (Game.LocalPlayer.Character.Position.DistanceTo(sus) <= RUN_DISTANCE_THRESHOLD) return true;
            }
            foreach(var vhc in susVhcs)
            {
                if (Game.LocalPlayer.Character.Position.DistanceTo(vhc) <= RUN_DISTANCE_THRESHOLD) return true;
            }
            return false;
        }

        private void initiateSituation()
        {
            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Initating situation...");
            calloutRunning = true;
            playerInitiatedAttack = false;
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
                        if (Game.LocalPlayer.Character.Position.DistanceTo(psblSpawnLocs[chosenSpawnIdx][0]) <= SITUATION_ACTIVATE_THRESHOLD)
                        {
                            if (!initialSetup)
                            {
                                // Spawn cars, peds, plane/container & customs
                                int roll = rnd.Next(1, 101);
                                targetHasDrugs = false;
                                if(roll <= int.Parse(Main.settingsFile.Read("drugsChance", "DrugShipment")))
                                {
                                    targetHasDrugs = true;
                                }

                                // Calculate car headings
                                Ped tempPed1 = new Ped("S_M_Y_COP_01", psblSpawnLocs[chosenSpawnIdx][1], 0);
                                Ped tempPed2 = new Ped("S_M_Y_COP_01", psblSpawnLocs[chosenSpawnIdx][2], 0);
                                tempPed1.Face(tempPed2);
                                float carHeading = tempPed1.Heading;
                                tempPed1.Delete();
                                tempPed2.Delete();

                                if (psblCargoHolder[chosenSpawnIdx] != "none")
                                {
                                    if (psblCargoHolder[chosenSpawnIdx] == "plane")
                                    {
                                        float planeHeading = (carHeading + 180 > 360) ? carHeading + 180 - 360 : carHeading + 180;
                                        cargoHolder = new Vehicle("MOGUL", psblSpawnLocs[chosenSpawnIdx][0], carHeading+180);
                                        Vehicle cargoHolderVhc = (Vehicle)cargoHolder;
                                        cargoHolder.IsPersistent = true;
                                        cargoHolderVhc.PrimaryColor = Color.White;
                                        cargoHolderVhc.SecondaryColor = Color.White;
                                        if(targetHasDrugs) cargoHolder.Metadata.searchTrunk = "~r~400 bags of cocaine~s~";
                                    }
                                    else if (psblCargoHolder[chosenSpawnIdx] == "container")
                                    {
                                        
                                    }
                                }

                                // To spawn multiple cars, we need to calculate the normal unit vector of the vector between the initial car spawn & the plane/container
                                // To do this, we first calculate the normal using basic trig rules (new_X = -y_length*sin90 + x_length*cos 90) & (new_Y = y_length*cos90 + x_length*sin90)
                                // This simplifies to just new_X = -1 * y_length and new_Y = x_length.
                                // Note that this is the DELTA already, because we assume our initial car spawn is (0,0) and we add it to the actual coordinates in the loop
                                // Once we have the new coordinates, we need to scale it to the unit vector which we do easily by dividing it by the ratio of the current distance by unit distance
                                // and scaling both X and Y coordinates by that multiplier.
                                Vector3 normalVector = new Vector3(-1 * (psblSpawnLocs[chosenSpawnIdx][0].Y - psblSpawnLocs[chosenSpawnIdx][1].Y), psblSpawnLocs[chosenSpawnIdx][0].X - psblSpawnLocs[chosenSpawnIdx][1].X, 0);
                                float scaleFactor = (float)(APPROX_CAR_WIDTH / Math.Sqrt(normalVector.X*normalVector.X+normalVector.Y*normalVector.Y));
                                Vector3 spawnDelta = new Vector3(normalVector.X * scaleFactor, normalVector.Y * scaleFactor, 0);
                                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Spawning " + numCars.ToString() + " cars with 2 peds each...");
                                int y = 0;
                                for (int i = 0; i < numCars; i++)
                                {
                                    // Cars
                                    short sideMultiplier = (short)(i % 2 == 0 ? 1 : -1); // Alternate the sides where cars go
                                    Vector3 carSpawnLocation = new Vector3(psblSpawnLocs[chosenSpawnIdx][1].X + (spawnDelta.X * (short)(Math.Ceiling((double)i / 2)) * sideMultiplier), psblSpawnLocs[chosenSpawnIdx][1].Y + (spawnDelta.Y * (short)(Math.Ceiling((double)i / 2)) * sideMultiplier), psblSpawnLocs[chosenSpawnIdx][1].Z);
                                    Vehicle susVhc = new Vehicle("CAVALCADE2", carSpawnLocation, carHeading);
                                    susVhc.IsPersistent = true;
                                    susVhc.PrimaryColor = Color.Black;
                                    susVhc.SecondaryColor = Color.Black;
                                    susVhc.Metadata.carTracker = i.ToString();
                                    if (targetHasDrugs) susVhc.Metadata.searchDriver = "A map of Los Santos";
                                    if (targetHasDrugs) susVhc.Metadata.searchPassenger = "~y~Box of 20 disposable cellphones~s~";
                                    if (targetHasDrugs) susVhc.Metadata.searchTrunk = "~r~50 bags of cocaine~s~";
                                    susVhcs.Add(susVhc);

                                    // People
                                    Ped driver = new Ped(HelperFuncs.GetRandomMexModel(), psblSpawnLocs[chosenSpawnIdx][3].Around2D(1f, 3f), 0);
                                    driver.IsPersistent = true;
                                    driver.KeepTasks = true;
                                    driver.RelationshipGroup = Main.gangGroup;
                                    driver.BlockPermanentEvents = true;
                                    driver.WarpIntoVehicle(susVhc, -1);
                                    if(targetHasDrugs) driver.Inventory.GiveNewWeapon("WEAPON_PISTOL", 40, true);
                                    if(targetHasDrugs) driver.Metadata.searchPed = "~y~Knife~s~, ~s~Wallet~s~, ~s~Disposable Cellphone~s~";
                                    Ped helper = targetHasDrugs ? HelperFuncs.CreateArmedGangster(psblSpawnLocs[chosenSpawnIdx][3].Around2D(1f, 8f), 0) : new Ped(HelperFuncs.GetRandomMexModel(), psblSpawnLocs[chosenSpawnIdx][3].Around(1f, 8f), 0);
                                    helper.Inventory.GiveNewWeapon("WEAPON_UNARMED", 1, true);
                                    roll = rnd.Next(0, possibleAnimations.Length);
                                    helper.Tasks.Clear();
                                    helper.Tasks.PlayAnimation(possibleAnimations[roll][0], possibleAnimations[roll][1], 1f, AnimationFlags.Loop);
                                    helper.Metadata.pedTracker = (i+y).ToString();
                                    y++;
                                    driver.Metadata.pedTracker = (i + y).ToString();
                                    suspects.Add(driver);
                                    suspects.Add(helper);
                                }

                                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "All cars spawned, creating customs officer...");

                                customsOfficer = new Ped("s_m_m_security_01", psblSpawnLocs[chosenSpawnIdx][1].Around2D(15f, 20f), 0);
                                customsOfficer.Tasks.Clear();
                                customsOfficer.Tasks.PlayAnimation("amb@world_human_smoking@male@male_a@idle_a", "idle_a", 1f, AnimationFlags.Loop);
                                customsOfficer.IsPersistent = true;
                                customsOfficer.KeepTasks = true;
                                customsOfficer.BlockPermanentEvents = true;
                                customsOfficer.RelationshipGroup = RelationshipGroup.Cop;
                                

                                if (!customsOfficer.Exists())
                                {
                                    Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Couldn't initiate situation: " + chosenSpawnIdx);
                                    HelperFuncs.DisplayInstruction("Error spawning creating situation. Notify Phyvolt with this bug. Dismissed!");
                                    End();
                                    break;
                                }

                                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Situation initiated.");

                                initialSetup = true;
                            }
                            if (Game.LocalPlayer.Character.Position.DistanceTo(customsOfficer) > DIALOG_RADIUS_THRESHOLD)
                            {
                                HelperFuncs.DisplayInstruction("Approach the ~g~customs officer~w~ and figure out what's going on.");
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
                    while (calloutRunning && !playerInitiatedAttack)
                    {
                        GameFiber.Yield();

                        Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Triggering dialog.");
                        targetBlip.DisableRoute();

                        // Initiate dialog
                        int roll = rnd.Next(0, dialogPossibilities[0].Length);
                        Game.DisplaySubtitle("~g~You~s~: " + dialogPossibilities[0][roll], Main.DELAY_BETWEEN_DIALOG);
                        GameFiber.Sleep(Main.DELAY_BETWEEN_DIALOG);
                        roll = rnd.Next(0, dialogPossibilities[1].Length);
                        Game.DisplaySubtitle("~b~Customs~s~: " + dialogPossibilities[1][roll], Main.DELAY_BETWEEN_DIALOG);
                        GameFiber.Sleep(Main.DELAY_BETWEEN_DIALOG);
                        roll = rnd.Next(0, dialogPossibilities[2].Length);
                        Game.DisplaySubtitle("~g~You~s~: " + dialogPossibilities[2][roll], Main.DELAY_BETWEEN_DIALOG);

                        Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Dialog complete!");
                        break;
                    }

                    // Trigger pursuit if needed
                    while (calloutRunning && !playerInitiatedAttack)
                    {
                        GameFiber.Yield();
                        HelperFuncs.DisplayInstruction("~r~Inspect all cargo~s~ and take appropriate action. Press ~y~~h~" + Main.mainInteractionKeyStr + "~h~~s~ to give customs a ~g~Code 4~s~");
                        if (isPlayerNearAnySpecialEntities())
                        {
                            if(targetHasDrugs)
                            {
                                activePursuit = Functions.CreatePursuit();
                                Functions.SetPursuitAsCalledIn(activePursuit);
                                Functions.SetPursuitIsActiveForPlayer(activePursuit, true);
                                autoInitiatedPursuit = true;
                                allSusInVhc = false;
                                customsOfficer.Tasks.ReactAndFlee(suspects[0]);
                                sendSussesToVehicle();
                                while (!allSusInVhc)
                                {
                                    GameFiber.Yield();
                                }
                                foreach (var sus in suspects)
                                {
                                    Functions.AddPedToPursuit(activePursuit, sus);
                                    PedPursuitAttributes pattr = Functions.GetPedPursuitAttributes(sus);
                                    pattr.AverageFightTime = (int)Math.Floor(60 / Math.Floor((double)Main.Difficulty / 2)); // seconds
                                    pattr.AverageSurrenderTime = 600 * (int)Math.Floor((double)Main.Difficulty / 2); // seconds
                                    pattr.BurstTireMaxDrivingSpeedMult = 0.1f;
                                    pattr.BurstTireSurrenderMult = 0.05f;
                                    pattr.MaxDrivingSpeed = 100f;
                                    pattr.MinDrivingSpeed = 30f;
                                    pattr.SurrenderChanceCarBadlyDamaged = 10f;
                                    pattr.SurrenderChanceTireBurst = 5f;
                                    pattr.SurrenderChanceTireBurstAndCrashed = 10f;
                                    pattr.CanUseCars = true;
                                }
                                Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Suspects are on the run!");
                                if (targetBlip.Exists()) targetBlip.Delete();
                                HelperFuncs.DispatchCartelBackup(psblSpawnLocs[chosenSpawnIdx][0], ref backup, ref backupVhc, ref guardBlips);
                            }
                            break;
                        }
                    }

                    var susOutstanding = new List<Ped>();
                    foreach (var sus in suspects)
                    {
                        if (targetHasDrugs && sus.Exists() && sus.IsAlive && !sus.IsCuffed)
                        {
                            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Adding sus #" + sus.Metadata.pedTracker + " to outstanding list");
                            susOutstanding.Add(sus);
                        }
                    }

                    // Check if suspect subdued or was innocent
                    while (calloutRunning)
                    {
                        GameFiber.Yield();
                        if (!targetHasDrugs) {
                            if(playerInitiatedAttack)
                            {
                                HelperFuncs.DisplayInstruction("You've escalated the situation and customs is reporting to superiors. ~r~You must leave the scene immediately~s~ and debrief with Internal Affairs.");
                                if(Game.LocalPlayer.Character.Position.DistanceTo(psblSpawnLocs[chosenSpawnIdx][0]) > SITUATION_ACTIVATE_THRESHOLD)
                                {
                                    Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player resolved callout!");
                                    GameFiber.Sleep(1000);
                                    HelperFuncs.DisplayInstruction("You've left the scene. Internal affairs will reach out soon.");
                                    GameFiber.Sleep(4000);
                                    End();
                                    break;
                                }
                            } else
                            {
                                HelperFuncs.DisplayInstruction("~r~Inspect all cargo~s~ and take appropriate action. Press ~y~~h~" + Main.mainInteractionKeyStr + "~h~~s~ to give customs a ~g~Code 4~s~");
                                if (Game.IsKeyDown(Main.mainInteractionKey))
                                {
                                    Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player resolved callout!");
                                    GameFiber.Sleep(1000);
                                    HelperFuncs.DisplayInstruction("You let the suspects go. ~g~Code 4~s~.");
                                    Game.DisplaySubtitle("~g~You~s~: We're all clear, you can let them go", Main.DELAY_BETWEEN_DIALOG);
                                    sendSussesToVehicle();
                                    while (!allSusInVhc)
                                    {
                                        GameFiber.Yield();
                                    }
                                    GameFiber.Sleep(2000);
                                    End();
                                    break;
                                }
                            }
                        }
                        else
                        {
                            HelperFuncs.DisplayInstruction("Chase & arrest the ~r~suspects~s~.");
                            GameFiber.Sleep(3000);
                            GameFiber.Yield();
                            var newSusOutstanding = new List<Ped>();
                            foreach (var susO in susOutstanding)
                            {
                                if (!susO.Exists()) newSusOutstanding.Add(susO);
                                else if (susO.IsAlive && !susO.IsCuffed) newSusOutstanding.Add(susO);
                                else
                                {
                                    Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Removing sus #" + susO.Metadata.pedTracker + " from outstanding list");
                                }
                            }
                            susOutstanding = newSusOutstanding;
                            if (activePursuit == null || !Functions.IsPursuitStillRunning(activePursuit))
                            {
                                GameFiber.Sleep(4000);
                                GameFiber.Yield();

                                if (susOutstanding.Count != 0 || Game.LocalPlayer.Character.IsDead)
                                {
                                    Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player failed at callout!");
                                    GameFiber.Sleep(2000);
                                    Game.DisplayNotification("Pursuit was lost and ~r~suspects may still be at large~s~. A BOLO has been issued in the vicinity.");
                                    HelperFuncs.ChangeDifficulty(true);
                                }
                                else
                                {
                                    Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Player resolved callout!");
                                    GameFiber.Sleep(3000);
                                    Game.DisplayNotification("~g~Code 4~s~. All suspects were subdued. No further units required.");
                                    HelperFuncs.ChangeDifficulty(false);
                                }

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
                if (customsOfficer.Exists()) customsOfficer.Dismiss();
                if(cargoHolder.Exists()) cargoHolder.Dismiss();
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
                foreach (var susPed in suspects)
                {
                    if (susPed.Exists()) susPed.Dismiss();
                }
                foreach (var susVhc in susVhcs)
                {
                    if (susVhc.Exists()) susVhc.Dismiss();
                }
                suspects.Clear();
                susVhcs.Clear();
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
