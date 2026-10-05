using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using wawa.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;
using Newtonsoft.Json;

public class thunderRuleseedService : MonoBehaviour {

    public KMGameInfo gameInfos;
    GameObject ruleSeedModifierApiGameObject;

    // Only my own Ruleseeded missions should ever be affected
    // Those will contain the verificationKey to make sure I don't affect other 
    readonly string verificationKey = "mod_thunder725sMusicalAdventures_Ruleseed_";

    string currentBombId = "undefined";
    KMGameInfo.State currentGameState;
    int previousRuleseed = 1;
    bool hasVerifiedThisBombAlready;

	void Start()
	{
        ModInfo _e = wawa.IO.PathFinder.GetModInfo().Value;
        if (_e == null )
        {
            ModuleLog("Initializing - Couldn't get mod version from wawa!!");
        }
        else
        {
            ModuleLog("Initializing - Version {0}", _e.Version);
        }
        

        if (gameInfos == null)
        {
            ModuleLog("KMGameInfo NOT found!! Please report this to thunder725 (and send help too)");
        }
        else
        {
            ModuleLog("KMGameInfo found. Binding to delegates.");
            gameInfos.OnStateChange += OnGameStateChange;
            gameInfos.OnLightsChange += OnGameLightsChange;
        }


        isRuleseedRandomEnabledFromSettings();
    }

    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
    //  Delegates
    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=

    /// <summary>
    /// Called every time the Game State changes: Entering the Main Menu Room, Entering the Bomb Defusal Room, Transitionning from a state to another, etc
    /// </summary>
    void OnGameStateChange(KMGameInfo.State state)
    {
        hasVerifiedThisBombAlready = false;
        currentGameState = state;
        
        // When entering the main menu,
        // Check all missions and remove the Rule Seed blocker if Rule Seed is present!
        if (state == KMGameInfo.State.Setup)
        {
            RemoveRuleSeedModifierBlockers();
        }
    }



    /// <summary> Called when a new bomb is started, when the Lights turn on (after modules have been loaded) </summary>
    void OnBombStart()
    {
        // Get the current bomb's ID, and check if it is one of my own "must be ruleseeded" bomb
        currentBombId = GetMissionID();
        bool currentBombMustBeRuleseeded = currentBombId.StartsWith(verificationKey);

        // Bomb is not one of my own ruleseeded missions
        // Do nothing. I'm not developping a malware
        if (currentBombMustBeRuleseeded == false)
        { return; }


        ModuleLog("Starting thunder725 Ruleseeded mission {0}", currentBombId);
        bool ruleseedModIsPresent = GetRuleseedModifierApiGameObject();


        if (ruleseedModIsPresent == false)
        {
            // Prevent bomb from starting
            // This shouldn't ever happen, because there should be a blocker; but just in case!
            ModuleLog("Trying to start mission without Rule Seed Modifier being installed and active!");
            ExplodeBomb();
            return;
        }


        // Read the Settings file to know if the Rule Seed is randomized or not
        string _ruleseedSettingsValue = isRuleseedRandomEnabledFromSettings();


        // It's correctly set in the settings, all good
        // Save the Rule Seed just in case
        if (_ruleseedSettingsValue == "true")
        {
            previousRuleseed = getCurrentRuleseed();
            ModuleLog("Started mission with Rule Seed Modifier enabled and a random Rule Seed! You're allowed to play.");
        }
        // Settings explicitely tell us that the Rule Seed is set and not random. This is forbidden
        else if (_ruleseedSettingsValue == "false")
        {
            ModuleLog("Started mission with Rule Seed Modifier enabled but with a set Rule Seed! This mission should be played with a random Rule Seed only!!");
            ExplodeBomb();
        }
        // There has been an issue while reading the Settings file
        // Maybe it's missing altogether, or the specific setting wasn't found
        // In any case, use the failsafe check instead:
        else
        {
            // Read the current Rule Seed
            int currentRuleseed = getCurrentRuleseed();


            if (currentRuleseed == 1)
            {
                ModuleLog("Started mission with Rule Seed Modifier enabled but with a default Rule Seed! That's not valid!!!");
                ExplodeBomb();

            }
            // Prevent ruleseeds from being the same, that's cheating and basically ruleseed 1 with extra steps
            else if (previousRuleseed == currentRuleseed)
            {
                ModuleLog("Started mission with Rule Seed Modifier enabled but with the same Rule Seed as last attempt! This mission should be played with Random Rule Seeds!!");
                ExplodeBomb();

            }
            // All good if it's actually ruleseeded.
            else
            {
                previousRuleseed = currentRuleseed;
                ModuleLog("Started mission with Rule Seed Modifier enabled and a non-default non-repeating Rule Seed! You're allowed to play.");
            }
        }

    }


    /// <summary> Called whenever the lights turn on or off in the gameplay room. This is guaranteed to be AFTER the bomb has fully loaded! </summary>
    void OnGameLightsChange(bool lightsAreOn)
    {
        // Don't do anything in any of the transition states
        if (currentGameState != KMGameInfo.State.Gameplay)
        { return; }

        // Only do anything if lights are ON to make sure everything's initialized properly.
        if (lightsAreOn == false)
        { return; }

        // This should be only called once at the start of the bomb
        // However since we receive this call when lights are turned on in the Gameplay,
        // it might be called multiple times since lights can turn on and off during a solve
        // so this anti-return boolean is only reset when the game state changes, so it triggers only once per bomb!
        if (hasVerifiedThisBombAlready)
        { return; }
        hasVerifiedThisBombAlready = true;

        OnBombStart();
    }

    // Credit from eXish's Mission Service Script
    void ExplodeBomb()
    {
        Type bombType = ReflectionHelper.FindGameType("Bomb");
        UnityEngine.Object bomb = FindObjectOfType(bombType);
        if (bomb == null)
        {
            ModuleLog("Didn't find Bomb!! Please send this to thunder725 (and send help also please).");
        }
        else
        {
            ModuleLog("Exploding Bomb.");
            ReflectionHelper.CallMethod(bombType, "Detonate", FindObjectOfType(bombType));
        }
    }


    // Using the idea from Factory and Multiple Bombs requiring the non-module mod Factory to be installed to launch sequence bombs,
    // I'm forcing the mod Rule Seed Modifier to be installed and enabled to launch my Rule Seeded bombs
    // To do this, all of them are prefixed with "_Ruleseed_" and ask for the """module""" Rule Seed Modifier to be present
    // This is not a real module, but this method is here to catch those missions and remove the requirement if the RuleSeed mod is installed
    void RemoveRuleSeedModifierBlockers()
    {
        // If RuleSeed is not installed, we don't have to remove anything!
        if (GetRuleseedModifierApiGameObject() == false)
        { return; }

        var _allModMissions = ModManager.Instance.ModMissions;
        for (int i = 0; i < _allModMissions.Count; i++)
        {
            // Only affect my own missions that need to be ruleseeded
            if (_allModMissions[i].ID.StartsWith(verificationKey) == false)
            { continue; }

            // On the mission, get the GeneratorSettings containing all modules inside of the bomb
            // The last one will always be where "Rule Seed Modifier" is set
            // If it's present, remove it
            // If it's not present, it has already been removed during previous visits to the main menu
            if (_allModMissions[i].GeneratorSetting.ComponentPools.Last().ModTypes[0] == "Rule Seed Modifier")
            {
                ModuleLog("Removed the Rule Seed Modifier blocker from mission {0}", _allModMissions[i].ID);
                _allModMissions[i].GeneratorSetting.ComponentPools.RemoveAt(_allModMissions[i].GeneratorSetting.ComponentPools.Count - 1);
            }
            continue;
        }
    }




    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
    //  Helper functions
    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=

    

    // Code credit to eXish from his Custom Missions
    string GetMissionID()
    {
        try
        {
            Component gameplayState = GameObject.Find("GameplayState(Clone)").GetComponent("GameplayState");
            Type type = gameplayState.GetType();
            FieldInfo fieldMission = type.GetField("MissionToLoad", BindingFlags.Public | BindingFlags.Static);
            return fieldMission.GetValue(gameplayState).ToString();
        }
        catch (NullReferenceException)
        {
            return "undefined";
        }
    }

    bool GetRuleseedModifierApiGameObject()
    {
        ruleSeedModifierApiGameObject = GameObject.Find("RuleSeedModifierProperties");
        
        if (ruleSeedModifierApiGameObject == null)
        {
            ModuleLog("Did not find the RuleSeed Modifier API Game Object.");
            return false;
        }
        else
        {
            return true;
        }
    }

    int getCurrentRuleseed()
    {
        if (ruleSeedModifierApiGameObject == null) // If the Rule Seed Modifer is not installed, return.
        { return 1; }

        IDictionary<string, object> ruleSeedModifierAPI = ruleSeedModifierApiGameObject.GetComponent<IDictionary<string, object>>();
        int seed = (int)ruleSeedModifierAPI["RuleSeed"];
        ModuleLog("Found Ruleseed {0}.", seed);

        return seed;
    }





    // Class used to convert the settings JSON into code
    // Only the singular value that I'm interested in is declared, so everything else is ignored (and it doesn't cause any error)
    class RuleSeedSettings
    {
        public bool RandomRuleSeed = false;
    }

    // // Used to quickly test with a Settings JSON from within the editor directly
    // public TextAsset ruleseedSettingsTextAsset;

    // Used to determine, through grabbing the actual mod settings, whether the Rule Seed Modifier mod has Random Ruleseed enabled
    // The resulting string is the value of the RandomSeed boolean in the settings
    // If the value can't be read, it'll return "null" instead while logging
    string isRuleseedRandomEnabledFromSettings()
    {
        // Copied Verbatim from the VanillaRuleModifierAssembly ModSettings.cs
        string _settingsPath = Path.Combine(Application.persistentDataPath, "Modsettings");
        _settingsPath = Path.Combine(_settingsPath, "VanillaRuleModifier-settings.txt");

        ModuleLog("Searching for a Rule Seed Modifier Setting file in path {0}", _settingsPath);

        string _ruleSeedSettingsContent = "";
        try
        {
            if (File.Exists(_settingsPath))
            {
                // Read the content of the file
                _ruleSeedSettingsContent = File.ReadAllText(_settingsPath);

                // Deserialize (convert from JSON to the Class)
                RuleSeedSettings settings = JsonConvert.DeserializeObject<RuleSeedSettings>(_ruleSeedSettingsContent);


                ModuleLog("Found the Rule Seed Modifier Settings file, with RandomRuleSeed value {0}", settings.RandomRuleSeed);
                return settings.RandomRuleSeed.ToString();
            }
            else
            {
                ModuleLog("Couldn't find a Rule Seed Modifier Settings file at the path {0}", _settingsPath);
                return "null";
            }
        }
        catch (Exception ex)
        {
            ModuleLog("Couldn't load Rule Seed Modifier Settings due to exception:\n{0}\nStack Trace:\n{1}\n.", ex.Message, ex.StackTrace);
            return "null";
        }

        

        // // Regex for finding the
        // //     "RandomRuleSeed": true     
        // // and extracting the resulting "true" or "false"
        // // The @ before the " is to say to the string to not count \ as a special character
        // // However we still need a double " to tell it to count it as an actual " inside of the string, and not a terminator
        // string settingFinderRegex = @"RandomRuleSeed""\s*:\s*(true|false)";
        // 
        // Match _match = Regex.Match(_ruleSeedSettingsContent, settingFinderRegex, RegexOptions.IgnoreCase);
        // 
        // 
        // if (_match.Success == false)
        // {
        //     ModuleLog("Regex Search for the Random Rule Seed setting didn't yield any result.");
        //     return "null";   
        // }
        // 
        // if (_match.Groups.Count < 2)
        // {
        //     ModuleLog("Regex Search for the Random Rule Seed setting yielded less than 2 results in its group.");
        //     return "null";
        // }
        // 
        // // Groups[0] in this case is the entire matching string, which is   RandomRuleSeed": false
        // // We want only the part we specified in parentheses: true or false; that is contained within Groups[1]
        // string resultSettingString = _match.Groups[1].Value.ToLowerInvariant();
        // 
        // ModuleLog("Regex Search for the Random Rule Seed setting yielded a value of {0}.", resultSettingString);
        // return resultSettingString;
    }

   




    public void ModuleLog(string message, params object[] args)
    {
        Debug.LogFormat("[thunder725's Musical Ruleseed Service] " + string.Format(message, args));
    }
}



/**     CODE GRAVEYARD OF UNUSED CODE
 * 
 * 
 * 
 * 
 *     /// <summary> Generates a random Ruleseed within the bounds of the RuleSeed Modifier mod setting. </summary>
    int GetRandomRuleseedWithinBounds()
    {
        int seed = 0;

        int seedMaxValue = 10000;
        ModuleLog("Didn't find the Mod Settings for Rule Seed Modifier! Generating random Ruleseed [0-10,000] instead.");

        // This is what's used in
        // https://github.com/CaitSith2/KTANE-mods/blob/master/RuleSeedModifier/Managed/VanillaRuleModifierAssembly/VanillaRuleModifierAssembly/VanillaRuleModifier.cs#L184
        if (seedMaxValue < 0) { seedMaxValue = int.MaxValue; }

        seed = UnityEngine.Random.Range(0, seedMaxValue);
        return seed;
    }


    // Currently unused as the ruleseed can only be set BEFORE the mission gets loaded.
    // The current implementation checks only using events that get available AFTER the bomb is loaded,
    // so setting the ruleseed manually is impossible
    /// <summary> Method to set a random ruleseed on the current bomb </summary>
    void SetRandomRuleseed()
    {
        if (ruleSeedModifierApiGameObject == null) // If the Rule Seed Modifer is not installed, return.
        { return; }

        int seed = GetRandomRuleseedWithinBounds();

        IDictionary<string, object> ruleSeedModifierAPI = ruleSeedModifierApiGameObject.GetComponent<IDictionary<string, object>>();
        ruleSeedModifierAPI["RuleSeed"] = seed;
        ModuleLog("Force-setting Ruleseed to be randomly-obtained {0}.", seed);
    }
 * 
 * 
 * 
 * 
 * 
 * void LogAllLoadedModsFromMissionManagedPov()
    {
        ModuleLog("All loaded Modules: {0}", ModManager.Instance.loadedBombComponents.Keys.Join(" // "));
    }

    void LogAllModdedMissions()
    {
        ModuleLog("All loaded Missions: {0}", ModManager.Instance.ModMissions.Select(x => x.ID).Join("\n"));
    }
 * 
 */