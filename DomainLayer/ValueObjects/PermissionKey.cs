using DomainLayer.Exceptions;
using System.Text.RegularExpressions;

namespace DomainLayer.ValueObjects
{
    /// <summary>
    /// A permission is written APP.MODULE.RESOURCE.ACTION (upper case). Any segment may be "*",
    /// e.g. POS.BILLING.INVOICE.REFUND, POS.*.*.*, *.*.*.VIEW
    /// </summary>
    public sealed class PermissionKey : ValueObject
    {
        private static readonly Regex Pattern = new(@"^[A-Z0-9_*-]+\.[A-Z0-9_*-]+\.[A-Z0-9_*-]+\.[A-Z*]+$", RegexOptions.Compiled);

        public string Value { get; }

        private PermissionKey(string value) => Value = value;

        public static PermissionKey Create(string value)
        {
            var key = (value ?? string.Empty).Trim().ToUpperInvariant();
            if (!Pattern.IsMatch(key))
                throw new DomainException($"Permission '{value}' must have four segments: APP.MODULE.RESOURCE.ACTION");
            return new PermissionKey(key);
        }

        public static string Of(string app, string module, string resource, string action) =>
            $"{app}.{module}.{resource}.{action}".ToUpperInvariant();

        /// <summary>
        /// True when <paramref name="granted"/> (which may contain "*") applies to <paramref name="requested"/>.
        /// A "*" in the requested key means "any", so POS.*.*.* asks "does the user have anything in POS?".
        /// </summary>
        public static bool Matches(string granted, string requested)
        {
            var g = granted.ToUpperInvariant().Split('.');
            var r = requested.ToUpperInvariant().Split('.');
            if (g.Length != 4 || r.Length != 4) return false;
            for (var i = 0; i < 4; i++)
                if (g[i] != "*" && r[i] != "*" && g[i] != r[i]) return false;
            return true;
        }

        /// <summary>Stricter than <see cref="Matches"/>: a "*" in the requested key is only covered by a "*" in the grant.</summary>
        public static bool Covers(string granted, string requested)
        {
            var g = granted.ToUpperInvariant().Split('.');
            var r = requested.ToUpperInvariant().Split('.');
            if (g.Length != 4 || r.Length != 4) return false;
            for (var i = 0; i < 4; i++)
                if (g[i] != "*" && g[i] != r[i]) return false;
            return true;
        }

        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Value;
        }

        public override string ToString() => Value;
    }
}
