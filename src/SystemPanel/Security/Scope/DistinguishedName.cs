using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SystemPanel.Security.Scope
{
    /// <summary>
    /// RFC 4514 kaçış kurallarına uyan, değişmez (immutable) bir LDAP Distinguished Name.
    ///
    /// Scope (il/birim) güvenlik sınırı DN hiyerarşisi üzerinden uygulandığı için DN'ler
    /// asla düz metin olarak (<c>EndsWith</c>, <c>Contains</c>, virgülle bölme) karşılaştırılmaz:
    /// <c>CN=x\,OU=ANKARA,OU=IL,DC=d</c> nesnesi ANKARA altında DEĞİLDİR, doğrudan OU=IL altındadır.
    /// </summary>
    public sealed class DistinguishedName : IEquatable<DistinguishedName>
    {
        private readonly Rdn[] _rdns;

        private DistinguishedName(Rdn[] rdns) => _rdns = rdns;

        /// <summary>Soldan sağa (yaprak → kök) RDN'ler.</summary>
        public IReadOnlyList<Rdn> Rdns => _rdns;

        public int Depth => _rdns.Length;

        public static DistinguishedName Parse(string value)
            => TryParse(value, out var dn) ? dn : throw new FormatException("Geçersiz distinguished name.");

        public static bool TryParse(string? value, [NotNullWhen(true)] out DistinguishedName? dn)
        {
            dn = null;
            if (string.IsNullOrWhiteSpace(value) || value.Length > 2048)
                return false;

            var rdns = new List<Rdn>();
            var current = new StringBuilder();
            var raw = value.Trim();

            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '\\')
                {
                    // Kaçış dizisini olduğu gibi taşı; değer çözümlemesi Rdn.TryParse'ta yapılır.
                    if (i + 1 >= raw.Length) return false;
                    current.Append(c).Append(raw[i + 1]);
                    i++;
                }
                else if (c == ',' || c == ';')
                {
                    if (!Rdn.TryParse(current.ToString(), out var rdn)) return false;
                    rdns.Add(rdn);
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            if (!Rdn.TryParse(current.ToString(), out var last)) return false;
            rdns.Add(last);

            dn = new DistinguishedName(rdns.ToArray());
            return true;
        }

        /// <summary>Bu DN, <paramref name="ancestor"/> ile aynı veya onun altında mı?</summary>
        public bool IsWithin(DistinguishedName ancestor)
        {
            if (ancestor._rdns.Length > _rdns.Length)
                return false;

            int offset = _rdns.Length - ancestor._rdns.Length;
            for (int i = 0; i < ancestor._rdns.Length; i++)
            {
                if (!_rdns[offset + i].Equals(ancestor._rdns[i]))
                    return false;
            }

            return true;
        }

        /// <summary>Kesin alt düğüm (kendisi hariç).</summary>
        public bool IsDescendantOf(DistinguishedName ancestor)
            => _rdns.Length > ancestor._rdns.Length && IsWithin(ancestor);

        public DistinguishedName? Parent
            => _rdns.Length <= 1 ? null : new DistinguishedName(_rdns.Skip(1).ToArray());

        public bool Equals(DistinguishedName? other)
            => other is not null && other._rdns.Length == _rdns.Length && IsWithin(other);

        public override bool Equals(object? obj) => Equals(obj as DistinguishedName);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var r in _rdns) hash.Add(r);
            return hash.ToHashCode();
        }

        /// <summary>Kanonik (kaçışlı) dize gösterimi.</summary>
        public override string ToString() => string.Join(",", _rdns.Select(r => r.ToString()));

        /// <summary>Tek bir RDN (çok değerli RDN'ler "+" ile bütün halinde karşılaştırılır).</summary>
        public readonly struct Rdn : IEquatable<Rdn>
        {
            private Rdn(string type, string value, string normalizedValue)
            {
                Type = type;
                Value = value;
                _normalized = normalizedValue;
            }

            private readonly string _normalized;

            /// <summary>Büyük harfe çevrilmiş öznitelik tipi (OU, CN, DC...).</summary>
            public string Type { get; }

            /// <summary>Kaçışları çözülmüş ham değer.</summary>
            public string Value { get; }

            internal static bool TryParse(string text, out Rdn rdn)
            {
                rdn = default;
                var s = text.Trim();
                int eq = IndexOfUnescaped(s, '=');
                if (eq <= 0) return false;

                var type = s.Substring(0, eq).Trim();
                if (type.Length == 0 || !type.All(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '.'))
                    return false;

                if (!TryUnescape(s.Substring(eq + 1).Trim(), out var value) || value.Length == 0)
                    return false;

                rdn = new Rdn(type.ToUpperInvariant(), value, value.ToUpperInvariant());
                return true;
            }

            private static int IndexOfUnescaped(string s, char target)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    if (s[i] == '\\') { i++; continue; }
                    if (s[i] == target) return i;
                }
                return -1;
            }

            private static bool TryUnescape(string s, out string value)
            {
                value = string.Empty;
                if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
                    s = s.Substring(1, s.Length - 2);

                var bytes = new List<byte>();
                var sb = new StringBuilder();

                void FlushBytes()
                {
                    if (bytes.Count == 0) return;
                    sb.Append(Encoding.UTF8.GetString(bytes.ToArray()));
                    bytes.Clear();
                }

                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    if (c != '\\')
                    {
                        FlushBytes();
                        sb.Append(c);
                        continue;
                    }

                    if (i + 1 >= s.Length) return false;

                    if (i + 2 < s.Length && IsHex(s[i + 1]) && IsHex(s[i + 2]))
                    {
                        bytes.Add(byte.Parse(s.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 2;
                    }
                    else
                    {
                        FlushBytes();
                        sb.Append(s[i + 1]);
                        i++;
                    }
                }

                FlushBytes();
                value = sb.ToString();
                return true;
            }

            private static bool IsHex(char c) => Uri.IsHexDigit(c);

            public bool Equals(Rdn other)
                => string.Equals(Type, other.Type, StringComparison.Ordinal)
                   && string.Equals(_normalized, other._normalized, StringComparison.Ordinal);

            public override bool Equals(object? obj) => obj is Rdn r && Equals(r);

            public override int GetHashCode() => HashCode.Combine(Type, _normalized);

            public override string ToString() => $"{Type}={Escape(Value)}";

            internal static string Escape(string value)
            {
                var sb = new StringBuilder(value.Length + 8);
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    bool special = c is ',' or '+' or '"' or '\\' or '<' or '>' or ';' or '='
                                   || (i == 0 && (c == ' ' || c == '#'))
                                   || (i == value.Length - 1 && c == ' ');
                    if (special) sb.Append('\\');
                    sb.Append(c);
                }
                return sb.ToString();
            }
        }
    }
}
