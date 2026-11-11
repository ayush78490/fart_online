using System.IO;
using System.Text;
using Concentus.Enums;
using Concentus.Structs;
using FartOnline.Game.Net;
using UnityEditor;
using UnityEngine;

namespace FartOnline.GameEditor
{
    /// <summary>
    /// Menu Tools > Fart Online > Tests > Run Online Self-Test: offline checks for the online layer.
    ///  1. Opus (Concentus) round trip: a 440 Hz tone, 20 ms frames at 16 kHz, encoded, decoded, packet loss concealed;
    ///     reports bytes per frame and the signal-to-noise ratio after decoding.
    ///  2. Join tickets: tickets signed by the Node room service (Temp/fo_tickets.txt: a valid one, then an expired one,
    ///     made with the test secret below) must verify / be rejected by RoomTicketVerifier, and a tampered or
    ///     wrong-room ticket must fail.
    /// </summary>
    public static class OnlineSelfTest
    {
        public const string TestSecret = "test-secret-0123456789abcdefghijklmnopqrstuvwxyz";

        [MenuItem("Tools/Fart Online/Tests/Run Online Self-Test")]
        public static void RunMenu() => Debug.Log("[OnlineSelfTest]\n" + Run());

        public static string Run()
        {
            var sb = new StringBuilder();
            int fails = 0;

            // ---- 1. Opus
            var enc = OpusEncoder.Create(16000, 1, OpusApplication.OPUS_APPLICATION_VOIP);
            enc.Bitrate = 16000;
            enc.Complexity = 5;
            var dec = OpusDecoder.Create(16000, 1);
            const int frames = 50, n = 320;
            var pkt = new byte[200];
            var outp = new short[n];
            double sig = 0, noise = 0;
            int bytes = 0;
            var history = new short[n * frames];
            var decoded = new short[n * frames];
            for (int f = 0; f < frames; f++)
            {
                var pcm = new short[n];
                for (int i = 0; i < n; i++) pcm[i] = (short)(9000 * System.Math.Sin(2 * System.Math.PI * 440 * (f * n + i) / 16000.0));
                System.Array.Copy(pcm, 0, history, f * n, n);
                int len = enc.Encode(pcm, 0, n, pkt, 0, pkt.Length);
                bytes += len;
                int got = dec.Decode(pkt, 0, len, outp, 0, n, false);
                System.Array.Copy(outp, 0, decoded, f * n, got);
            }
            // Opus has a small algorithmic delay: align by the best lag before measuring
            int bestLag = 0; double bestErr = double.MaxValue;
            for (int lag = 0; lag < 400; lag++)
            {
                double e = 0;
                for (int i = 8000; i < 12000; i++) { double d = decoded[i + lag] - history[i]; e += d * d; }
                if (e < bestErr) { bestErr = e; bestLag = lag; }
            }
            for (int i = 8000; i < 12000; i++) { sig += (double)history[i] * history[i]; double d = decoded[i + bestLag] - history[i]; noise += d * d; }
            double snr = 10 * System.Math.Log10(sig / System.Math.Max(1, noise));
            int plc = dec.Decode(null, 0, 0, outp, 0, n, false);
            bool opusOk = bytes / frames > 10 && bytes / frames < 80 && snr > 10 && plc == n;
            if (!opusOk) fails++;
            sb.AppendLine($"Opus: {bytes / frames} bytes/frame ({bytes * 8 / (frames * 0.02) / 1000:F1} kbit/s), SNR {snr:F1} dB (lag {bestLag}), PLC frame {plc} samples -> {(opusOk ? "PASS" : "FAIL")}");

            // ---- 2. tickets from the Node service
            string path = Path.Combine(Application.dataPath, "../Temp/fo_tickets.txt");
            if (!File.Exists(path)) { sb.AppendLine("Tickets: Temp/fo_tickets.txt missing (make it with node, see the class comment) -> SKIP"); }
            else
            {
                var lines = File.ReadAllLines(path);
                bool ok1 = RoomTicketVerifier.Verify(lines[0].Trim(), TestSecret, "ABC123", out var t, out string e1) && t.Username == "pig_tester" && t.Owner;
                bool ok2 = !RoomTicketVerifier.Verify(lines[1].Trim(), TestSecret, "ABC123", out _, out string e2);
                bool ok3 = !RoomTicketVerifier.Verify(lines[0].Trim(), TestSecret, "XYZ999", out _, out string e3);
                string tampered = lines[0].Trim();
                tampered = tampered.Substring(0, 10) + (tampered[10] == 'A' ? 'B' : 'A') + tampered.Substring(11);
                bool ok4 = !RoomTicketVerifier.Verify(tampered, TestSecret, "ABC123", out _, out string e4);
                bool ok5 = !RoomTicketVerifier.Verify(lines[0].Trim(), TestSecret + "x", "ABC123", out _, out _);
                sb.AppendLine($"Ticket from Node verifies: {(ok1 ? "PASS" : "FAIL " + e1)}");
                sb.AppendLine($"Expired ticket rejected: {(ok2 ? "PASS (" + e2 + ")" : "FAIL")}");
                sb.AppendLine($"Wrong room rejected: {(ok3 ? "PASS (" + e3 + ")" : "FAIL")}");
                sb.AppendLine($"Tampered ticket rejected: {(ok4 ? "PASS" : "FAIL")}");
                sb.AppendLine($"Wrong secret rejected: {(ok5 ? "PASS" : "FAIL")}");
                if (!ok1) fails++; if (!ok2) fails++; if (!ok3) fails++; if (!ok4) fails++; if (!ok5) fails++;
            }
            sb.AppendLine(fails == 0 ? "ALL PASS" : fails + " FAILED");
            return sb.ToString();
        }
    }
}
