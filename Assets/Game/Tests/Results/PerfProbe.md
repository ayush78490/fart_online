# Perf probe (Mobile quality level, editor, AI crew of 3, 30 s)

| When | Label | Avg frame (ms) | p95 (ms) | Max (ms) | Draw calls | SetPass | Triangles | Renderers | Max chunk build (ms) | Crashes |
|---|---|---|---|---|---|---|---|---|---|---|
| 11-05 01:59 | baseline (before optimisation) | 3.9 | 4.6 | 40.4 | 827 | 82 | 727k | 832 | 5.9 | 1 |
| 11-05 02:05 | toy parts merged + chunk static batching | 3.8 | 4.6 | 19.5 | 406 | 79 | 728k | 524 | 6.3 | 0 |
| 11-05 02:07 | + light gate poles, low-poly rollers, no pig shadows (mobile) | 3.7 | 4.4 | 18.0 | 401 | 86 | 642k | 528 | 6.6 | 1 |
| 11-05 02:09 | + static bank rollers on mobile, no roller posts | 3.6 | 4.3 | 22.2 | 343 | 82 | 590k | 493 | 7.3 | 1 |
| 11-05 02:11 | + chunk build split over 2 frames, 25 m shadows | 3.6 | 4.2 | 23.0 | 327 | 86 | 607k | 478 | 7.0 | 0 |

Note: editor frame times include editor overhead; draw calls, triangles and renderers carry over to devices. The 'Max chunk build' column is the slowest single build step.
