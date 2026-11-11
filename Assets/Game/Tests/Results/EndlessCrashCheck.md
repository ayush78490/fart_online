# Endless probe

Generated 2026-11-07 16:02. Water width 15 m, raft radius 2.4 m.

## 1. Layout guarantee (raft-sized sweep along the safe path, 52 obstacle-clock samples; fire jets and cannonball triggers included; whirlpool pull must never reach the raft's rim)

| Seed | Rows | Obstacles | Min gap (m) | Path metres checked | Violations |
|---|---|---|---|---|---|

Obstacle mix: .

**PASS: the safe path is clear for every seed at every sampled time.**

Wait gates: none planned.

## 2. AI play tests (real physics, Standard assist)

| AI crew | Seed | Result | Distance (m) | Time (s) | Crash |
|---|---|---|---|---|---|

## 3. Crash check (raft dropped upstream of an obstacle, no thrust)

| Obstacle | Result | Reason |
|---|---|---|
| Boulder | game over | an ice crystal |
| Crates | game over | a barricade |
| Barrels | game over | a spiked mine |
| BuoyLine | game over | a rope fence |
| Log | game over | a concrete barrier |
| Spinner | game over | a spinning blade |
| Press | game over | a stamping press |
| SpikeTrap | game over | a spike trap |
| Buzzsaw | game over | a buzzsaw |
| StarSpinner | game over | a spinning spike star |
| Puffer | game over | a puffer mine |
| BankRoller | game over | a spiked mine |
| Tentacle | game over | a kraken tentacle |
| FireJet | game over | a fire jet (raft held in the danger zone) |
| Mortar | game over | a cannonball (raft held in the danger zone) |
| Whirlpool | game over | a gate post |
| SpikeSwing | game over | a swinging spiked log (raft held in the danger zone) |
| MineField | game over | a spiked mine |
| SpikeLogRoll | game over | a rolling spiked log |
| SpikeBridge | game over | a spiked log under the bridge |
| WaitGate | game over | a closing spike gate (raft held in the danger zone) |
| Bank edge | game over | a spiked bank roller |

**PASS: every obstacle hit ends the run.**
