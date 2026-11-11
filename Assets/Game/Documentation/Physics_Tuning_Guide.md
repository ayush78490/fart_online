# Fart Online: gameplay physics tuning guide

All the numbers live in **`Assets/Game/Settings/RaftTuning.asset`** (class `RaftTuning`). You can edit it during Play Mode; `SharedRaftController.ApplyTuning()` pushes the body values to the Rigidbody. The current values were checked with the play-mode tests listed in `Test_Report.md`.

## The model

- **One body.** The raft is one Rigidbody (450 kg, round, radius 2.4 m). Its centre of mass is lowered 0.25 m for stability. Characters have no colliders or rigidbodies and are parented to the seats.
- **Seats.** Each player sits on a circle of radius 1.65 m, **facing the centre**. Their position is an angle measured clockwise from the bow (0 = front, 90 = right, 180 = back).
- **Fart force.** For player *i* at angle θᵢ, the push direction in raft space is Dᵢ = −(sin θᵢ, cos θᵢ), from the seat toward the centre:

  `Fᵢ = Dᵢ × T × Sᵢ`, where T = `thrust` and Sᵢ = intensity (1 while F is held, scaled down if the tank runs dry mid-step).

  It is applied at the seat's thrust origin, 0.36 m behind the character and 0.15 m above the seat.
- **Combining.** Every physics step, `SharedRaftController` sums `F_total = Σ Fᵢ` and `τ_total = Σ (rᵢ × Fᵢ)` about the live centre of mass, and applies them once. If |F_total| exceeds `maxCombinedForce`, force and torque are scaled together.
- **Consequences of the round design:**
  - Thrust lines pass close to the centre, so farting causes almost no yaw. Measured yaw is 0.00 rad/s for back, side and opposite-pair farting.
  - Rotation comes from collisions and the current.
  - Players steer by **where they sit**, not by aiming. Two players on opposite sides cancel each other out.
  - Players can't pass each other (`minSeatSpacing`), so a full crew can only shift a little.
- **Force modes:**
  - An F press = one impulse (`tapImpulse`).
  - Holding F for longer than `sustainDelay` = continuous `ForceMode.Force`.
  - A charged release and a brake are single impulses.
  - The same action never produces both an impulse and a continuous force in one step.
- **Water.**
  - `RaftBuoyancy` has nine float points 0.45 m below the deck. `buoyancy` 22 gives a waterline at the deck.
  - It drags the raft toward the local current with a drag of `currentDrag` (0.9/s) × (1 + 0.3 × (flow − 4 m/s)) in fast water. That extra grip in fast water is what carries the raft down the chute to the jump.
  - `WaterCurrentController.strength` scales every flow speed.
- **Limits and stabilisation:**
  - **Speed:** above `maxSpeed` relative to the water (10.5 m/s), a soft counter-force applies.
  - **Rotation:** angular velocity is capped at `maxAngularVelocity`.
  - **Self-righting:** a torque of `uprightStrength` per radian of tilt, damped by `uprightDamping`, and stiffer past `softTiltLimit`. It is 30% as strong in the air.

## Values and why

| Value | Now | Effect / reason |
|---|---|---|
| `thrust` | 1750 N | One player at the back gains about 3.3 m/s in 1.5 s (self-test), comfortably beating the current. Two players gain about 4.7 m/s in 1 s. |
| `maxCombinedForce` | 4900 N | Four players together accelerate hard but stay controllable. |
| `tapImpulse` | 750 N·s | About 1.5 m/s nudge: a correction or a timed hop past a sweeper. |
| `chargedImpulseMin/Max`, `chargeTime` | 1150 / 3000 N·s, 1.1 s | Up to about 5 m/s (self-test 4.97); costs up to 38% gas. |
| `brakeImpulse` | 1500 N·s | Removes about 1.2 m/s from any seat (fires against the velocity). |
| `seatMoveSpeed` | 105 °/s | Half the rim in about 1.7 s; half speed while farting. |
| `minSeatSpacing` | 42° | Four pigs fit (4 × 42 = 168° of the rim), but they can't stack on one spot. |
| `sustainCostPerSecond` / `regenPerSecond` / `regenDelay` | 0.30 / 0.24 / 0.45 s | About 3 s of continuous stream from a full tank; rest about 4 s to refill. |
| `maxBurstDuration` / `burstRecovery` | 1.8 s / 0.45 s | Forces rhythm (bursts) instead of one endless stream. |
| `exhaustedCooldown` / `exhaustedRecoverLevel` | 1.3 s / 25% | An empty tank is a short, visible penalty ("EMPTY" plus a grey "pfft"). |
| `airThrustFactor` | 0.45 | Farts still work in the air over the jump, but weaker. |
| `capsizeAngle` / `flipSeconds` | 75° / 1.5 s | Capsize wipeout. |
| `strandedSeconds` | 5 s | Stuck on a bank or the dry gap → wipeout. |
| `stuckSeconds` | 6 s | No progress while thrusting → "press R" prompt (never automatic). |
| `wipeoutDelay` | 1.6 s | Wipeout cue, then respawn at the last checkpoint. |

## Solo assist

| | Off | Standard | High |
|---|---|---|---|
| Gas regeneration | ×1 | ×1.3 | ×1.6 |
| Yaw damping when nobody farts | none | 0.8/s | 1.6/s |
| Self-righting | ×1 | ×1.25 | ×1.6 |
| Wall scrapes (impacts below `heavyImpactImpulse`) | full | 35% of the lost speed and added spin given back | 55% |
| Rim walking speed | ×1 | ×1 | ×1.3 |

Assist never moves the raft by itself. There is no hidden propulsion and no teammates other than real players: the game has no AI crew (removed 2026-11-11; a bot driver remains only for the editor test probes).

## Tuning workflow

1. Change `RaftTuning.asset` (in Play Mode if you like).
2. Run *Tools > Fart Online > Tests > Run Physics Self-Test* (19 checks: directions, spacing, impulses, gas, collision, recovery). The thresholds are in `Assets/Game/Tests/PhysicsSelfTest.cs`.
3. Run *Run Jump Probe (section E)*: drift, stream and charged attempts at the gap.
4. Run *Run Course Run (AI crew 1 and 4)*: a full-course completion check, with stall points logged.

## Course geometry that interacts with tuning

- **Narrowest water:** 9.5 m (canyon B). The round raft is 4.8 m across.
- **LowBeam_2:** a slalom beam reaching 40% of the width at crew height (0.95 m). The hull slides under it; the crew (clearance top 1.3 m) does not.
- **OverheadBeam_1:** full width, bottom at 1.5 m. Clears an upright raft; catches the crew if the raft is pitched or rolled.
- **Jump (section E):** the chute flow reaches 8.5 m/s, with a rock kicker at the lip. The dry span is about 7 m.
  - A river segment is wet only when **both** of its points are wet, so dry points must be flanked closely by wet points.
  - With the fast-water grip, even a drift clears the gap. Only a raft slowed by braking or reverse farting falls short.

**2026-10-31:** thrust raised about 15% (above) together with the harder endless river (`Endless_Mode.md`, "Hard mode tuning"). Physics self-test: 19/19.
