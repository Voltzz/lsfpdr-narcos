using Rage;
using Rage.Native;
using System.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using LSPD_First_Response.Engine;

namespace narcos
{
    internal class HelperFuncs
    {
        private static string[] gangsterModels =
        {
            "g_m_y_mexgang_01", "g_m_y_mexgoon_01", "g_m_y_mexgoon_02", "g_m_y_mexgoon_03", "a_m_y_mexthug_01", "ig_ramp_hic",
            "ig_ramp_mex", "g_m_y_salvagoon_01", "g_m_y_salvagoon_03", "g_m_y_armgoon_02"
        };
        private static float MINIMUM_DISTANCE_SPAWN = 0.2f;
        private static Random rnd = new Random(MathHelper.GetRandomInteger(100, 100000));
        private static string lastInstructionGiven = "";
        private static uint lastInstructionTime = 0;
        private static uint lastInstructionId = 0;

        public static Vector3 FindBestPositionForPed(Vector3 refPosition, float maxDistance, bool anyFlatGround = true)
        {
            Vector3 intendedPos = Vector3.Zero;
            float shrinkDistBy = 0f;
            Vector3 iterativePos;
            float distanceSearchDelta = maxDistance / 10;
            while ((maxDistance - shrinkDistBy) >= MINIMUM_DISTANCE_SPAWN && intendedPos == Vector3.Zero)
            {
                iterativePos = World.GetNextPositionOnStreet(refPosition.Around(maxDistance - shrinkDistBy));
                if (NativeFunction.Natives.GET_SAFE_COORD_FOR_PED<bool>(iterativePos.X, iterativePos.Y, iterativePos.Z, true, out intendedPos, anyFlatGround ? 16 : 1))
                {
                    break;
                }
                else
                {
                    shrinkDistBy += distanceSearchDelta;
                }
            }
            return intendedPos;
        }

        public static Ped CreateArmedGangster(Vector3 location, float heading)
        {
            Ped gangster = new Ped(GetRandomMexModel(), location, heading);
            gangster.Inventory.GiveNewWeapon("WEAPON_PISTOL", (short)(20 * Main.Difficulty), true);
            gangster.Inventory.GiveNewWeapon("WEAPON_ASSAULTRIFLE", (short)(20 * Main.Difficulty), true);
            gangster.Metadata.searchPed = "~y~Knife~s~, ~s~Wallet~s~, ~y~Disposable Cellphone~s~";
            gangster.Armor = 50;
            if (Main.Difficulty > 6)
            {
                gangster.Armor = 100;
            }
            gangster.IsPersistent = true;
            gangster.BlockPermanentEvents = true;
            gangster.KeepTasks = true;
            gangster.RelationshipGroup = Main.gangGroup;
            gangster.BlockPermanentEvents = true;
            return gangster;
        }

        public static void BackupFightTaskActivate(Ped driver, Ped support, Vehicle veh)
        {
            GameFiber.StartNew(delegate
            {
                try
                {
                    bool taskActivated = false;
                    while(!taskActivated)
                    {
                        GameFiber.Yield();
                        if (Game.LocalPlayer.Character.Position.DistanceTo(driver.Position) <= 50f)
                        {
                            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Backup arrived, fighting player...");
                            driver.Tasks.ClearImmediately();
                            driver.Tasks.FightAgainst(Game.LocalPlayer.Character);
                            support.Tasks.FightAgainst(Game.LocalPlayer.Character);
                            taskActivated = true;
                        } else if((driver.Tasks.CurrentTaskStatus != TaskStatus.Preparing && driver.Tasks.CurrentTaskStatus != TaskStatus.Preparing) || driver.IsStill)
                        {
                            driver.Tasks.ClearImmediately();
                            support.Tasks.ClearImmediately();
                            driver.WarpIntoVehicle(veh, -1);
                            support.WarpIntoVehicle(veh, 0);
                            driver.Tasks.DriveToPosition(Game.LocalPlayer.Character.Position, 55f, VehicleDrivingFlags.None);
                        }
                    }

                }
                catch (System.Threading.ThreadAbortException e)
                {
                    Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Received force end from LSPDFR, abandoning backup gangster tasks...");
                }
                catch (Exception e)
                {
                    Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + e.ToString());
                    Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Error while trying to route gangsters to player, probably because the callout was resolved early.");
                }
            });
        }

        public static void DispatchCartelBackup(Vector3 spawnPoint, ref List<Ped> susPedList, ref List<Vehicle> susVhcList, ref Dictionary<Ped, Blip> guardBlips, short difficultyThreshold=2)
        {
            short adjustedDifficulty = (short)Math.Floor((double)((Main.Difficulty - difficultyThreshold) / 2));
            for (int i = 0; i < adjustedDifficulty; i++)
            {
                Ped backupPed = CreateArmedGangster(spawnPoint.Around(500f), 0);
                Ped backupPed2 = CreateArmedGangster(spawnPoint.Around(500f), 0);
                Vehicle backupVhc = new Vehicle("CAVALCADE2", World.GetNextPositionOnStreet(spawnPoint.Around(500f)), 0);
                backupVhc.IsPersistent = true;
                backupVhc.PrimaryColor = Color.Black;
                backupVhc.SecondaryColor = Color.Black;
                backupPed.WarpIntoVehicle(backupVhc, -1);
                backupPed2.WarpIntoVehicle(backupVhc, 0);
                backupPed.Tasks.DriveToPosition(spawnPoint.Around(5f), 55f, VehicleDrivingFlags.None);
                backupPed.RelationshipGroup = Main.gangGroup;
                backupPed2.RelationshipGroup = Main.gangGroup;
                backupPed.KeepTasks = true;
                backupPed2.KeepTasks = true;
                backupPed.BlockPermanentEvents = true;
                backupPed2.BlockPermanentEvents = true;
                susPedList.Add(backupPed);
                susPedList.Add(backupPed2);
                susVhcList.Add(backupVhc);
                Blip backupBlip = new Blip(backupPed);
                backupBlip.IsFriendly = false;
                guardBlips.Add(backupPed, backupBlip);
                Blip backupBlip2 = new Blip(backupPed2);
                backupBlip2.IsFriendly = false;
                guardBlips.Add(backupPed2, backupBlip2);
                BackupFightTaskActivate(backupPed, backupPed2, backupVhc);
            }
            if (adjustedDifficulty > 0)
            {
                Game.DisplayNotification("~r~The Cartel has sent backup to assist.");
            }
        }

        public static Model GetRandomMexModel()
        {
            return gangsterModels[rnd.Next(0, gangsterModels.Length)];
        }

        public static void DisplayInstruction(string instruction)
        {
            if(instruction == lastInstructionGiven)
            {
                if((Game.GameTime - lastInstructionTime) > Main.INSTRUCTIONS_REMINDER_TIME)
                {
                    lastInstructionTime = Game.GameTime;
                    if (lastInstructionId != 0) Game.RemoveNotification(lastInstructionId);
                    lastInstructionId = Game.DisplayNotification(instruction);
                }
            } else
            {
                if(lastInstructionId != 0) Game.RemoveNotification(lastInstructionId);
                lastInstructionGiven = instruction;
                lastInstructionTime = Game.GameTime;
                lastInstructionId = Game.DisplayNotification(instruction);
            }
        }

        public static void ChangeDifficulty(bool increase)
        {
            if(!Main.dynamicWorld)
            {
                Game.LogTrivial("Living Cartel is not enabled. Not changing difficulty");
                return;
            }
            Game.LogTrivial("Difficulty was changed from: " + Main.Difficulty.ToString());
            if (increase)
            {
                Main.Difficulty++;
                Game.DisplayNotification("The Cartel has gotten ~r~stronger~s~.");
            }
            else
            {
                Main.Difficulty--;
                Game.DisplayNotification("The Cartel has gotten ~g~weaker~s~.");
            }

            if(Main.Difficulty > 10)
            {
                Main.Difficulty = 10;
            }

            if(Main.Difficulty < 2)
            {
                Main.Difficulty = 2;
            }
            if(Main.persistDifficulty)
            {
                Main.databaseFile.Write("difficulty", Main.Difficulty.ToString(), "gamesaves");
            }
            Game.LogTrivial("Difficulty was changed to: " + Main.Difficulty.ToString());
        }

        public static string RandomString(short letterLength, short numberLength)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            const string numbs = "0123456789";
            var letterPortion = new string(Enumerable.Repeat(chars, letterLength)
              .Select(s => s[rnd.Next(s.Length)]).ToArray());
            var numberPortion = new string(Enumerable.Repeat(numbs, numberLength)
              .Select(s => s[rnd.Next(s.Length)]).ToArray());
            return letterPortion + numberPortion;
        }
    }

    public class SettingsIniFile
    {
        string Path;
        string IniPath = @".\Plugins\LSPDFR\Narcos\"+Assembly.GetExecutingAssembly().GetName().Name;

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern long WritePrivateProfileString(string Section, string Key, string Value, string FilePath);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern int GetPrivateProfileString(string Section, string Key, string Default, StringBuilder RetVal, int Size, string FilePath);

        public SettingsIniFile()
        {
            Path = new FileInfo(IniPath+".ini").FullName;
            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Loading ini file from: " + Path);
        }

        public string Read(string Key, string Section = null)
        {
            var RetVal = new StringBuilder(255);
            GetPrivateProfileString(Section ?? IniPath, Key, "", RetVal, 255, Path);
            return RetVal.ToString();
        }

        public void Write(string Key, string Value, string Section = null)
        {
            WritePrivateProfileString(Section ?? IniPath, Key, Value, Path);
        }

        public void DeleteKey(string Key, string Section = null)
        {
            Write(Key, null, Section ?? IniPath);
        }
    }

    public class DatabaseFile
    {
        string Path;
        string DataPath = @".\Plugins\LSPDFR\Narcos\data";

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern long WritePrivateProfileString(string Section, string Key, string Value, string FilePath);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern int GetPrivateProfileString(string Section, string Key, string Default, StringBuilder RetVal, int Size, string FilePath);

        public DatabaseFile()
        {
            Path = new FileInfo(DataPath + ".db").FullName;
            Game.LogTrivial(Main.DEBUG_OUTPUT_PREFIX + "Loading data file from: " + Path);
        }

        public string Read(string Key, string Section = null)
        {
            var RetVal = new StringBuilder(255);
            GetPrivateProfileString(Section ?? DataPath, Key, "", RetVal, 255, Path);
            return RetVal.ToString();
        }

        public void Write(string Key, string Value, string Section = null)
        {
            WritePrivateProfileString(Section ?? DataPath, Key, Value, Path);
        }

        public void DeleteKey(string Key, string Section = null)
        {
            Write(Key, null, Section ?? DataPath);
        }
    }
}
