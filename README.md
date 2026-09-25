# Limit Boosters (CheapPackLimit)

A Stacklands mod that balances early-game card spam by limiting purchases and/or dynamically increasing the cost of the cheapest booster packs on each board.

---

## Features

- **Purchase Limit**: Limits how many times you can buy the cheapest packs before they are temporarily locked (`MAX`).
- **Board-Specific Price Escalation**: Customize individual price increases for each board (`main`, `island`, `cities`, etc.).
- **Percentage or Flat Increase**: Choose between fixed gold increments or percentage increases (e.g., 20% on a 5-cost pack increases price by +1 to 6, rounded up).
- **Independent or Shared Limits**: Configure limits per individual pack or shared across all cheap packs.
- **Moon-Based Reset Interval**: Set limits to reset every Moon, every $N$ Moons, or never.
- **Save & Load Persistence**: Automatically saves your purchase counts, locked pack states, and price increases into your save file.
- **Multi-Board Support**: Automatically detects and adapts to the cheapest packs on any board based on base cost.

---

## Configuration Settings

You can adjust these settings in-game via **Options > Mod Options > Limit Boosters**, or by editing the mod's configuration file.

| Setting Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| **`Max Purchases`** | `int` | `5` | Maximum number of times you can buy a tracked pack before it becomes locked. When reached, dragging currency onto the pack is blocked and `<color=red>MAX</color>` is displayed.<br>• Set to `-1` or `0` to disable the purchase limit feature. |
| **`Limit Per Individual Pack`** | `bool` | `true` | Controls how the purchase limit is tracked:<br>• `true`: Each pack has its own separate limit (e.g., buying 5 *Humble Beginning* packs only locks *Humble Beginning*; you can still buy 5 *Seeking Answers* packs).<br>• `false`: Shared pool (buying any 5 tracked cheap packs locks all cheap packs). |
| **`Use Percentage Increase`** | `bool` | `false` | When enabled, price increases are calculated as a percentage of the pack's base cost rounded up (e.g., a 20% increase on a 5g pack increases price by +1g to 6g). When disabled, price increases are flat gold values. |
| **`Price Increase per Buy (Default)`** | `int` | `1` | Fallback price increase per buy if a board doesn't have a specific setting. If percentage mode is active, this is the percentage.<br>• Set to `-1` or `0` to disable. |
| **`Price Increase ({boardId})`** | `int` | `1` | Dynamically created for each game board (e.g., `Price Increase (main)`, `Price Increase (island)`). Specifies the price increase amount (flat or percent) for that board.<br>• Set to `-1` or `0` to disable for that board. |
| **`Cheapest Packs Count`** | `int` | `2` | Number of cheapest booster packs per board tracked by this mod (e.g. `2` tracks the 2 lowest-cost packs like *Humble Beginning* @ 3g and *Seeking Answers* @ 4g).<br>• Set to `-1` or `0` to completely disable all mod features. |
| **`Reset Time (Moons)`** | `int` | `1` | How often purchase limits and price increases reset, measured in game Moons (`CurrentMonth`):<br>• `1`: Resets every Moon at the start of each month (default).<br>• `2`, `3`, etc.: Limits and price increases persist across multiple moons before resetting.<br>• `-1`: Never resets for the remainder of the run. |
| **`Enable Board Conditions`** | `bool` | `false` | Master switch to only activate limits and price increases on a board once specific conditions are satisfied. If false (default), the mod is always active everywhere. |
| **`Condition: By Moon`** | `bool` | `false` | When enabled, requires reaching or passing the target Moon number before the mod activates. |
| **`Condition: Activation Moon`** | `int` | `5` | Target Moon number (e.g., `5` means active starting on Moon 5). Must be 1 or higher. |
| **`Condition: By Card Count`** | `bool` | `false` | When enabled, requires reaching a specific card count on the board before the mod activates. |
| **`Condition: Card ID to Count`** | `string` | `villager` | Card ID(s) to count (e.g., `villager`, `coin`, or comma-separated `villager, militia`). Leave blank or `*` to count all cards on board. |
| **`Condition: Required Card Count`** | `int` | `10` | Minimum number of matching cards required to activate the mod. Must be 1 or higher. |
| **`Condition: Require ALL (AND)`** | `bool` | `false` | If enabled, ALL active conditions (Moon AND Card Count) must be satisfied. If disabled (default), satisfying EITHER condition (Moon OR Card Count) will activate the mod. |
| **`Condition: Stay Active Once Triggered`** | `bool` | `false` | If true, once a board meets activation conditions, it stays active permanently for that board. If false (default), conditions are evaluated dynamically in real time. |

---

## Example Configurations

### Scenario A: Purchase Cap Only (Default)
- `Max Purchases`: `5`
- `Limit Per Individual Pack`: `true`
- `Price Increase per Buy`: `0` (or `-1`)
- `Reset Time (Moons)`: `1`
> *Result: You can buy at most 5 of each cheap pack per Moon for the normal price. After 5 buys, that pack locks until the next Moon.*

### Scenario B: Percentage-Based Escalation
- `Max Purchases`: `-1` (disabled)
- `Use Percentage Increase`: `true`
- `Price Increase (main)`: `20` (20% per buy)
- `Reset Time (Moons)`: `1`
> *Result: On the mainland, a 5-cost pack increases by 20% (+1g, rounded up) with each purchase (5g -> 6g -> 7g...). Resets each Moon.*

### Scenario C: Hardcore Multi-Moon Challenge
- `Max Purchases`: `3`
- `Price Increase per Buy`: `2`
- `Reset Time (Moons)`: `3`
> *Result: You can only buy 3 cheap packs every 3 Moons, and each purchase costs +2g more.*
