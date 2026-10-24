using DomainLayer.Entities;
using DomainLayer.Enumerations;
using System.Globalization;

namespace DomainLayer.Services
{
    public sealed record ConditionResult(bool Ok, string Text);

    public static class ConditionEvaluator
    {
        public static readonly IReadOnlyDictionary<ConditionOperator, string> Labels = new Dictionary<ConditionOperator, string>
        {
            [ConditionOperator.Eq] = "==",
            [ConditionOperator.Neq] = "!=",
            [ConditionOperator.In] = "in",
            [ConditionOperator.NotIn] = "not in",
            [ConditionOperator.Gt] = ">",
            [ConditionOperator.Gte] = ">=",
            [ConditionOperator.Lt] = "<",
            [ConditionOperator.Lte] = "<=",
            [ConditionOperator.Contains] = "contains",
            [ConditionOperator.StartsWith] = "starts with",
            [ConditionOperator.InSchedule] = "within schedule",
            [ConditionOperator.NotInSchedule] = "outside schedule",
        };

        private static bool Eqi(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        // numbers compare numerically, everything else case-insensitively ("HH:mm" times sort correctly as text)
        private static int Compare(string a, string b)
        {
            if (decimal.TryParse(a, NumberStyles.Number, CultureInfo.InvariantCulture, out var na) &&
                decimal.TryParse(b, NumberStyles.Number, CultureInfo.InvariantCulture, out var nb))
                return na.CompareTo(nb);
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public static ConditionResult Evaluate(PolicyCondition c, IReadOnlyDictionary<string, string> ctx, DateTime at,
            IReadOnlyList<Schedule> schedules, IReadOnlyList<Holiday> holidays)
        {
            if (c.Operator is ConditionOperator.InSchedule or ConditionOperator.NotInSchedule)
            {
                var s = schedules.FirstOrDefault(x => x.Id.ToString() == c.Value || string.Equals(x.Code, c.Value, StringComparison.OrdinalIgnoreCase));
                if (s == null) return new ConditionResult(false, $"schedule '{c.Value}' not found");
                ctx.TryGetValue("user.region", out var region);
                var inside = ScheduleEvaluator.IsInSchedule(s, at, holidays, region);
                var ok = c.Operator == ConditionOperator.InSchedule ? inside : !inside;
                return new ConditionResult(ok, $"{ctx["env.day"]} {ctx["env.time"]} {(inside ? "is within" : "is outside")} {s.Name}");
            }

            ctx.TryGetValue(c.Attribute, out var left);
            string? right = c.ValueIsAttribute ? (ctx.TryGetValue(c.Value, out var r) ? r : null) : c.Value;
            var shownRight = c.ValueIsAttribute ? $"{c.Value} ({right ?? "missing"})" : $"'{c.Value}'";
            var text = $"{c.Attribute} ({left ?? "missing"}) {Labels[c.Operator]} {shownRight}";

            // missing attributes never satisfy a condition (fail closed)
            if (left is null || right is null) return new ConditionResult(false, text);

            var list = right.Split(',').Select(x => x.Trim()).ToList();
            var lefts = left.Split(',');
            var result = c.Operator switch
            {
                ConditionOperator.Eq => Eqi(left, right),
                ConditionOperator.Neq => !Eqi(left, right),
                ConditionOperator.In => lefts.Any(l => list.Any(x => Eqi(l, x))),
                ConditionOperator.NotIn => !lefts.Any(l => list.Any(x => Eqi(l, x))),
                ConditionOperator.Gt => Compare(left, right) > 0,
                ConditionOperator.Gte => Compare(left, right) >= 0,
                ConditionOperator.Lt => Compare(left, right) < 0,
                ConditionOperator.Lte => Compare(left, right) <= 0,
                ConditionOperator.Contains => left.Contains(right, StringComparison.OrdinalIgnoreCase),
                ConditionOperator.StartsWith => left.StartsWith(right, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
            return new ConditionResult(result, text);
        }

        /// <summary>Human readable rule, as shown in the admin panel and stored in Policies.RuleExpression.</summary>
        public static string Expression(Policy p, IReadOnlyList<Schedule> schedules)
        {
            if (p.Conditions.Count == 0) return "true";
            var parts = p.Conditions.Select(c =>
            {
                if (c.Operator is ConditionOperator.InSchedule or ConditionOperator.NotInSchedule)
                {
                    var code = schedules.FirstOrDefault(x => x.Id.ToString() == c.Value)?.Code ?? c.Value;
                    var fn = $"InSchedule(env.now, '{code}')";
                    return c.Operator == ConditionOperator.InSchedule ? fn : "!" + fn;
                }
                string value;
                if (c.ValueIsAttribute) value = c.Value;
                else if (c.Operator is ConditionOperator.In or ConditionOperator.NotIn)
                    value = "[" + string.Join(", ", c.Value.Split(',').Select(x => $"'{x.Trim()}'")) + "]";
                else value = decimal.TryParse(c.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out _) ? c.Value : $"'{c.Value}'";
                return $"{c.Attribute} {Labels[c.Operator]} {value}";
            });
            return string.Join(p.Match == ConditionMatch.All ? " && " : " || ", parts);
        }
    }
}
