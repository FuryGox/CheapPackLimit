using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LimitBoostersNS
{
    public class LimitBoosters : Mod
    {
        // Mod Settings
        public ConfigEntry<int> MaxPurchasesConfig = null!;
        public ConfigEntry<bool> PerPackLimitConfig = null!;
        public ConfigEntry<int> PriceIncreaseConfig = null!;
        public ConfigEntry<int> TrackedCheapestCountConfig = null!;
        public ConfigEntry<int> ResetMonthsConfig = null!;
        public ConfigEntry<bool> UsePercentageConfig = null!;
        public Dictionary<string, ConfigEntry<int>> BoardPriceIncreaseConfigs = new Dictionary<string, ConfigEntry<int>>();

        // State tracking
        public static LimitBoosters Instance = null!;
        public static int PurchasesThisMonth = 0;
        public static Dictionary<string, int> PackPurchases = new Dictionary<string, int>();
        public static int LastResetMonth = -1;
        public static string LastBoardId = "";
        public static object? CurrentRoundExtraKeyValuesRef = null;

        public static void Log(string message)
        {
            Instance?.Logger.Log(message);
        }

        public override void Ready()
        {
            Instance = this;
            Logger.Log("LimitBoosters mod is ready.");

            // Register Settings for the Mod Options Menu
            MaxPurchasesConfig = Config.GetEntry<int>("Max Purchases", 5);
            MaxPurchasesConfig.UI.Tooltip = "Maximum number of booster packs that can be purchased per month. This limit resets at the start of each new month. Set to -1 or 0 to disable this feature.";
            PerPackLimitConfig = Config.GetEntry<bool>("Limit Per Individual Pack", true);
            PerPackLimitConfig.UI.Tooltip = "Whether the purchase limit applies to each individual booster pack. If false, the limit is applied globally across all packs.";
            PriceIncreaseConfig = Config.GetEntry<int>("Price Increase per Buy (Default)", 1);
            PriceIncreaseConfig.UI.Tooltip = "Default amount by which the price of a booster pack increases with each purchase if no board-specific setting applies. Set to -1 or 0 to disable.";
            TrackedCheapestCountConfig = Config.GetEntry<int>("Cheapest Packs Count", 2);
            TrackedCheapestCountConfig.UI.Tooltip = "Number of cheapest booster packs to track for price increases. Set to -1 or 0 to disable this feature.";
            ResetMonthsConfig = Config.GetEntry<int>("Reset Time (Moons)", 1);
            ResetMonthsConfig.UI.Tooltip = "Number of Moons (CurrentMonth) between resets. Set to 1 to reset every moon (default), 2 to reset every 2 moons, etc. Set to -1 to never reset.";

            UsePercentageConfig = Config.GetEntry<bool>("Use Percentage Increase", false);
            UsePercentageConfig.UI.Tooltip = "Whether to use percentage-based price increase instead of a flat value (e.g., 20% on a 5-cost pack increases price by +1 to 6, rounded up).";

            Logger.Log($"[Config] Initial settings: MaxPurchases={MaxPurchasesConfig.Value}, PerPackLimit={PerPackLimitConfig.Value}, PriceIncreaseDefault={PriceIncreaseConfig.Value}, TrackedCheapestCount={TrackedCheapestCountConfig.Value}, ResetMonths={ResetMonthsConfig.Value}, UsePercentage={UsePercentageConfig.Value}");

            // Button to Reset Mod Data for the Current Save Round
            var resetSaveConfig = Config.GetEntry<bool>("ResetCurrentSave", false);
            resetSaveConfig.UI.Hidden = true;
            resetSaveConfig.UI.OnUI = (entry) =>
            {
                if (ModOptionsScreen.instance == null || PrefabManager.instance?.ButtonPrefab == null) return;

                CustomButton btn = UnityEngine.Object.Instantiate(PrefabManager.instance.ButtonPrefab, ModOptionsScreen.instance.ButtonsParent);
                btn.transform.localScale = Vector3.one;
                btn.transform.localPosition = Vector3.zero;
                btn.transform.localRotation = Quaternion.identity;

                btn.TextMeshPro.text = "Reset Limits (Current Save)";
                btn.TooltipText = "Immediately resets the purchased booster counters and price increases for the current run/month.";
                btn.Clicked += () =>
                {
                    // 1. Reset memory variables
                    ResetMonthlyCounters("Manual Config UI Reset");

                    // 2. If in-game, flush to ExtraKeyValues, save game, and refresh booster boxes
                    if (WorldManager.instance != null && WorldManager.instance.CurrentGameState != WorldManager.GameState.InMenu)
                    {
                        SaveToExtraKeyValues();
                        SaveManager.instance?.Save(saveRound: true);
                        Log("[Save] In-game manual reset saved via SaveManager.Save(saveRound: true).");

                        // Refresh Booster text immediately
                        if (WorldManager.instance.AllBoosterBoxes != null)
                        {
                            foreach (var box in WorldManager.instance.AllBoosterBoxes)
                            {
                                if (box != null && box.Booster != null && box.Booster.IsUnlocked)
                                {
                                    int cost = box.GetCost() - box.StoredCostAmount;
                                    if (box.BoardCurrency == BoardCurrency.Gold)
                                        box.BuyText.text = $"{cost}{Icons.Gold}";
                                    else if (box.BoardCurrency == BoardCurrency.Shell)
                                        box.BuyText.text = $"{cost}{Icons.Shell}";
                                    else if (box.BoardCurrency == BoardCurrency.Dollar)
                                        box.BuyText.text = $"{cost}{Icons.Dollar}";
                                }
                            }
                        }
                    }
                    else
                    {
                        // 3. If accessed from Main Menu, also update CurrentSave's LastPlayedRound
                        var save = SaveManager.instance?.CurrentSave;
                        if (save?.LastPlayedRound?.ExtraKeyValues != null)
                        {
                            var pItem = save.LastPlayedRound.ExtraKeyValues.FirstOrDefault(kv => kv.Key == "CheapPackLimit_PurchasesThisMonth");
                            if (pItem != null) pItem.Value = "0";
                            var ppItem = save.LastPlayedRound.ExtraKeyValues.FirstOrDefault(kv => kv.Key == "CheapPackLimit_PackPurchases");
                            if (ppItem != null) ppItem.Value = "{}";
                            SaveManager.instance?.Save(save);
                            Log("[Save] Main menu manual reset saved via SaveManager.Save(save).");
                        }
                    }

                    btn.TextMeshPro.text = "<color=green>Reset Done!</color>";
                    Log("[Reset] Manually reset limits for the current save round from Mod Options.");
                };
            };

            if (WorldManager.instance?.Boards != null)
            {
                List<GameBoard> gameboard = WorldManager.instance.Boards;
                Log($"[Boards] Discovered {gameboard.Count} game board(s).");
                foreach (var board in gameboard)
                {
                    Log($"[Boards] Board found: Id='{board.Id}', Name='{board.name}'");
                    RegisterBoardConfig(board.Id, board.name);
                }
            }

            // Apply Harmony patches
            Harmony.PatchAll();
            Logger.Log("LimitBoosters initialized with Harmony.");
        }

        public void RegisterBoardConfig(string boardId, string boardName = "")
        {
            if (string.IsNullOrEmpty(boardId) || BoardPriceIncreaseConfigs.ContainsKey(boardId)) return;

            var entry = Config.GetEntry<int>($"Price Increase ({boardName})", 1);
            entry.UI.Tooltip = $"Price increase per buy for '{boardId}'{(string.IsNullOrEmpty(boardName) ? "" : $" ({boardName})")}. If 'Use Percentage Increase' is true, this represents the percent (e.g. 20 for 20%). Set to -1 or 0 to disable.";
            BoardPriceIncreaseConfigs[boardId] = entry;
            Log($"[Config] Registered board price increase for '{boardId}'{(string.IsNullOrEmpty(boardName) ? "" : $" ({boardName})")}: value={entry.Value}");
        }

        public int GetPriceIncreaseForBoard(string boardId)
        {
            if (!string.IsNullOrEmpty(boardId) && BoardPriceIncreaseConfigs.TryGetValue(boardId, out var boardConfig))
            {
                return boardConfig.Value;
            }
            return PriceIncreaseConfig?.Value ?? 1;
        }

        public void Update()
        {
            if (WorldManager.instance == null || WorldManager.instance.CurrentBoard == null) return;
            if (WorldManager.instance.CurrentGameState == WorldManager.GameState.InMenu) return;

            // Detect new save/game session by tracking changes in RoundExtraKeyValues reference
            var currentKeyValues = WorldManager.instance.RoundExtraKeyValues;
            if (currentKeyValues != null && !ReferenceEquals(currentKeyValues, CurrentRoundExtraKeyValuesRef))
            {
                CurrentRoundExtraKeyValuesRef = currentKeyValues;
                LoadFromExtraKeyValues();
                CheckMonthReset("SessionChange");
                Log($"[Session] Save round session changed. Synced counters: PurchasesThisMonth={PurchasesThisMonth}, LastResetMonth={LastResetMonth}, Board='{LastBoardId}'.");
            }

            // Dynamically register any boards discovered during gameplay
            if (WorldManager.instance.Boards != null)
            {
                foreach (var board in WorldManager.instance.Boards)
                {
                    if (!string.IsNullOrEmpty(board.Id) && !BoardPriceIncreaseConfigs.ContainsKey(board.Id))
                    {
                        RegisterBoardConfig(board.Id, board.name);
                    }
                }
            }

            CheckMonthReset("Update");
        }

        public static void CheckMonthReset(string triggerSource)
        {
            if (WorldManager.instance == null || WorldManager.instance.CurrentBoard == null) return;
            if (WorldManager.instance.CurrentGameState == WorldManager.GameState.InMenu) return;

            string currentBoardId = WorldManager.instance.CurrentBoard.Id;
            int currentMonth = WorldManager.instance.CurrentMonth;
            if (currentMonth <= 0) return;

            // Board transition check: if the active board changed, update tracking without miscalculating moon delta
            if (!string.IsNullOrEmpty(currentBoardId) && !string.IsNullOrEmpty(LastBoardId) && currentBoardId != LastBoardId)
            {
                Log($"[BoardSwitch] Switched board from '{LastBoardId}' to '{currentBoardId}' (CurrentMonth={currentMonth}, LastResetMonth={LastResetMonth}). Updating board tracking.");
                LastBoardId = currentBoardId;
                LastResetMonth = currentMonth;
                SaveToExtraKeyValues();
                return;
            }
            LastBoardId = currentBoardId;

            // Initialize on first frame if not set
            if (LastResetMonth == -1)
            {
                LastResetMonth = currentMonth;
                int resetIntervalVal = Instance?.ResetMonthsConfig?.Value ?? 1;
                Log($"[MonthReset] Initialized LastResetMonth={LastResetMonth} on board '{currentBoardId}' (CurrentMonth={currentMonth}, ResetInterval={resetIntervalVal}).");
                SaveToExtraKeyValues();
                return;
            }

            // Handle new game or earlier save loaded (moon decreased on the same board)
            if (currentMonth < LastResetMonth)
            {
                ResetMonthlyCounters($"Moon decreased (trigger: {triggerSource}, current: {currentMonth} < last: {LastResetMonth})");
                return;
            }

            // Check if the configured number of Moons/Months has passed
            int resetInterval = Instance?.ResetMonthsConfig?.Value ?? 1;
            if (resetInterval > 0 && (currentMonth - LastResetMonth) >= resetInterval)
            {
                ResetMonthlyCounters($"Reset interval reached ({resetInterval} moon(s) passed, trigger: {triggerSource}, current: {currentMonth} vs last: {LastResetMonth})");
            }
        }

        public static void ResetMonthlyCounters(string reason = "Reset")
        {
            int oldPurchases = PurchasesThisMonth;
            int oldPacksCount = PackPurchases.Count;
            PurchasesThisMonth = 0;
            PackPurchases.Clear();
            if (WorldManager.instance != null && WorldManager.instance.CurrentBoard != null && WorldManager.instance.CurrentMonth > 0)
            {
                LastResetMonth = WorldManager.instance.CurrentMonth;
                LastBoardId = WorldManager.instance.CurrentBoard.Id;
            }
            else
            {
                LastResetMonth = -1;
            }
            SaveToExtraKeyValues();
            Log($"[Reset] {reason}: Reset cheap booster limits and price increases (Previous: PurchasesThisMonth={oldPurchases}, PackRecords={oldPacksCount} -> New: PurchasesThisMonth={PurchasesThisMonth}, LastResetMonth={LastResetMonth}, Board='{LastBoardId}').");
        }

        #region Save / Load Support
        public static void SetExtraValue(string key, string value)
        {
            if (WorldManager.instance?.RoundExtraKeyValues == null) return;
            var item = WorldManager.instance.RoundExtraKeyValues.FirstOrDefault(kv => kv.Key == key);
            if (item != null)
            {
                item.Value = value;
            }
            else
            {
                WorldManager.instance.RoundExtraKeyValues.Add(new SerializedKeyValuePair { Key = key, Value = value });
            }
        }

        public static string? GetExtraValue(string key)
        {
            if (WorldManager.instance?.RoundExtraKeyValues == null) return null;
            return WorldManager.instance.RoundExtraKeyValues.FirstOrDefault(kv => kv.Key == key)?.Value;
        }

        public static void SaveToExtraKeyValues()
        {
            try
            {
                SetExtraValue("CheapPackLimit_PurchasesThisMonth", PurchasesThisMonth.ToString());
                SetExtraValue("CheapPackLimit_LastResetMonth", LastResetMonth.ToString());
                string packPurchasesJson = JsonConvert.SerializeObject(PackPurchases);
                SetExtraValue("CheapPackLimit_PackPurchases", packPurchasesJson);
                if (!string.IsNullOrEmpty(LastBoardId))
                {
                    SetExtraValue("CheapPackLimit_LastBoardId", LastBoardId);
                }
                Log($"[Save] Saved pack limit data to RoundExtraKeyValues: PurchasesThisMonth={PurchasesThisMonth}, LastResetMonth={LastResetMonth}, Board='{LastBoardId}', PackPurchases={packPurchasesJson}");
            }
            catch (Exception ex)
            {
                Log($"[Save] Error saving pack limit data: {ex.Message}");
            }
        }

        public static void LoadFromExtraKeyValues()
        {
            try
            {
                string? purchasesStr = GetExtraValue("CheapPackLimit_PurchasesThisMonth");
                if (int.TryParse(purchasesStr, out int purchases))
                {
                    PurchasesThisMonth = purchases;
                }
                else
                {
                    PurchasesThisMonth = 0;
                }

                string? lastMonthStr = GetExtraValue("CheapPackLimit_LastResetMonth");
                if (int.TryParse(lastMonthStr, out int lastMonth))
                {
                    LastResetMonth = lastMonth;
                }
                else if (WorldManager.instance != null && WorldManager.instance.CurrentBoard != null && WorldManager.instance.CurrentMonth > 0)
                {
                    LastResetMonth = WorldManager.instance.CurrentMonth;
                }
                else
                {
                    LastResetMonth = -1;
                }

                string? boardIdStr = GetExtraValue("CheapPackLimit_LastBoardId");
                if (!string.IsNullOrEmpty(boardIdStr))
                {
                    LastBoardId = boardIdStr;
                }
                else if (WorldManager.instance?.CurrentBoard != null)
                {
                    LastBoardId = WorldManager.instance.CurrentBoard.Id;
                }

                string? packPurchasesStr = GetExtraValue("CheapPackLimit_PackPurchases");
                if (!string.IsNullOrEmpty(packPurchasesStr))
                {
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, int>>(packPurchasesStr);
                    PackPurchases = dict ?? new Dictionary<string, int>();
                }
                else
                {
                    PackPurchases.Clear();
                }

                Log($"[Load] Loaded pack limit data from RoundExtraKeyValues: PurchasesThisMonth={PurchasesThisMonth}, LastResetMonth={LastResetMonth}, Board='{LastBoardId}', PackPurchases={packPurchasesStr ?? "{}"}");
            }
            catch (Exception ex)
            {
                Log($"[Load] Error loading pack limit data: {ex.Message}");
            }
        }
        #endregion

        /// <summary>
        /// Finds the N cheapest booster pack IDs for the active board using base costs from BoosterpackData.
        /// If TrackedCheapestCountConfig is -1 (or <= 0), the feature is completely disabled.
        /// </summary>
        public static HashSet<string> GetCheapestBoosterIds()
        {
            if (WorldManager.instance?.CurrentBoard?.BoosterIds == null)
                return new HashSet<string>();

            int count = Instance != null ? Instance.TrackedCheapestCountConfig.Value : 2;
            if (count == -1 || count <= 0)
                return new HashSet<string>(); // -1 disables tracking completely

            return WorldManager.instance.CurrentBoard.BoosterIds
                .Select(id => WorldManager.instance.GetBoosterData(id))
                .Where(data => data != null)
                .OrderBy(data => data.Cost) // Base cost from BoosterpackData directly
                .Take(count)
                .Select(data => data.BoosterId)
                .ToHashSet();
        }

        /// <summary>
        /// Checks whether the purchase limit has been reached for a specific booster pack.
        /// Respects the PerPackLimitConfig setting.
        /// If MaxPurchasesConfig is -1 (or <= 0), the purchase limit is disabled.
        /// </summary>
        public static bool IsPackLimitReached(string boosterId)
        {
            if (Instance == null) return false;

            int limit = Instance.MaxPurchasesConfig.Value;
            if (limit == -1 || limit <= 0) return false;

            var cheapPacks = GetCheapestBoosterIds();
            if (!cheapPacks.Contains(boosterId)) return false;

            if (Instance.PerPackLimitConfig.Value)
            {
                PackPurchases.TryGetValue(boosterId, out int boughtCount);
                return boughtCount >= limit;
            }
            else
            {
                return PurchasesThisMonth >= limit;
            }
        }
    }

    
    #region HARMONY PATCHES

    /// <summary>
    /// Reset limits automatically when a new month begins.
    /// </summary>
    [HarmonyPatch(typeof(WorldManager), nameof(WorldManager.IncrementMonth))]
    public static class Patch_WorldManager_IncrementMonth
    {
        public static void Postfix()
        {
            LimitBoosters.Log($"[Month] WorldManager.IncrementMonth triggered (New Month: {WorldManager.instance?.CurrentMonth}). Checking month reset...");
            LimitBoosters.CheckMonthReset("IncrementMonth");
        }
    }

    /// <summary>
    /// Track when a booster pack is purchased.
    /// </summary>
    [HarmonyPatch(typeof(BuyBoosterBox), "CreateBoosterPack")]
    public static class Patch_BuyBoosterBox_CreateBoosterPack
    {
        public static void Postfix(BuyBoosterBox __instance)
        {
            string boardId = __instance.MyBoard?.Id ?? WorldManager.instance?.CurrentBoard?.Id ?? "UnknownBoard";
            int baseCost = __instance.Cost;
            int cost = __instance.GetCost();
            int increase = cost - baseCost;
            string currency = __instance.BoardCurrency.ToString();
            int currentMonth = WorldManager.instance?.CurrentMonth ?? -1;

            var cheapPacks = LimitBoosters.GetCheapestBoosterIds();
            bool isCheapPack = cheapPacks.Contains(__instance.BoosterId);

            if (isCheapPack)
            {
                LimitBoosters.PurchasesThisMonth++;

                if (!LimitBoosters.PackPurchases.ContainsKey(__instance.BoosterId))
                    LimitBoosters.PackPurchases[__instance.BoosterId] = 0;

                LimitBoosters.PackPurchases[__instance.BoosterId]++;

                LimitBoosters.SaveToExtraKeyValues();

                LimitBoosters.PackPurchases.TryGetValue(__instance.BoosterId, out int packCount);
                int maxLimit = LimitBoosters.Instance != null ? LimitBoosters.Instance.MaxPurchasesConfig.Value : 5;
                bool isLimited = LimitBoosters.IsPackLimitReached(__instance.BoosterId);
                string limitStr = maxLimit <= 0 ? "Unlimited" : $"{packCount}/{maxLimit}";

                LimitBoosters.Log(
                    $"[BuyPack] Purchased cheap booster '{__instance.BoosterId}' on board '{boardId}' (Month: {currentMonth}) | Cost: {cost} {currency} (Base: {baseCost}, Increase: +{increase}) | Pack buys: {limitStr} | Total cheap buys this month: {LimitBoosters.PurchasesThisMonth} | Status: {(isLimited ? "LOCKED (Limit Reached)" : "Available")}");
            }
            else
            {
                LimitBoosters.Log(
                    $"[BuyPack] Purchased regular booster '{__instance.BoosterId}' on board '{boardId}' (Month: {currentMonth}) | Cost: {cost} {currency} (Base: {baseCost}) | Status: Untracked");
            }
        }
    }

    /// <summary>
    /// Block buying once the limit is reached.
    /// </summary>
    [HarmonyPatch(typeof(BuyBoosterBox), nameof(BuyBoosterBox.CanHaveCard))]
    public static class Patch_BuyBoosterBox_CanHaveCard
    {
        public static void Postfix(BuyBoosterBox __instance, ref bool __result)
        {
            if (!__result) return;

            if (LimitBoosters.IsPackLimitReached(__instance.BoosterId))
            {
                __result = false; // Cannot drop gold / cards onto this pack
            }
        }
    }

    /// <summary>
    /// Dynamically increase the cost per purchase.
    /// Also updates the cost text in BuyBoosterBox.Update().
    /// </summary>
    [HarmonyPatch(typeof(BuyBoosterBox), nameof(BuyBoosterBox.GetCost))]
    public static class Patch_BuyBoosterBox_GetCost
    {
        public static void Postfix(BuyBoosterBox __instance, ref int __result)
        {
            if (LimitBoosters.Instance == null) return;

            var cheapPacks = LimitBoosters.GetCheapestBoosterIds();
            if (!cheapPacks.Contains(__instance.BoosterId)) return;

            if (!LimitBoosters.PackPurchases.TryGetValue(__instance.BoosterId, out int boughtCount) || boughtCount <= 0)
                return;

            string boardId = __instance.MyBoard?.Id ?? WorldManager.instance?.CurrentBoard?.Id ?? "";
            int increaseSetting = LimitBoosters.Instance.GetPriceIncreaseForBoard(boardId);

            if (increaseSetting == -1 || increaseSetting <= 0) return;

            if (LimitBoosters.Instance.UsePercentageConfig != null && LimitBoosters.Instance.UsePercentageConfig.Value)
            {
                // Percentage increase rounded up (e.g., baseCost = 5, 20% -> +1 per buy => 6)
                int baseCost = __instance.Cost;
                int increasePerBuy = Mathf.Max(1, Mathf.CeilToInt(baseCost * (increaseSetting / 100f)));
                __result += boughtCount * increasePerBuy;
            }
            else
            {
                // Flat value increase
                __result += boughtCount * increaseSetting;
            }
        }
    }

    /// <summary>
    /// Display "MAX" on the booster box if the purchase limit is reached.
    /// </summary>
    [HarmonyPatch(typeof(BuyBoosterBox), "Update")]
    public static class Patch_BuyBoosterBox_Update
    {
        public static void Postfix(BuyBoosterBox __instance)
        {
            if (LimitBoosters.IsPackLimitReached(__instance.BoosterId))
            {
                int resetMoons = LimitBoosters.Instance != null ? LimitBoosters.Instance.ResetMonthsConfig.Value : 1;
                if (resetMoons > 1 && WorldManager.instance != null && LimitBoosters.LastResetMonth != -1)
                {
                    int passed = WorldManager.instance.CurrentMonth - LimitBoosters.LastResetMonth;
                    int remaining = Mathf.Max(1, resetMoons - passed);
                    __instance.BuyText.text = $"<color=red>MAX ({remaining}m)</color>";
                }
                else
                {
                    __instance.BuyText.text = "<color=red>MAX</color>";
                }
            }
        }
    }

    /// <summary>
    /// Save current pack purchase limits and price increases to the save file.
    /// </summary>
    [HarmonyPatch(typeof(WorldManager), nameof(WorldManager.GetSaveRound))]
    public static class Patch_WorldManager_GetSaveRound
    {
        public static void Prefix()
        {
            LimitBoosters.Log($"[Save] WorldManager.GetSaveRound triggered (Month: {WorldManager.instance?.CurrentMonth}, Board: '{WorldManager.instance?.CurrentBoard?.Id}', PurchasesThisMonth: {LimitBoosters.PurchasesThisMonth}, LastResetMonth: {LimitBoosters.LastResetMonth}, PackPurchases: {JsonConvert.SerializeObject(LimitBoosters.PackPurchases)}). Persisting pack limit data...");
            LimitBoosters.SaveToExtraKeyValues();
        }
    }

    /// <summary>
    /// Load pack purchase limits and price increases from the save file.
    /// </summary>
    [HarmonyPatch(typeof(WorldManager), nameof(WorldManager.LoadSaveRound))]
    public static class Patch_WorldManager_LoadSaveRound
    {
        public static void Postfix()
        {
            LimitBoosters.Log($"[Load] WorldManager.LoadSaveRound triggered (Month: {WorldManager.instance?.CurrentMonth}, Board: '{WorldManager.instance?.CurrentBoard?.Id}'). Loading pack limit data...");
            LimitBoosters.CurrentRoundExtraKeyValuesRef = WorldManager.instance?.RoundExtraKeyValues;
            LimitBoosters.LoadFromExtraKeyValues();
            LimitBoosters.CheckMonthReset("LoadSaveRound");
        }
    }

    /// <summary>
    /// Cleanly initialize counters when a new round is started.
    /// </summary>
    [HarmonyPatch(typeof(WorldManager), nameof(WorldManager.StartNewRound))]
    public static class Patch_WorldManager_StartNewRound
    {
        public static void Postfix()
        {
            LimitBoosters.Log($"[NewRound] WorldManager.StartNewRound triggered (Month: {WorldManager.instance?.CurrentMonth}, Board: '{WorldManager.instance?.CurrentBoard?.Id}'). Initializing counters...");
            LimitBoosters.CurrentRoundExtraKeyValuesRef = WorldManager.instance?.RoundExtraKeyValues;
            LimitBoosters.ResetMonthlyCounters("StartNewRound");
        }
    }
    #endregion
}