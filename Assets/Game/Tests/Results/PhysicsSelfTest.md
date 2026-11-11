# Physics self-test (round raft)

Run: 2026-11-05 01:47, Unity 6000.5.6f1, fixed step 0.0200 s.
Result: **19 passed, 0 failed**.

- Round raft radius 2.4 m, seats at 1.65 m, mass 450 kg, thrust 2400 N per player, tap 1000 N·s, charged 1550-4000 N·s, brake 2000 N·s, rim walk 105°/s
- PASS  Farting from the back of the round raft pushes it forward  [Δv forward 4.43, sideways -0.02 m/s]
- PASS  Thrust through the centre does not spin the raft  [yaw rate 0.00 rad/s]
- PASS  D walks the player to their right along the rim  [angle 180 -> 127 (-53°, expected ≈ -53°)]
- PASS  A walks the player back to the left  [angle 180]
- PASS  Farting from the right side pushes the raft left  [Δv right -3.93, forward 0.15 m/s]
- PASS  Farting from the front pushes the raft backward (brake / reverse)  [Δv forward -3.69 m/s]
- PASS  Two players at the back combine into a stronger forward push  [Δv forward 6.14, sideways -0.02 m/s, yaw 0.00]
- PASS  Players on opposite sides cancel each other out  [Δv 0.14 / 0.00 m/s]
- PASS  Players cannot walk through each other (minimum seat spacing)  [gap 42.0° (min 42°)]
- PASS  A diagonal seat pushes the raft diagonally (forward + left)  [Δv forward 2.63, right -2.54 m/s]
- PASS  Tapping F gives a short puff impulse  [Δv 1.23 m/s (ideal 2.22)]
- PASS  Charged burst is much stronger than a tap  [charge 1.00, Δv 5.65 m/s]
- PASS  Brake burst slows the raft from any seat  [speed 3.92 -> 1.01 m/s]
- PASS  Gas tank empties under sustained use and blocks thrust while exhausted  [exhausted True, force while exhausted False]
- PASS  Gas regenerates after a pause  [gas 0.59]
- PASS  Pushing into the canyon wall produces an impact and keeps the raft in play  [impacts 1, heavy False, wipeouts +0]
- PASS  Falling below the course triggers a wipeout that disables thrust  [reason fell, thrust blocked True]
- PASS  Wipeout respawns at the last checkpoint and resumes play  [distance to respawn 0.36 m, state Playing]
- PASS  Capsizing past the safe angle triggers recovery  [reason capsized]
