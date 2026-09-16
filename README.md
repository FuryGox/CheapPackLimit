# Limit Boosters (CheapPackLimit)

A Stacklands mod that balances early-game card spam by limiting purchases and/or dynamically increasing the cost of the cheapest booster packs on each board.

---

## Features

- **Purchase Limit (Variant 1)**: Limits how many times you can buy the cheapest packs before they are temporarily locked (`MAX`).
- **Price Escalation (Variant 2)**: Increases the cost of cheapest packs each time you buy one.
- **Independent or Shared Limits**: Configure limits per individual pack or shared across all cheap packs.
- **Moon-Based Reset Interval**: Set limits to reset every Moon, every $N$ Moons, or never.
- **Save & Load Persistence**: Automatically saves your purchase counts, locked pack states, and price increases into your save file.
- **Multi-Board Support**: Automatically detects and adapts to the cheapest packs on any board (Mainland, Island, Cities, etc.) based on base cost.

---

## Configuration Settings

You can adjust these settings in-game via **Options > Mod Options > Limit Boosters**, or by editing the mod's configuration file.

| Setting Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| **`Max Purchases`** | `int` | `5` | Maximum number of times you can buy a tracked pack before it becomes locked. When reached, dragging currency onto the pack is blocked and `<color=red>MAX</color>` is displayed.<br>• Set to `-1` or `0` to disable the purchase limit feature. |
| **`Limit Per Individual Pack`** | `bool` | `true` | Controls how the purchase limit is tracked:<br>• `true`: Each pack has its own separate limit (e.g., buying 5 *Humble Beginning* packs only locks *Humble Beginning*; you can still buy 5 *Seeking Answers* packs).<br>• `false`: Shared pool (buying any 5 tracked cheap packs locks all cheap packs). |
| **`Price Increase per Buy`** | `int` | `1` | Extra gold/currency cost added to the pack price with every purchase.<br>• Set to `-1` or `0` to disable price increases. |
| **`Cheapest Packs Count`** | `int` | `2` | Number of cheapest booster packs per board tracked by this mod (e.g. `2` tracks the 2 lowest-cost packs like *Humble Beginning* @ 3g and *Seeking Answers* @ 4g).<br>• Set to `-1` or `0` to completely disable all mod features. |
| **`Reset Time (Moons)`** | `int` | `1` | How often purchase limits and price increases reset, measured in game Moons (`CurrentMonth`):<br>• `1`: Resets every Moon at the start of each month (default).<br>• `2`, `3`, etc.: Limits and price increases persist across multiple moons before resetting.<br>• `-1`: Never resets for the remainder of the run. |

---

## Example Configurations

### Scenario A: Purchase Cap Only (Default)
- `Max Purchases`: `5`
- `Limit Per Individual Pack`: `true`
- `Price Increase per Buy`: `0` (or `-1`)
- `Reset Time (Moons)`: `1`
> *Result: You can buy at most 5 of each cheap pack per Moon for the normal price. After 5 buys, that pack locks until the next Moon.*

### Scenario B: Escalating Prices (No Hard Lock)
- `Max Purchases`: `-1`
- `Price Increase per Buy`: `1`
- `Reset Time (Moons)`: `1`
> *Result: You can buy as many packs as you want, but each purchase raises the price by +1g. Prices reset back to base costs each Moon.*

### Scenario C: Hardcore Multi-Moon Challenge
- `Max Purchases`: `3`
- `Price Increase per Buy`: `2`
- `Reset Time (Moons)`: `3`
> *Result: You can only buy 3 cheap packs every 3 Moons, and each purchase costs +2g more.*
