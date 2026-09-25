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
        public ConfigEntry<bool> PerBoardPriceIncreaseConfig = null!;
        public Dictionary<string, ConfigEntry<int>> BoardPriceIncreaseConfigs = new Dictionary<string, ConfigEntry<int>>();

        // Board Activation Condition Settings
        public ConfigEntry<bool> EnableBoardConditionsConfig = null!;
        public ConfigEntry<bool> EnableMoonConditionConfig = null!;
        public ConfigEntry<int> ActivationMoonConfig = null!;
        public ConfigEntry<bool> EnableCardConditionConfig = null!;
        public ConfigEntry<string> ActivationCardIdConfig = null!;
        public ConfigEntry<int> ActivationCardCountConfig = null!;
        public ConfigEntry<bool> RequireAllConditionsConfig = null!;
        public ConfigEntry<bool> PersistentActivationConfig = null!;

        // State tracking
        public static LimitBoosters Instance = null!;
        public static int PurchasesThisMonth = 0;
        public static Dictionary<string, int> PackPurchases = new Dictionary<string, int>();
        public static HashSet<string> ActivatedBoards = new HashSet<string>();
        public static int LastResetMonth = -1;
        public static string LastBoardId = "";
        public static object? CurrentRoundExtraKeyValuesRef = null;
        private static bool _isInitialized = false;
        private static string _lastConditionConfigSignature = "";
        private static bool _lastLoggedConditionMet = false;
        private static string _lastLoggedReason = "";

        public static void Log(string message)
        {
            Instance?.Logger.Log(message);
        }

        public override void Ready()
        {
            Instance = this;
            if (_isInitialized)
            {
                Logger.Log("[Init] LimitBoosters is already initialized. Skipping duplicate Ready() call.");
                return;
            }
            _isInitialized = true;
            Logger.Log("LimitBoosters mod is ready.");

            // Register Settings for the Mod Options Menu
            MaxPurchasesConfig = Config.GetEntry<int>("Max Purchases", 5);
            MaxPurchasesConfig.UI.Tooltip = "Maximum number of booster packs that can be purchased per month. This limit resets at the start of each new month. Set to -1 or 0 to disable this feature.";
            PerPackLimitConfig = Config.GetEntry<bool>("Limit Per Individual Pack", true);
            PerPackLimitConfig.UI.Tooltip = "Whether purchase limits and price increases apply to each individual booster pack. If false, limits and price increases are shared globally across all cheap packs.";
            PriceIncreaseConfig = Config.GetEntry<int>("Price Increase per Buy (Default)", 1);
            PriceIncreaseConfig.UI.Tooltip = "Default amount by which the price of a booster pack increases with each purchase. Set to -1 or 0 to disable price increases.";
            PerBoardPriceIncreaseConfig = Config.GetEntry<bool>("Enable Board Specific Prices", false);
            PerBoardPriceIncreaseConfig.UI.Tooltip = "Whether to use custom price increase settings for each specific board. If false (default), 'Price Increase per Buy (Default)' is used for all boards.";
            TrackedCheapestCountConfig = Config.GetEntry<int>("Cheapest Packs Count", 2);
            TrackedCheapestCountConfig.UI.Tooltip = "Number of cheapest booster packs to track for price increases. Set to -1 or 0 to disable this feature.";
            ResetMonthsConfig = Config.GetEntry<int>("Reset Time (Moons)", 1);
            ResetMonthsConfig.UI.Tooltip = "Number of Moons (CurrentMonth) between resets. Set to 1 to reset every moon (default), 2 to reset every 2 moons, etc. Set to -1 or 0 to disable reset.";

            UsePercentageConfig = Config.GetEntry<bool>("Use Percentage Increase", false);
            UsePercentageConfig.UI.Tooltip = "Whether to use percentage-based price increase instead of a flat value (e.g., 20% on a 5-cost pack increases price by +1 to 6, rounded up).";

            // Register Board Activation Condition Settings
            EnableBoardConditionsConfig = Config.GetEntry<bool>("Enable Board Conditions", false);
            EnableBoardConditionsConfig.UI.Tooltip = "Whether to only activate the mod (limits and price increases) on a board once specific conditions are met. If disabled (default), the mod is always active on all boards.";

            EnableMoonConditionConfig = Config.GetEntry<bool>("Condition: By Moon", false);
            EnableMoonConditionConfig.UI.Tooltip = "Activate the mod on a board once the Moon number reaches or passes the specified threshold.";

            ActivationMoonConfig = Config.GetEntry<int>("Condition: Activation Moon", 5);
            ActivationMoonConfig.UI.Tooltip = "The Moon number at or after which the mod activates (CurrentMonth >= value). For example, 5 means active starting on Moon 5. Must be 1 or higher.";

            EnableCardConditionConfig = Config.GetEntry<bool>("Condition: By Card Count", false);
            EnableCardConditionConfig.UI.Tooltip = "Activate the mod on a board when a specific card count threshold is reached on that board.";

            ActivationCardIdConfig = Config.GetEntry<string>("Condition: Card ID to Count", "villager");
            ActivationCardIdConfig.UI.Tooltip = "The Card ID to count on the board (e.g., 'villager', 'coin', 'wood'). Multiple IDs can be separated by commas (e.g., 'villager, militia'). Leave blank to count ALL cards on the board.";

            ActivationCardCountConfig = Config.GetEntry<int>("Condition: Required Card Count", 10);
            ActivationCardCountConfig.UI.Tooltip = "Minimum number of cards required on the board to activate the mod. Must be 1 or higher.";

            RequireAllConditionsConfig = Config.GetEntry<bool>("Condition: Require ALL (AND)", false);
            RequireAllConditionsConfig.UI.Tooltip = "If enabled, ALL active conditions (Moon AND Card Count) must be satisfied. If disabled (default), satisfying EITHER condition (Moon OR Card Count) will activate the mod.";

            PersistentActivationConfig = Config.GetEntry<bool>("Condition: Stay Active Once Triggered", false);
            PersistentActivationConfig.UI.Tooltip = "Once a board satisfies the activation conditions, keep the mod active permanently on that board (even if card count drops). If false (default), conditions are evaluated dynamically.";

            Logger.Log($"[Config] Initial settings: MaxPurchases={MaxPurchasesConfig.Value}, PerPackLimit={PerPackLimitConfig.Value}, PriceIncreaseDefault={PriceIncreaseConfig.Value}, PerBoardPriceIncrease={PerBoardPriceIncreaseConfig.Value}, TrackedCheapestCount={TrackedCheapestCountConfig.Value}, ResetMonths={ResetMonthsConfig.Value}, UsePercentage={UsePercentageConfig.Value}, EnableConditions={EnableBoardConditionsConfig.Value}, ByMoon={EnableMoonConditionConfig.Value} (Moon={ActivationMoonConfig.Value}), ByCard={EnableCardConditionConfig.Value} (Id='{ActivationCardIdConfig.Value}', Count={ActivationCardCountConfig.Value}), RequireAll={RequireAllConditionsConfig.Value}, PersistentActivation={PersistentActivationConfig.Value}");

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
                btn.TooltipText = "Immediately resets the purchased booster counters, price increases, and board activation states for the current run/month.";
                btn.Clicked += () =>
                {
                    // 1. Reset memory variables
                    ResetMonthlyCounters("Manual Config UI Reset");
                    ActivatedBoards.Clear();

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
                            var actItem = save.LastPlayedRound.ExtraKeyValues.FirstOrDefault(kv => kv.Key == "CheapPackLimit_ActivatedBoards");
                            if (actItem != null) actItem.Value = "";
                            SaveManager.instance?.Save(save);
                            Log("[Save] Main menu manual reset saved via SaveManager.Save(save).");
                        }
                    }

                    btn.TextMeshPro.text = "<color=green>Reset Done!</color>";
                    Log("[Reset] Manually reset limits and board activations for the current save round from Mod Options.");
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

            // Safely unpatch any existing patches with this instance ID before applying to prevent duplicate hooks
            try
            {
                Harmony?.UnpatchSelf();
            }
            catch (Exception ex)
            {
                Logger.Log($"[Init] Note: UnpatchSelf cleanup returned: {ex.Message}");
            }

            // Apply Harmony patches
            Harmony?.PatchAll();
            Logger.Log("LimitBoosters initialized with Harmony.");
        }

        public void RegisterBoardConfig(string boardId, string boardName = "")
        {
            if (string.IsNullOrEmpty(boardId) || BoardPriceIncreaseConfigs.ContainsKey(boardId)) return;

            string displayName = string.IsNullOrEmpty(boardName) ? boardId : boardName;
            var entry = Config.GetEntry<int>($"Price Increase ({displayName})", -2);
            entry.UI.Tooltip = $"Price increase per buy for '{boardId}' ({displayName}). Set to -2 to use Default setting. Set to -1 or 0 to disable. (Requires 'Enable Board Specific Prices' to be ON).";
            BoardPriceIncreaseConfigs[boardId] = entry;
            Log($"[Config] Registered board price increase for '{boardId}' ({displayName}): value={entry.Value}");
        }

        public int GetPriceIncreaseForBoard(string boardId)
        {
            if (PerBoardPriceIncreaseConfig != null && PerBoardPriceIncreaseConfig.Value)
            {
                if (!string.IsNullOrEmpty(boardId) && BoardPriceIncreaseConfigs.TryGetValue(boardId, out var boardConfig))
                {
                    if (boardConfig.Value != -2)
                    {
                        return boardConfig.Value;
                    }
                }
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

            // Clear ActivatedBoards if condition configuration settings were changed by the user in Mod Options
            string currentSig = $"{Instance?.EnableBoardConditionsConfig?.Value}|{Instance?.EnableMoonConditionConfig?.Value}|{Instance?.ActivationMoonConfig?.Value}|{Instance?.EnableCardConditionConfig?.Value}|{Instance?.ActivationCardIdConfig?.Value}|{Instance?.ActivationCardCountConfig?.Value}|{Instance?.RequireAllConditionsConfig?.Value}|{Instance?.PersistentActivationConfig?.Value}";
            if (_lastConditionConfigSignature != "" && _lastConditionConfigSignature != currentSig)
            {
                Log($"[ConditionConfig] Settings changed in menu (Sig: '{currentSig}'). Clearing ActivatedBoards cache.");
                ActivatedBoards.Clear();
                SaveToExtraKeyValues();
            }
            _lastConditionConfigSignature = currentSig;

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
                SetExtraValue("CheapPackLimit_ActivatedBoards", string.Join(",", ActivatedBoards));
                Log($"[Save] Saved pack limit data to RoundExtraKeyValues: PurchasesThisMonth={PurchasesThisMonth}, LastResetMonth={LastResetMonth}, Board='{LastBoardId}', ActivatedBoards='{string.Join(",", ActivatedBoards)}', PackPurchases={packPurchasesJson}");
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

                string? activatedStr = GetExtraValue("CheapPackLimit_ActivatedBoards");
                ActivatedBoards.Clear();
                if (!string.IsNullOrEmpty(activatedStr))
                {
                    foreach (var b in activatedStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!string.IsNullOrWhiteSpace(b)) ActivatedBoards.Add(b.Trim());
                    }
                }

                Log($"[Load] Loaded pack limit data from RoundExtraKeyValues: PurchasesThisMonth={PurchasesThisMonth}, LastResetMonth={LastResetMonth}, Board='{LastBoardId}', ActivatedBoards='{string.Join(",", ActivatedBoards)}', PackPurchases={packPurchasesStr ?? "{}"}");
            }
            catch (Exception ex)
            {
                Log($"[Load] Error loading pack limit data: {ex.Message}");
            }
        }
        #endregion

        /// <summary>
        /// Finds the N cheapest booster pack IDs for the specified board (defaults to current board) using base costs from BoosterpackData.
        /// If TrackedCheapestCountConfig is -1 (or <= 0), the feature is completely disabled.
        /// </summary>
        public static HashSet<string> GetCheapestBoosterIds(GameBoard? board = null)
        {
            GameBoard? targetBoard = board ?? WorldManager.instance?.CurrentBoard;
            if (targetBoard?.BoosterIds == null)
                return new HashSet<string>();

            int count = Instance != null ? Instance.TrackedCheapestCountConfig.Value : 2;
            if (count == -1 || count <= 0)
                return new HashSet<string>(); // -1 disables tracking completely

            return targetBoard.BoosterIds
                .Select(id => WorldManager.instance?.GetBoosterData(id))
                .Where(data => data != null)
                .OrderBy(data => data!.Cost) // Base cost from BoosterpackData directly
                .Take(count)
                .Select(data => data!.BoosterId)
                .ToHashSet();
        }

        /// <summary>
        /// Checks whether the purchase limit has been reached for a specific booster pack on the specified board.
        /// Respects the PerPackLimitConfig setting.
        /// If MaxPurchasesConfig is -1 (or <= 0), the purchase limit is disabled.
        /// </summary>
        public static bool IsPackLimitReached(string boosterId, GameBoard? board = null)
        {
            if (Instance == null) return false;

            GameBoard? targetBoard = board ?? WorldManager.instance?.CurrentBoard;

            // If board activation conditions are not met, the mod is inactive on this board
            if (!IsBoardConditionMet(targetBoard)) return false;

            int limit = Instance.MaxPurchasesConfig.Value;
            if (limit == -1 || limit <= 0) return false;

            var cheapPacks = GetCheapestBoosterIds(targetBoard);
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

        /// <summary>
        /// Gets the current Moon/Month for a specific board.
        /// </summary>
        public static int GetMoonForBoard(GameBoard? board)
        {
            if (WorldManager.instance == null) return 1;
            if (board == null || board == WorldManager.instance.CurrentBoard)
            {
                return WorldManager.instance.CurrentMonth;
            }
            if (WorldManager.instance.BoardMonths != null && !string.IsNullOrEmpty(board.Id))
            {
                string id = board.Id.ToLowerInvariant();
                if (id == "main" || id == "mainland") return WorldManager.instance.BoardMonths.MainMonth;
                if (id == "island") return WorldManager.instance.BoardMonths.IslandMonth;
                if (id == "forest") return WorldManager.instance.BoardMonths.ForestMonth;
                if (id == "greed") return WorldManager.instance.BoardMonths.GreedMonth;
                if (id == "happiness") return WorldManager.instance.BoardMonths.HappinessMonth;
                if (id == "death") return WorldManager.instance.BoardMonths.DeathMonth;
                if (id == "cities") return WorldManager.instance.BoardMonths.CitiesMonth;
            }
            return WorldManager.instance.CurrentMonth;
        }

        /// <summary>
        /// Counts cards matching cardId on a specific board.
        /// If cardId is empty or '*', returns the total non-destroyed cards on the board.
        /// Supports comma/semicolon separated IDs (e.g. 'villager, militia').
        /// </summary>
        public static int GetCardCountOnBoard(GameBoard? board, string cardId)
        {
            if (board == null || string.IsNullOrEmpty(board.Id) || WorldManager.instance == null) return 0;

            var cards = WorldManager.instance.GetAllCardsOnBoard(board.Id);
            if (cards == null) return 0;

            if (string.IsNullOrWhiteSpace(cardId) || cardId.Trim() == "*")
            {
                return cards.Count(c => c != null && !c.Destroyed);
            }

            var ids = cardId.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim().ToLowerInvariant())
                            .ToHashSet();

            // Expand common aliases/typos: e.g. "vilager" <-> "villager"
            if (ids.Contains("vilager")) ids.Add("villager");
            if (ids.Contains("villager")) ids.Add("vilager");

            return cards.Count(c => c != null && !c.Destroyed && c.CardData != null && !string.IsNullOrEmpty(c.CardData.Id) && ids.Contains(c.CardData.Id.ToLowerInvariant()));
        }

        /// <summary>
        /// Evaluates whether the mod activation conditions are satisfied for the given board.
        /// If board is null, evaluates against WorldManager.instance.CurrentBoard.
        /// </summary>
        public static bool IsBoardConditionMet(GameBoard? board, out string reason)
        {
            // 1. Check if conditions are enabled either through master switch or individual condition toggles
            bool masterEnabled = Instance?.EnableBoardConditionsConfig != null && Instance.EnableBoardConditionsConfig.Value;
            bool moonToggle = Instance?.EnableMoonConditionConfig != null && Instance.EnableMoonConditionConfig.Value;
            bool cardToggle = Instance?.EnableCardConditionConfig != null && Instance.EnableCardConditionConfig.Value;

            // If master switch is OFF AND neither specific condition toggle is ON, conditions are not enabled (Mod always active)
            if (!masterEnabled && !moonToggle && !cardToggle)
            {
                reason = "Conditions disabled (Mod always active)";
                return true;
            }

            // 2. If conditions are ON, but game/board is not ready, mod must NOT activate
            if (WorldManager.instance == null)
            {
                reason = "No WorldManager (Mod inactive)";
                return false;
            }

            GameBoard? targetBoard = board ?? WorldManager.instance.CurrentBoard;
            if (targetBoard == null)
            {
                reason = "No active board (Mod inactive)";
                return false;
            }

            string boardId = targetBoard.Id ?? "";

            // 3. Persistent activation check
            bool persistentEnabled = Instance?.PersistentActivationConfig != null && Instance.PersistentActivationConfig.Value;
            if (persistentEnabled && !string.IsNullOrEmpty(boardId) && ActivatedBoards.Contains(boardId))
            {
                reason = $"Board '{boardId}' previously met conditions (Persistent active)";
                return true;
            }

            // If persistent activation is disabled by user, clear any stale cached boards
            if (!persistentEnabled && ActivatedBoards.Count > 0)
            {
                ActivatedBoards.Clear();
                SaveToExtraKeyValues();
                Log("[Condition] Persistent activation is disabled. Cleared ActivatedBoards cache.");
            }

            // 4. Check active condition switches and validate targets (> 0)
            int targetMoon = Instance?.ActivationMoonConfig?.Value ?? 1;
            bool checkMoon = moonToggle && targetMoon > 0;

            int targetCardCount = Instance?.ActivationCardCountConfig?.Value ?? 0;
            bool checkCard = cardToggle && targetCardCount > 0;

            // If conditions are enabled, but no valid condition (Moon or Card) is turned on, mod is NOT active
            if (!checkMoon && !checkCard)
            {
                reason = "Board conditions enabled, but no valid condition (Moon/Card with value > 0) is active (Mod inactive)";
                return false;
            }

            // 5. Evaluate Moon Condition
            bool moonMet = false;
            int currentMoon = GetMoonForBoard(targetBoard);
            if (checkMoon)
            {
                moonMet = currentMoon >= targetMoon;
            }

            // 6. Evaluate Card Count Condition
            bool cardMet = false;
            string targetCardId = Instance?.ActivationCardIdConfig?.Value ?? "";
            int currentCardCount = 0;
            if (checkCard)
            {
                currentCardCount = GetCardCountOnBoard(targetBoard, targetCardId);
                cardMet = currentCardCount >= targetCardCount;
            }

            // 7. Combine conditions based on RequireAll (AND vs OR)
            bool requireAll = Instance?.RequireAllConditionsConfig != null && Instance.RequireAllConditionsConfig.Value;
            bool isMet;

            if (checkMoon && checkCard)
            {
                isMet = requireAll ? (moonMet && cardMet) : (moonMet || cardMet);
                string op = requireAll ? "AND" : "OR";
                reason = $"Board '{boardId}' - Moon: {currentMoon}/{targetMoon} ({(moonMet ? "Pass" : "Fail")}) {op} Cards ('{(string.IsNullOrEmpty(targetCardId) ? "All" : targetCardId)}'): {currentCardCount}/{targetCardCount} ({(cardMet ? "Pass" : "Fail")}) => {(isMet ? "ACTIVE" : "INACTIVE")}";
            }
            else if (checkMoon)
            {
                isMet = moonMet;
                reason = $"Board '{boardId}' - Moon: {currentMoon}/{targetMoon} ({(moonMet ? "Pass" : "Fail")}) => {(isMet ? "ACTIVE" : "INACTIVE")}";
            }
            else
            {
                isMet = cardMet;
                reason = $"Board '{boardId}' - Cards ('{(string.IsNullOrEmpty(targetCardId) ? "All" : targetCardId)}'): {currentCardCount}/{targetCardCount} ({(cardMet ? "Pass" : "Fail")}) => {(isMet ? "ACTIVE" : "INACTIVE")}";
            }

            // 8. If conditions are met and persistent activation is enabled, persist state for this board
            if (isMet && persistentEnabled && !string.IsNullOrEmpty(boardId))
            {
                if (!ActivatedBoards.Contains(boardId))
                {
                    ActivatedBoards.Add(boardId);
                    SaveToExtraKeyValues();
                    Log($"[Condition] Board '{boardId}' met activation conditions and is now persistently marked as ACTIVE.");
                }
            }

            if (isMet != _lastLoggedConditionMet || reason != _lastLoggedReason)
            {
                _lastLoggedConditionMet = isMet;
                _lastLoggedReason = reason;
                Log($"[Condition] Status updated: {reason}");
            }

            return isMet;
        }

        public static bool IsBoardConditionMet(GameBoard? board = null)
        {
            return IsBoardConditionMet(board, out _);
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
        private static int _lastPurchasedFrame = -1;
        private static BuyBoosterBox? _lastPurchasedBox = null;

        public static void Postfix(BuyBoosterBox __instance)
        {
            // Safeguard against duplicate hook executions on the same booster box in the exact same frame
            if (Time.frameCount == _lastPurchasedFrame && ReferenceEquals(_lastPurchasedBox, __instance))
            {
                LimitBoosters.Log($"[BuyPack] Duplicate purchase call detected on booster box '{__instance.BoosterId}' in frame {Time.frameCount}. Skipped duplicate.");
                return;
            }
            _lastPurchasedFrame = Time.frameCount;
            _lastPurchasedBox = __instance;
            string boardId = __instance.MyBoard?.Id ?? WorldManager.instance?.CurrentBoard?.Id ?? "UnknownBoard";
            var board = __instance.MyBoard ?? WorldManager.instance?.CurrentBoard;
            int baseCost = __instance.Cost;
            int cost = __instance.GetCost();
            int increase = cost - baseCost;
            string currency = __instance.BoardCurrency.ToString();
            int currentMonth = WorldManager.instance?.CurrentMonth ?? -1;

            if (!LimitBoosters.IsBoardConditionMet(board, out string conditionReason))
            {
                LimitBoosters.Log(
                    $"[BuyPack] Board '{boardId}' condition not met ({conditionReason}). Mod is inactive on this board. Pack '{__instance.BoosterId}' bought at base cost without tracking.");
                return;
            }

            var cheapPacks = LimitBoosters.GetCheapestBoosterIds(board);
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
                bool isLimited = LimitBoosters.IsPackLimitReached(__instance.BoosterId, board);
                bool perPack = LimitBoosters.Instance?.PerPackLimitConfig?.Value ?? true;
                string limitStr = maxLimit <= 0 ? "Unlimited" : (perPack ? $"{packCount}/{maxLimit}" : $"{LimitBoosters.PurchasesThisMonth}/{maxLimit}");

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

            var board = __instance.MyBoard ?? WorldManager.instance?.CurrentBoard;
            if (LimitBoosters.IsPackLimitReached(__instance.BoosterId, board))
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

            var board = __instance.MyBoard ?? WorldManager.instance?.CurrentBoard;
            if (!LimitBoosters.IsBoardConditionMet(board)) return;

            var cheapPacks = LimitBoosters.GetCheapestBoosterIds(board);
            if (!cheapPacks.Contains(__instance.BoosterId)) return;

            // If PerPackLimitConfig is false, price increase scales globally based on all cheap packs bought this month.
            // If PerPackLimitConfig is true, price increase scales only with purchases of this specific pack.
            int countToUse;
            if (LimitBoosters.Instance.PerPackLimitConfig != null && !LimitBoosters.Instance.PerPackLimitConfig.Value)
            {
                countToUse = LimitBoosters.PurchasesThisMonth;
            }
            else
            {
                LimitBoosters.PackPurchases.TryGetValue(__instance.BoosterId, out countToUse);
            }

            if (countToUse <= 0)
                return;

            string boardId = __instance.MyBoard?.Id ?? WorldManager.instance?.CurrentBoard?.Id ?? "";
            int increaseSetting = LimitBoosters.Instance.GetPriceIncreaseForBoard(boardId);

            if (increaseSetting == -1 || increaseSetting <= 0) return;

            if (LimitBoosters.Instance.UsePercentageConfig != null && LimitBoosters.Instance.UsePercentageConfig.Value)
            {
                // Percentage increase rounded up (e.g., baseCost = 5, 20% -> +1 per buy => 6)
                int baseCost = __instance.Cost;
                int increasePerBuy = Mathf.Max(1, Mathf.CeilToInt(baseCost * (increaseSetting / 100f)));
                __result += countToUse * increasePerBuy;
            }
            else
            {
                // Flat value increase
                __result += countToUse * increaseSetting;
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
            var board = __instance.MyBoard ?? WorldManager.instance?.CurrentBoard;
            if (LimitBoosters.IsPackLimitReached(__instance.BoosterId, board))
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
            LimitBoosters.ActivatedBoards.Clear();
            LimitBoosters.ResetMonthlyCounters("StartNewRound");
        }
    }
    #endregion
}