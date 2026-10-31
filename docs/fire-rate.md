# Fire rate converter

Games describe a weapon's rate of fire in **rounds per minute** (RPM). Haptic Gaming Bridge works with a **cooldown in milliseconds**: the minimum time between two pulses. The converter in the action editor turns one into the other so you can match a weapon without doing the maths.

## How to use it

1. Open an action (**+ Add action**, or **Edit** on an existing one).
2. Type the weapon's rate in **Rounds per minute**, or press one of the quick buttons (300, 600, 900, 1200).
3. The **Cooldown** box and slider update. The line under the rate box tells you the interval, for example *100 ms between shots*.
4. Tick **Repeat while held** so the pulse repeats at that rate while you hold the button.

It also works the other way: change the cooldown or move the slider and the rate box follows. While you are typing a rate, the box keeps exactly what you typed, so 700 stays 700 even though it is stored as 86 ms.

Decimals are accepted with a point or a comma (`600.5` or `600,5`). Do not type thousands separators: `1,5` means one and a half, not fifteen hundred.

## The formula

```
cooldown (ms) = 60000 / rounds per minute        (rounded to a whole millisecond)
rounds per minute = 60000 / cooldown (ms)
```

| Rounds per minute | Cooldown | Shots in 10 s |
|---|---|---|
| 60 | 1000 ms | 10 |
| 300 | 200 ms | 50 |
| 600 | 100 ms | 100 |
| 750 | 80 ms | 125 |
| 900 | 67 ms | 150 |
| 1200 | 50 ms | 200 |
| 3000 | 20 ms | 500 |

Because the cooldown is a whole number of milliseconds, some rates cannot be exact: 900 per minute is 66.67 ms and is stored as 67 ms, which reads back as 896. The error is at most half a millisecond per shot.

## Limits

- **Lowest cooldown: 20 ms** (3000 rounds per minute). A faster rate is clamped to this.
- **Highest cooldown: 60 000 ms** (1 round per minute).
- A rate of 0, a negative number or text that is not a number does not change the cooldown.
- **Accuracy.** The repeat timer on Windows ticks about every 15 ms. Each shot is scheduled from when it was due, not from when the timer noticed, so the shots stay on a fixed grid and the average rate is exact. Individual shots can still be up to one tick early or late. Below 50 ms that is a big share of the interval, so the editor warns that the rate is approximate.
- **The mouse.** The mouse merges or drops haptic pulses that arrive too close together, and each pulse is short. Matching a weapon's exact rate does not mean you will feel every shot as a separate pulse at high rates. On the one MX Master 4 this was tried on, cooldowns between about 50 and 350 ms felt right depending on the pattern; that is a personal impression, not a measured limit, so try your own weapon with the **Test** button.
- It only changes the cooldown. Which key triggers it, the pattern and the modifier are set separately.

## How it is checked

The test suite (`tests/HapticGamingBridge.Tests`) checks the conversion in both directions, the clamping, parsing, and, with a simulated timer, that holding a button for ten seconds delivers the requested number of shots at 300, 600, 750, 900 and 1200 rounds per minute within 2 per cent. Run it with `tests\run-testroom.ps1`.
