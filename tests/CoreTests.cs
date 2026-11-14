using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using PubgScore;

internal static class CoreTests
{
    private static int checks;
    private static string root;
    public static int Main()
    {
        root = Path.Combine(Path.GetTempPath(), "pubg-core-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            PersistenceAndConflicts();
            CalculationAndValidation();
            FailedPersistence();


            Console.WriteLine("PASS: " + checks + " checks (storage, decimal scoring, CAS).");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }
    private static ChangeRequest Change(string key, string value, int version)
    { return new ChangeRequest { Key = key, Value = value, Version = version, RequestId = Guid.NewGuid().ToString("N") }; }
    private static void Assert(bool condition, string message)
    { checks++; if (!condition) throw new Exception("ASSERTION FAILED: " + message); }
    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); } catch (T) { checks++; return; }
        throw new Exception("Expected " + typeof(T).Name + ": " + message);
    }
    private static void PersistenceAndConflicts()
    {
        string path = Path.Combine(root, "persistent.json");
        LocalStore store = new LocalStore(path);
        Assert(store.Snapshot().RoundCount == 50 && store.Snapshot().Get("t0.p0.name") == "", "blank defaults");
        ChangeRequest first = Change("t0.r1.p0.score", "2.5", 0);
        store.Apply(first);
        store.Apply(first);
        Assert(store.Snapshot().Version(first.Key) == 1, "same request is idempotent");
        ChangeRequest collision = Change("t0.r1.p1.score", "7", 0); collision.RequestId = first.RequestId;
        Throws<InvalidOperationException>(delegate { store.Apply(collision); }, "request ID reuse");
        Throws<ConflictException>(delegate { store.Apply(Change(first.Key, "99", 0)); }, "stale cell edit");
        Task a = Task.Factory.StartNew(delegate { store.Apply(Change("t0.r1.p1.score", "3", 0)); });
        Task b = Task.Factory.StartNew(delegate { store.Apply(Change("t0.r1.p2.score", "4", 0)); });
        Task.WaitAll(a, b);
        Assert(store.Snapshot().GetDecimal("t0.r1.p1.score", 0m) == 3m && store.Snapshot().GetDecimal("t0.r1.p2.score", 0m) == 4m, "independent concurrent cells survive");
        ScoreState copy = store.Snapshot(); copy.Cells[first.Key].Value = "999";
        Assert(store.Snapshot().Get(first.Key) == "2.5", "snapshot is defensive copy");
        LocalStore reopened = new LocalStore(path); reopened.Apply(first);
        Assert(reopened.Snapshot().Version(first.Key) == 1, "request journal survives reload");
        Assert(File.Exists(path + ".bak"), "atomic replacement backup");
        string corruptPath = Path.Combine(root, "corrupt.json"); File.WriteAllText(corruptPath, "{oops");
        Throws<InvalidDataException>(delegate { new LocalStore(corruptPath); }, "malformed storage rejected");
        Assert(File.ReadAllText(corruptPath) == "{oops", "corrupted storage preserved");
    }
    private static void CalculationAndValidation()
    {
        LocalStore store = new LocalStore(Path.Combine(root, "scores.json"));
        string[,] values = { { "t0.r1.p0.score", "2.5" }, { "t0.r2.p0.score", "-1.25" },
            { "t0.r1.p1.score", "3" }, { "t0.p0.penalty", "1.5" }, { "t0.r1.p0.check", "1" },
            { "t0.r2.p0.check", "1" }, { "t0.r1.chicken", "-4" }, { "t0.r2.chicken", "8" }, { "t0.reference", "23" } };
        for (int index = 0; index < values.GetLength(0); index++) store.Apply(Change(values[index, 0], values[index, 1], 0));
        TeamSummary score = ScoreMath.Calculate(store.Snapshot(), 0);
        Assert(score.Raw[0] == 1.25m && score.Deductions[0] == 3m && score.PlayerTotals[0] == -1.75m, "decimal raw and per-check deductions");
        Assert(score.ChickenSum == 4m && score.Total == 5.25m, "team total excludes reference");
        Assert(score.RoundTotals[0] == 0m && score.RoundTotals[1] == 5.25m, "round total order");
        Assert(ScoreMath.Calculate(store.Snapshot(), 7).Total == 0m, "independent teams");
        store.Apply(Change("t7.r179.p3.score", "-0.25", 0)); store.Apply(Change("roundCount", "179", 0));
        Assert(ScoreMath.Calculate(store.Snapshot(), 7).Total == -0.25m, "last supported round");
        Throws<ArgumentException>(delegate { store.Apply(Change("t10.name", "bad", 0)); }, "team bounds");
        Throws<ArgumentException>(delegate { store.Apply(Change("t0.r180.p0.score", "1", 0)); }, "round bounds");
        Throws<ArgumentException>(delegate { store.Apply(Change("t0.p0.penalty", "-1", 1)); }, "penalty is nonnegative");
        Throws<ArgumentException>(delegate { store.Apply(Change("t0.r3.chicken", "7", 0)); }, "original discrete chicken options");
        Throws<ArgumentException>(delegate { store.Apply(Change("t0.r3.p0.score", "1000000", 0)); }, "numeric limit");
        Throws<ArgumentException>(delegate { store.Apply(Change("unknown", "1", 0)); }, "unknown field");
        Throws<ArgumentException>(delegate { store.Apply(Change("t0.status", "anything", 0)); }, "status whitelist");
        Assert(ScoreRules.Normalize("t0.r3.chicken", "800") == "800", "large original chicken option");
    }
    private static void FailedPersistence()
    {
        string obstruction = Path.Combine(root, "not-a-directory"); File.WriteAllText(obstruction, "blocked");
        LocalStore store = new LocalStore(Path.Combine(obstruction, "data.json"));
        Throws<IOException>(delegate { store.Apply(Change("t0.name", "modified", 0)); }, "failed save");
        Assert(store.Snapshot().Version("t0.name") == 0, "memory rolls back on save failure");
    }
}
