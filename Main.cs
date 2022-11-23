using Rage;
using LSPD_First_Response.Mod.API;
using narcos.Callouts;
using System.Windows.Forms;

// TODO: Turf war (multi-shooter, multi-car shootout on residential streets)
// TODO: Drug Kingpin spotted (vehicle chase with heavy defenses)
// TODO: Drug Cleanup - too many druggies in neighborhood, clean up the streets (possible confrontation)

namespace narcos
{
    public class Main : Plugin
    {
        public static string PLUGIN_FULL_NAME = "N*A*R*C*O*S";
        public static string DEBUG_OUTPUT_PREFIX = "[N*A*R*C*O*S] ";
        public static SettingsIniFile settingsFile;
        public static DatabaseFile databaseFile;
        public string uniqueId;
        public static Keys mainInteractionKey;
        public static string mainInteractionKeyStr;
        public static bool initializedCorrectly;
        public static bool dynamicWorld;
        public static bool persistDifficulty;
        public static KeysConverter kc = new KeysConverter();
        public static int INSTRUCTIONS_REMINDER_TIME = 4000; // miliseconds
        public static int DELAY_BETWEEN_DIALOG = 3000; // miliseconds
        public static short Difficulty = 2; // increases automatically if calls are denied / failed - changes happen every 2 increments
        public static RelationshipGroup gangGroup;

        public override void Initialize()
        {
            Functions.OnOnDutyStateChanged += OnOnDutyStateChangedHandler;
            settingsFile = new SettingsIniFile(); // Automatically opens the relevant file
            databaseFile = new DatabaseFile();
            initializedCorrectly = true;
            if(!IniIntegrityCheck())
            {
                Game.LogTrivial(PLUGIN_FULL_NAME + " (v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString() + ") FAILED TO INITIALIZE! Your .ini file is missing or incorrect.");
                Game.DisplayNotification("web_nationalofficeofsecurityenforcement", "web_nationalofficeofsecurityenforcement", PLUGIN_FULL_NAME, "by ~HUD_COLOUR_G5~~h~Phyvolt~s~", "Version: ~b~" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString() + " ~r~~h~FAILED~h~~s~~n~~n~Your .ini file is missing or incorrect.");
                initializedCorrectly = false;
                return;
            } else if(settingsFile.Read("Version", "PluginData") != System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString())
            {
                Game.LogTrivial(PLUGIN_FULL_NAME + " (v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString() + ") FAILED TO INITIALIZE! You updated the plugin but not the .ini file.");
                Game.DisplayNotification("web_nationalofficeofsecurityenforcement", "web_nationalofficeofsecurityenforcement", PLUGIN_FULL_NAME, "by ~HUD_COLOUR_G5~~h~Phyvolt~s~", "Version: ~b~" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString() + " ~r~~h~FAILED~h~~s~~n~~n~You updated the plugin but not the .ini file.");
                initializedCorrectly = false;
                return;
            }
            mainInteractionKeyStr = settingsFile.Read("MainInteractionKey", "General");
            Game.LogTrivial(PLUGIN_FULL_NAME + " (v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString() + ") Setting main interaction key to: " + mainInteractionKeyStr);
            dynamicWorld = settingsFile.Read("DynamicWorld", "General") == "true";
            Game.LogTrivial(PLUGIN_FULL_NAME + " (v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString() + ") Living Cartel enabled: " + dynamicWorld);

            LoadOrCreateData();
            Game.LogTrivial(PLUGIN_FULL_NAME + " (v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString() + ") Data file loaded!");
            persistDifficulty = settingsFile.Read("PersistDifficulty", "General") == "true";
            if (!persistDifficulty)
            {
                ResetDifficulty();
                Game.LogTrivial(PLUGIN_FULL_NAME + " (v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString() + ") Difficulty is NOT being persisted and has been reset.");
            }

            gangGroup = new RelationshipGroup("Mexican Cartel");

            mainInteractionKey = (Keys) kc.ConvertFromString(mainInteractionKeyStr);
            Game.LogTrivial(PLUGIN_FULL_NAME + " (v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString() + ") has been initialized.");
        }

        public override void Finally()
        {
            Game.LogTrivial(PLUGIN_FULL_NAME + " has been cleaned up.");
        }

        private static void OnOnDutyStateChangedHandler(bool OnDuty)
        {
            if (OnDuty && initializedCorrectly)
            {
                RegisterCallouts();
                Game.LogTrivial(PLUGIN_FULL_NAME + " (v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString() + ") is now ON DUTY.");
                Game.DisplayNotification("web_nationalofficeofsecurityenforcement", "web_nationalofficeofsecurityenforcement", PLUGIN_FULL_NAME, "by ~HUD_COLOUR_G5~~h~Phyvolt~s~", "Version: ~b~" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString() + " ~g~loaded~s~");
            }
        }

        private static void RegisterCallouts()
        {
            if(settingsFile.Read("DrugDeal", "CalloutsEnabled") == "true") Functions.RegisterCallout(typeof(DrugDeal));
            if(settingsFile.Read("DrugBust", "CalloutsEnabled") == "true") Functions.RegisterCallout(typeof(DrugBust));
            if(settingsFile.Read("DrugSeize", "CalloutsEnabled") == "true") Functions.RegisterCallout(typeof(DrugSeize));
            if(settingsFile.Read("DrugShipment", "CalloutsEnabled") == "true") Functions.RegisterCallout(typeof(DrugShipment));
        }

        private void LoadOrCreateData()
        {
            if (databaseFile == null || databaseFile.Read("seed_id", "internal") == "")
            {
                uniqueId = HelperFuncs.RandomString(4, 3);
                databaseFile.Write("seed_id", uniqueId, "internal");
                databaseFile.Write("difficulty", "2", "gamesaves");
            } else
            {
                uniqueId = databaseFile.Read("seed_id", "internal");
                Difficulty = short.Parse(databaseFile.Read("difficulty", "gamesaves"));
            }
        }

        private static void ResetDifficulty()
        {
            Difficulty = 2;
            databaseFile.Write("difficulty", "2", "gamesaves");
        }

        private static bool IniIntegrityCheck()
        {
            if (settingsFile == null || settingsFile.Read("Version", "PluginData") == "") return false;

            // Check all required settings exist
            if (settingsFile.Read("MainInteractionKey", "General") == "") return false;
            if (settingsFile.Read("DynamicWorld", "General") == "") return false;
            if (settingsFile.Read("PersistDifficulty", "General") == "") return false;
            if (settingsFile.Read("DrugDeal", "CalloutsEnabled") == "") return false;
            if (settingsFile.Read("DrugBust", "CalloutsEnabled") == "") return false;
            if (settingsFile.Read("DrugSeize", "CalloutsEnabled") == "") return false;
            if (settingsFile.Read("DrugShipment", "CalloutsEnabled") == "") return false;
            if (settingsFile.Read("isSuspectArmedProbability", "DrugDeal") == "") return false;
            if (settingsFile.Read("numInitialGuards", "DrugBust") == "") return false;
            if (settingsFile.Read("numAddtlGuards", "DrugBust") == "") return false;
            if (settingsFile.Read("secureAreaTime", "DrugBust") == "") return false;
            if (settingsFile.Read("drugsChance", "DrugSeize") == "") return false;
            if (settingsFile.Read("runChanceIfDrugs", "DrugSeize") == "") return false;
            if (settingsFile.Read("numCars", "DrugShipment") == "") return false;
            if (settingsFile.Read("drugsChance", "DrugShipment") == "") return false;

            return true;
        }
    }
}
