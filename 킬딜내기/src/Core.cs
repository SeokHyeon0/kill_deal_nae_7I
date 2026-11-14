using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace PubgScore
{
    public sealed class CellValue
    {
        public string Value { get; set; }
        public int Version { get; set; }
    }

    public sealed class ScoreState
    {
        public Dictionary<string, CellValue> Cells { get; set; }
        public ScoreState() { Cells = new Dictionary<string, CellValue>(StringComparer.Ordinal); }
        public string Get(string key, string fallback)
        {
            CellValue value;
            return Cells != null && Cells.TryGetValue(key, out value) && value != null && value.Value != null
                ? value.Value : fallback;
        }
        public string Get(string key)
        {
            string value = Get(key, null);
            return value ?? ScoreRules.DefaultValue(key);
        }
        public int GetInt(string key, int fallback)
        {
            int result;
            return Int32.TryParse(Get(key, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : fallback;
        }
        public decimal GetDecimal(string key, decimal fallback)
        {
            decimal result;
            return ScoreRules.TryDecimal(Get(key, ""), out result) ? result : fallback;
        }
        public int Version(string key)
        {
            CellValue value;
            return Cells != null && Cells.TryGetValue(key, out value) && value != null ? value.Version : 0;
        }
        public int RoundCount { get { return Math.Max(ScoreRules.DefaultRounds, GetInt("roundCount", ScoreRules.DefaultRounds)); } }
        public ScoreState Copy()
        {
            ScoreState copy = new ScoreState();
            foreach (KeyValuePair<string, CellValue> item in Cells)
                copy.Cells.Add(item.Key, new CellValue { Value = item.Value.Value, Version = item.Value.Version });
            return copy;
        }
    }

    public static class ScoreRules
    {
        public const int TeamCount = 10;
        public const int PlayerCount = 4;
        public const int DefaultRounds = 50;
        public const int MaximumRounds = 179;
        public static readonly int[] ChickenOptions = { -5, -4, -3, -2, -1, 0, 1, 2, 3, 4, 5, 6, 8, 10, 300, 500, 800 };
        private static readonly Regex TeamKey = new Regex(@"^t([0-9])\.(name|reference|status)$", RegexOptions.CultureInvariant);
        private static readonly Regex PlayerKey = new Regex(@"^t([0-9])\.p([0-3])\.(name|penalty)$", RegexOptions.CultureInvariant);
        private static readonly Regex RoundKey = new Regex(@"^t([0-9])\.r([1-9][0-9]{0,2})\.(chicken|p([0-3])\.(score|kills|damage|check))$", RegexOptions.CultureInvariant);
        private static readonly Regex RequestIdPattern = new Regex(@"^[A-Za-z0-9_-]{1,128}$", RegexOptions.CultureInvariant);

        public static string DefaultValue(string key)
        {
            if (key == "roundCount") return DefaultRounds.ToString(CultureInfo.InvariantCulture);
            if (key == "buddySize") return "1";
            Match match = TeamKey.Match(key ?? "");
            if (match.Success)
            {
                if (match.Groups[2].Value == "name") return (Int32.Parse(match.Groups[1].Value) + 1).ToString(CultureInfo.InvariantCulture) + "팀";
                if (match.Groups[2].Value == "status") return "ready";
                return "23";
            }
            match = PlayerKey.Match(key ?? "");
            if (match.Success) return "";
            return key != null && key.EndsWith(".check", StringComparison.Ordinal) ? "0" : "";
        }

        public static bool TryDecimal(string value, out decimal result)
        {
            if (!Decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out result)) return false;
            // Decimal.TryParse otherwise silently rounds nonzero values below its precision to zero.
            if (result != 0m) return true;
            for (int index = 0; index < value.Length; index++)
                if (value[index] >= '1' && value[index] <= '9') return false;
            return true;
        }

        public static void ValidateRound(string key, int roundCount)
        {
            Match match = RoundKey.Match(key ?? "");
            if (match.Success && Int32.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) > roundCount)
                throw new ArgumentException("현재 방의 판수를 벗어난 기록은 저장할 수 없습니다.");
        }

        public static string NormalizeStored(string key, object value)
        {
            if (value is bool)
            {
                if (!key.EndsWith(".check", StringComparison.Ordinal)) throw new ArgumentException("숫자 형식이 아닙니다.");
                return Normalize(key, (bool)value ? "1" : "0");
            }
            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (value is double || value is float || value is decimal)
            {
                decimal number;
                if (!Decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) ||
                    (number == 0m && Convert.ToDouble(value, CultureInfo.InvariantCulture) != 0d))
                    throw new ArgumentException("표시할 수 없는 숫자입니다.");
                text = number.ToString("0.############################", CultureInfo.InvariantCulture);
            }
            // Retain compatibility with existing snapshots; this removed setting cannot be written.
            if (key == "endTime")
            {
                if (text.Length != 0 && !Regex.IsMatch(text, @"\A(?:[01][0-9]|2[0-3]):[0-5][0-9]\z", RegexOptions.CultureInvariant))
                    throw new ArgumentException("저장된 종료시간 값이 올바르지 않습니다.");
                return text;
            }
            return Normalize(key, text);
        }

        public static string Normalize(string key, string value)
        {
            if (key == null || value == null || key.Length > 40 || value.Length > 100)
                throw new ArgumentException("입력 항목 또는 값이 올바르지 않습니다.");
            if (key == "buddySize")
            {
                int size;
                if (value.Length != 1 || !Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out size) || size < 1 || size > 5)
                    throw new ArgumentException("깐부모드는 해제 또는 2~5팀을 선택하세요.");
                return size.ToString(CultureInfo.InvariantCulture);
            }
            if (key == "roundCount")
            {
                int rounds;
                if (!Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out rounds) || rounds < 20 || rounds > MaximumRounds)
                    throw new ArgumentException("라운드 수는 20~179 사이여야 합니다.");
                return rounds.ToString(CultureInfo.InvariantCulture);
            }
            Match match = TeamKey.Match(key);
            if (match.Success)
            {
                string type = match.Groups[2].Value;
                if (type == "name") return NormalizeName(value);
                if (type == "reference") return NormalizeNumber(value, -999999m, 999999m);
                if (value != "ready" && value != "live" && value != "done") throw new ArgumentException("상태 값이 올바르지 않습니다.");
                return value;
            }
            match = PlayerKey.Match(key);
            if (match.Success)
                return match.Groups[3].Value == "name" ? NormalizeName(value) : NormalizeNumber(value, 0m, 999999m);
            match = RoundKey.Match(key);
            if (match.Success && Int32.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) <= MaximumRounds)
            {
                if (match.Groups[3].Value == "chicken")
                {
                    if (value.Length == 0) return value;
                    int chicken;
                    if (!Int32.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out chicken) || Array.IndexOf(ChickenOptions, chicken) < 0)
                        throw new ArgumentException("허용된 치킨 가감점 중에서 선택해 주세요.");
                    return chicken.ToString(CultureInfo.InvariantCulture);
                }
                if (match.Groups[5].Value == "check")
                {
                    if (value != "0" && value != "1") throw new ArgumentException("체크 값은 0 또는 1이어야 합니다.");
                    return value;
                }
                if (match.Groups[5].Value == "kills" || match.Groups[5].Value == "damage")
                {
                    if (value.Length == 0) return "";
                    decimal number;
                    if (!TryDecimal(value, out number) || number < 0m || number > 999999m)
                        throw new ArgumentException("킬과 딜은 0~999999 범위의 숫자를 입력하세요.");
                    if (match.Groups[5].Value == "kills" && Decimal.Truncate(number) != number)
                        throw new ArgumentException("킬은 0~999999 범위의 정수를 입력하세요.");
                    if (match.Groups[5].Value == "damage")
                    {
                        // Firebase numbers use double precision. Reject values whose
                        // round-trip rounding would cross a 100-damage score boundary.
                        decimal storedNumber;
                        double wireNumber = (double)number;
                        if (!Decimal.TryParse(Convert.ToString(wireNumber, CultureInfo.InvariantCulture),
                            NumberStyles.Float, CultureInfo.InvariantCulture, out storedNumber) ||
                            (storedNumber == 0m && wireNumber != 0d) ||
                            Decimal.Floor(number / 100m) != Decimal.Floor(storedNumber / 100m))
                            throw new ArgumentException("딜의 소수 자릿수를 줄여서 입력하세요. 저장 시 점수가 달라지는 값은 사용할 수 없습니다.");
                    }
                    return number.ToString("0.############################", CultureInfo.InvariantCulture);
                }
                return NormalizeNumber(value, -999999m, 999999m);
            }
            throw new ArgumentException("허용되지 않은 입력 항목입니다.");
        }

        public static void ValidateRequest(ChangeRequest request)
        {
            if (request == null || request.Version < 0 || request.RequestId == null || !RequestIdPattern.IsMatch(request.RequestId))
                throw new ArgumentException("저장 요청 정보가 올바르지 않습니다.");
            Normalize(request.Key, request.Value);
        }
        private static string NormalizeName(string value)
        {
            if (value.Length > 60) throw new ArgumentException("이름은 60자 이하로 입력해 주세요.");
            foreach (char character in value)
                if (Char.IsControl(character)) throw new ArgumentException("이름에 줄바꿈이나 제어 문자를 넣을 수 없습니다.");
            return value.Trim();
        }
        private static string NormalizeNumber(string value, decimal minimum, decimal maximum)
        {
            if (value.Length == 0) return "";
            decimal number;
            if (!TryDecimal(value, out number) || number < minimum || number > maximum)
                throw new ArgumentException("숫자는 " + minimum.ToString(CultureInfo.InvariantCulture) + "~" + maximum.ToString(CultureInfo.InvariantCulture) + " 범위로 입력해 주세요.");
            return number.ToString("0.############################", CultureInfo.InvariantCulture);
        }
    }

    public sealed class TeamSummary
    {
        public string Name { get; set; }
        public string[] Names { get; set; }
        public decimal[] Raw { get; set; }
        public decimal[] Deductions { get; set; }
        public decimal[] PlayerTotals { get; set; }
        public decimal[] RoundTotals { get; set; }
        public decimal ChickenSum { get; set; }
        public decimal Total { get; set; }
    }

    public static class ScoreMath
    {
        static readonly string[] NameSuffixes = { ".p0.name", ".p1.name", ".p2.name", ".p3.name" };
        static readonly string[] PenaltySuffixes = { ".p0.penalty", ".p1.penalty", ".p2.penalty", ".p3.penalty" };
        static readonly string[] ScoreSuffixes = { ".p0.score", ".p1.score", ".p2.score", ".p3.score" };
        static readonly string[] KillSuffixes = { ".p0.kills", ".p1.kills", ".p2.kills", ".p3.kills" };
        static readonly string[] DamageSuffixes = { ".p0.damage", ".p1.damage", ".p2.damage", ".p3.damage" };
        static readonly string[] CheckSuffixes = { ".p0.check", ".p1.check", ".p2.check", ".p3.check" };
        public static decimal RawScore(ScoreState state, int team, int round, int player, string mode = "")
        {
            if (state == null) throw new ArgumentNullException("state");
            if (team < 0 || team >= ScoreRules.TeamCount) throw new ArgumentOutOfRangeException("team");
            if (round < 1 || round > ScoreRules.MaximumRounds) throw new ArgumentOutOfRangeException("round");
            if (player < 0 || player >= ScoreRules.PlayerCount) throw new ArgumentOutOfRangeException("player");
            return RoundPlayerScore(state, "t" + team.ToString(CultureInfo.InvariantCulture) + ".r" + round.ToString(CultureInfo.InvariantCulture), player, mode);
        }
        static decimal RoundPlayerScore(ScoreState state, string roundPrefix, int player, string mode)
        {
            // Keep legacy score input independent of the new kill/damage inputs.
            // Damage is converted once per round, so remainders never carry over.
            if (mode == "killdeal")
                return state.GetDecimal(roundPrefix + KillSuffixes[player], 0m) +
                    Decimal.Floor(state.GetDecimal(roundPrefix + DamageSuffixes[player], 0m) / 100m);
            return state.GetDecimal(roundPrefix + ScoreSuffixes[player], 0m);
        }
        public static TeamSummary Calculate(ScoreState state, int team, string mode = "")
        {
            if (state == null || team < 0 || team >= ScoreRules.TeamCount) throw new ArgumentOutOfRangeException("team");
            string prefix = "t" + team.ToString(CultureInfo.InvariantCulture);
            int rounds = state.RoundCount;
            TeamSummary result = new TeamSummary {
                Name = state.Get(prefix + ".name"), Names = new string[4], Raw = new decimal[4],
                Deductions = new decimal[4], PlayerTotals = new decimal[4], RoundTotals = new decimal[rounds]
            };
            decimal[] penalties = new decimal[4];
            for (int player = 0; player < 4; player++) {
                result.Names[player] = state.Get(prefix + NameSuffixes[player]);
                penalties[player] = state.GetDecimal(prefix + PenaltySuffixes[player], 0m);
            }
            for (int round = 1; round <= rounds; round++)
            {
                string roundPrefix = prefix + ".r" + round.ToString(CultureInfo.InvariantCulture);
                decimal chicken = state.GetDecimal(roundPrefix + ".chicken", 0m);
                result.ChickenSum += chicken;
                result.RoundTotals[round - 1] = chicken;
                for (int player = 0; player < 4; player++)
                {
                    decimal raw = RoundPlayerScore(state, roundPrefix, player, mode);
                    decimal deduction = state.Get(roundPrefix + CheckSuffixes[player], "0") == "1"
                        ? penalties[player] : 0m;
                    result.Raw[player] += raw;
                    result.Deductions[player] += deduction;
                    result.PlayerTotals[player] += raw - deduction;
                    result.RoundTotals[round - 1] += raw - deduction;
                }
                result.Total += result.RoundTotals[round - 1];
            }
            return result;
        }
    }

    public sealed class ChangeRequest
    {
        public string Key { get; set; }
        public string Value { get; set; }
        public int Version { get; set; }
        public string RequestId { get; set; }
    }

    public sealed class ConflictException : Exception
    {
        public string LatestValue;
        public int LatestVersion;
        public ConflictException(string latestValue, int latestVersion)
            : base("다른 참여자가 이 항목을 먼저 수정했습니다. 최신 값을 확인한 뒤 다시 입력해 주세요.")
        { LatestValue = latestValue; LatestVersion = latestVersion; }
    }

    public sealed class AppliedRequest
    {
        public string Id { get; set; }
        public string Fingerprint { get; set; }
    }
    public sealed class StoreDocument
    {
        public int Format { get; set; }
        public Dictionary<string, CellValue> Cells { get; set; }
        public List<AppliedRequest> Requests { get; set; }
    }

    public sealed class LocalStore
    {
        private readonly object gate = new object();
        private readonly string path;
        private ScoreState state;
        private List<AppliedRequest> recent = new List<AppliedRequest>();
        private const int MaximumJournal = 256;

        public LocalStore(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("저장 경로가 필요합니다.");
            this.path = Path.GetFullPath(path);
            state = new ScoreState();
            if (!File.Exists(this.path)) return;
            try
            {
                FileInfo info = new FileInfo(this.path);
                if (info.Length > 4 * 1024 * 1024) throw new InvalidDataException("저장 파일이 너무 큽니다.");
                StoreDocument document = Json().Deserialize<StoreDocument>(File.ReadAllText(this.path, Encoding.UTF8));
                if (document == null || document.Format != 1 || document.Cells == null || document.Requests == null || document.Requests.Count > MaximumJournal)
                    throw new InvalidDataException("지원하지 않는 저장 파일입니다.");
                foreach (KeyValuePair<string, CellValue> item in document.Cells)
                {
                    if (item.Value == null || item.Value.Version < 1) throw new InvalidDataException("항목 버전이 올바르지 않습니다.");
                    if (item.Value.Value == null) throw new InvalidDataException("항목 값이 올바르지 않습니다.");
                    ScoreRules.NormalizeStored(item.Key, item.Value.Value);
                }
                HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (AppliedRequest request in document.Requests)
                    if (request == null || String.IsNullOrEmpty(request.Id) || request.Id.Length > 128 || request.Fingerprint == null || request.Fingerprint.Length != 44 || !ids.Add(request.Id))
                        throw new InvalidDataException("저장 요청 기록이 올바르지 않습니다.");
                state.Cells = document.Cells;
                recent = document.Requests;
            }
            catch (Exception error)
            {
                if (!(error is ArgumentException) && !(error is InvalidOperationException) && !(error is IOException) && !(error is FormatException)) throw;
                throw new InvalidDataException("저장 파일을 읽을 수 없습니다. 기존 파일을 보존했습니다. .bak 백업 파일을 확인해 주세요. " + this.path, error);
            }
        }

        public ScoreState Snapshot() { lock (gate) return state.Copy(); }

        public ScoreState Apply(ChangeRequest request)
        {
            if (request != null) request = new ChangeRequest { Key = request.Key, Value = request.Value, Version = request.Version, RequestId = request.RequestId };
            ScoreRules.ValidateRequest(request);
            string normalized = ScoreRules.Normalize(request.Key, request.Value);
            string fingerprint = Fingerprint(request.Key, normalized, request.Version);
            lock (gate)
            {
                foreach (AppliedRequest done in recent)
                {
                    if (done.Id != request.RequestId) continue;
                    if (done.Fingerprint != fingerprint) throw new InvalidOperationException("같은 요청 ID를 다른 수정에 사용할 수 없습니다.");
                    return state.Copy();
                }
                int version = state.Version(request.Key);
                if (version != request.Version) throw new ConflictException(state.Get(request.Key), version);
                if (version == Int32.MaxValue) throw new InvalidOperationException("이 항목의 수정 횟수가 한도에 도달했습니다.");
                ScoreState next = state.Copy();
                next.Cells[request.Key] = new CellValue { Value = normalized, Version = version + 1 };
                List<AppliedRequest> nextRecent = new List<AppliedRequest>(recent);
                nextRecent.Add(new AppliedRequest { Id = request.RequestId, Fingerprint = fingerprint });
                if (nextRecent.Count > MaximumJournal) nextRecent.RemoveAt(0);
                Save(next, nextRecent);
                state = next;
                recent = nextRecent;
                return state.Copy();
            }
        }

        private void Save(ScoreState next, List<AppliedRequest> requests)
        {
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(Json().Serialize(new StoreDocument { Format = 1, Cells = next.Cells, Requests = requests }));
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(data, 0, data.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        private static string Fingerprint(string key, string value, int version)
        {
            using (SHA256 hash = SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(key + "\n" + value + "\n" + version.ToString(CultureInfo.InvariantCulture))));
        }
        internal static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024, RecursionLimit = 32 }; }
    }
}
