using System;
using System.Text;

namespace FartOnline.Game.Net
{
    /// <summary>
    /// Short, typeable room codes for direct-IP sessions: the host's IPv4 address (and the port, when it is not the
    /// default 7777) in base 32 without look-alike characters, e.g. 192.168.1.23 -> "GAP-4QR9". Friends type the
    /// code (or the plain address) into the Join box. No server is involved: a code only says where the host is.
    /// </summary>
    public static class RoomCode
    {
        const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // no I, O, 0, 1
        public const ushort DefaultPort = 7777;

        public static string Encode(string ipv4, ushort port)
        {
            var parts = ipv4.Split('.');
            if (parts.Length != 4) return "";
            ulong v = 0;
            foreach (var p in parts)
            {
                if (!byte.TryParse(p, out byte b)) return "";
                v = (v << 8) | b;
            }
            int bits = 32;
            if (port != DefaultPort) { v = (v << 16) | port; bits = 48; }
            int chars = (bits + 4) / 5;
            var sb = new StringBuilder();
            for (int i = chars - 1; i >= 0; i--) sb.Append(Alphabet[(int)((v >> (i * 5)) & 31)]);
            string s = sb.ToString();
            return s.Substring(0, 3) + "-" + s.Substring(3);
        }

        /// <summary>Accepts a room code, "address" or "address:port".</summary>
        public static bool TryParse(string text, out string address, out ushort port)
        {
            address = "";
            port = DefaultPort;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            if (TryDecode(text, out address, out port)) return true;
            int colon = text.LastIndexOf(':');
            if (colon > 0 && ushort.TryParse(text.Substring(colon + 1), out ushort p)) { port = p; text = text.Substring(0, colon); }
            address = text;
            return true;
        }

        static bool TryDecode(string code, out string address, out ushort port)
        {
            address = "";
            port = DefaultPort;
            string s = code.Replace("-", "").Replace(" ", "").ToUpperInvariant();
            if (s.Length != 7 && s.Length != 10) return false;
            if (s.Contains(".")) return false;
            ulong v = 0;
            foreach (char c in s)
            {
                int k = Alphabet.IndexOf(c);
                if (k < 0) return false;
                v = (v << 5) | (uint)k;
            }
            if (s.Length == 10) { port = (ushort)(v & 0xFFFF); v >>= 16; }
            if (v > uint.MaxValue) return false;
            address = $"{(v >> 24) & 255}.{(v >> 16) & 255}.{(v >> 8) & 255}.{v & 255}";
            return true;
        }

        /// <summary>This machine's most likely LAN address (an up, non-loopback IPv4 interface with a gateway), or "".</summary>
        public static string LocalLanAddress()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return "";
#else
            try
            {
                string best = "";
                int bestScore = -1;
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                    var props = ni.GetIPProperties();
                    bool gateway = false;
                    foreach (var g in props.GatewayAddresses)
                        if (g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any)) gateway = true;
                    string desc = (ni.Description + " " + ni.Name).ToLowerInvariant();
                    bool virt = desc.Contains("virtual") || desc.Contains("vethernet") || desc.Contains("hyper-v") || desc.Contains("vmware") || desc.Contains("vpn");
                    foreach (var ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                        string ip = ua.Address.ToString();
                        if (ip.StartsWith("169.254.")) continue;
                        bool priv = ip.StartsWith("192.168.") || ip.StartsWith("10.") || IsPrivate172(ip);
                        int score = (gateway ? 4 : 0) + (priv ? 2 : 0) + (virt ? 0 : 1);
                        if (score > bestScore) { bestScore = score; best = ip; }
                    }
                }
                return best;
            }
            catch (Exception) { return ""; }
#endif
        }

        static bool IsPrivate172(string ip)
        {
            if (!ip.StartsWith("172.")) return false;
            var p = ip.Split('.');
            return p.Length == 4 && int.TryParse(p[1], out int b) && b >= 16 && b <= 31;
        }
    }
}
