# Endless probe

Generated 2026-11-07 15:56. Water width 15 m, raft radius 2.4 m.

## 1. Layout guarantee (raft-sized sweep along the safe path, 52 obstacle-clock samples; fire jets and cannonball triggers included; whirlpool pull must never reach the raft's rim)

| Seed | Rows | Obstacles | Min gap (m) | Path metres checked | Violations |
|---|---|---|---|---|---|
| 11 | 212 | 488 | 5.80 | 4155 | 0 |
| 2024 | 209 | 484 | 5.80 | 4155 | 0 |
| 77777 | 211 | 452 | 5.80 | 4155 | 0 |
| 31337 | 217 | 500 | 5.80 | 4155 | 0 |
| 9001 | 220 | 478 | 5.80 | 4155 | 0 |

Obstacle mix: Barrels 42, Crates 36, BankRoller 243, MineField 232, SpikeBridge 404, SpikeSwing 62, Puffer 117, Press 91, SpikeTrap 103, WaitGate 121, Windmill 82, BuoyLine 295, Gate 132, Pendulum 58, Buzzsaw 106, FireJet 33, Boulder 29, Mortar 67, Spinner 55, Tentacle 19, Sweeper 20, SpikeLogRoll 7, Log 8, RockPair 15, StarSpinner 8, Barrier 6, Whirlpool 11.

**PASS: the safe path is clear for every seed at every sampled time.**

Wait gates: 121, open 1.00 s longer than the raft needs to pass at the slowest current there (worst case). **PASS**

## 2. AI play tests (real physics, Standard assist)

| AI crew | Seed | Result | Distance (m) | Time (s) | Crash |
|---|---|---|---|---|---|
| 3 | 240152805 | crashed | 123 | 85 | a closing spike gate |
| 1 | 1725682125 | crashed | 61 | 14 | a buzzsaw |

## 3. Crash check (raft dropped upstream of an obstacle, no thrust)

| Obstacle | Result | Reason |
|---|---|---|
| Boulder | game over | an ice crystal |
| Crates | game over | a barricade |
| Barrels | NO CRASH |  |
| BuoyLine | game over | a rope fence |
| Log | game over | a concrete barrier |
| Spinner | game over | a spinning blade |
| Press | game over | a stamping press |
| SpikeTrap | game over | a spike trap |
| Buzzsaw | game over | a buzzsaw |
| StarSpinner | game over | a spinning spike star |
| Puffer | game over | a puffer mine |
| BankRoller | NO CRASH |  |
| Tentacle | game over | a kraken tentacle |
| FireJet | game over | a fire jet (raft held in the danger zone) |
| Mortar | game over | a cannonball (raft held in the danger zone) |
| Whirlpool | game over | a whirlpool grinder |
| SpikeSwing | game over | a swinging spiked log (raft held in the danger zone) |
| MineField | NO CRASH |  |
| SpikeLogRoll | not planned in range | |
| SpikeBridge | game over | a spiked log under the bridge |
| WaitGate | game over | a closing spike gate (raft held in the danger zone) |
| Bank edge | game over | a spiked bank roller |

**FAIL: 3 hits did not end the run.**
