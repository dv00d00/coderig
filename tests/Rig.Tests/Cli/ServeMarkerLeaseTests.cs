using System.Text.Json;
using Rig.Cli.CommandLine;
using Rig.Cli.Services;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class ServeMarkerLeaseTests
{
    private static readonly JsonSerializerOptions MarkerJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [Test]
    public void A_live_marker_is_preserved_byte_for_byte_and_the_second_publisher_does_not_own_it()
    {
        WithTempRoot(root =>
        {
            var path = MarkerPath(root);
            using var first = ServeMarkerLease.Publish(root, 5049, "http://localhost:5049");
            var firstContents = File.ReadAllText(path);

            using var second = ServeMarkerLease.Publish(root, 5050, "http://localhost:5050");

            first.OwnsMarker.ShouldBeTrue();
            second.OwnsMarker.ShouldBeFalse();
            second.BlockingMarker.ShouldNotBeNull().Pid.ShouldBe(Environment.ProcessId);
            File.ReadAllText(path).ShouldBe(firstContents);
            AssertMarkerGateAvailable(path);

            second.Dispose();
            File.ReadAllText(path).ShouldBe(firstContents);
            AssertMarkerGateAvailable(path);

            first.Dispose();
            AssertMarkerGateAvailable(path);
        });
    }

    [Test]
    public void A_dead_pid_marker_is_reclaimed_and_removed_only_by_the_new_lease()
    {
        WithTempRoot(root =>
        {
            var path = MarkerPath(root);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var stale = new ServeMarker(
                Port: 5049,
                Url: "http://localhost:5049",
                Pid: int.MaxValue,
                WorkingDirectory: AnnotateResidentTransport.CanonicalPath(root),
                StartedUtc: DateTimeOffset.UtcNow.AddMinutes(-5)
            );
            File.WriteAllText(path, JsonSerializer.Serialize(stale, MarkerJson));

            var lease = ServeMarkerLease.Publish(root, 5050, "http://localhost:5050");
            try
            {
                lease.OwnsMarker.ShouldBeTrue();
                var current = AnnotateResidentTransport.ReadMarker(path).ShouldNotBeNull();
                current.Port.ShouldBe(5050);
                current.Url.ShouldBe("http://localhost:5050");
                current.Pid.ShouldBe(Environment.ProcessId);
                File.Exists(path).ShouldBeTrue();
                AssertMarkerGateAvailable(path);
            }
            finally
            {
                lease.Dispose();
            }

            File.Exists(path).ShouldBeFalse();
            AssertMarkerGateAvailable(path);
        });
    }

    [Test]
    public void Disposal_never_deletes_a_replacement_marker()
    {
        WithTempRoot(root =>
        {
            var path = MarkerPath(root);
            var lease = ServeMarkerLease.Publish(root, 5050, "http://localhost:5050");
            var own = AnnotateResidentTransport.ReadMarker(path).ShouldNotBeNull();
            var replacement = own with { Port = 5051, Url = "http://localhost:5051", StartedUtc = own.StartedUtc.AddSeconds(1) };
            var replacementContents = JsonSerializer.Serialize(replacement, MarkerJson);
            File.WriteAllText(path, replacementContents);

            lease.Dispose();

            File.ReadAllText(path).ShouldBe(replacementContents);
            AnnotateResidentTransport.ReadMarker(path).ShouldBe(replacement);
            AssertMarkerGateAvailable(path);
        });
    }

    [Test]
    public void A_malformed_marker_fails_closed_without_being_replaced()
    {
        WithTempRoot(root =>
        {
            var path = MarkerPath(root);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            const string malformed = "{ not-json";
            File.WriteAllText(path, malformed);

            Should.Throw<JsonException>(() => ServeMarkerLease.Publish(root, 5050, "http://localhost:5050"));

            File.ReadAllText(path).ShouldBe(malformed);
            AssertMarkerGateAvailable(path);
        });
    }

    [Test]
    public async Task Concurrent_publishers_atomically_select_exactly_one_owner()
    {
        AnnotateResidentTransport.IsAlive(Environment.ProcessId).ShouldBeTrue();

        var root = Directory.CreateTempSubdirectory("rig-serve-marker-").FullName;
        try
        {
            var publishers = Enumerable
                .Range(0, 8)
                .Select(index => Task.Run(() => ServeMarkerLease.Publish(root, 5100 + index, $"http://localhost:{5100 + index}")))
                .ToArray();
            var leases = await Task.WhenAll(publishers);
            try
            {
                var owner = leases.Where(lease => lease.OwnsMarker).ShouldHaveSingleItem();
                var marker = AnnotateResidentTransport.ReadMarker(MarkerPath(root)).ShouldNotBeNull();
                marker.Url.ShouldBe($"http://localhost:{marker.Port}");
                marker.Port.ShouldBeInRange(5100, 5107);
                leases.Where(lease => !lease.OwnsMarker).ShouldAllBe(lease => lease.BlockingMarker == marker);
                AssertMarkerGateAvailable(MarkerPath(root));

                foreach (var lease in leases.Where(lease => !lease.OwnsMarker))
                {
                    lease.Dispose();
                }

                File.Exists(MarkerPath(root)).ShouldBeTrue();
                owner.Dispose();
                File.Exists(MarkerPath(root)).ShouldBeFalse();
                AssertMarkerGateAvailable(MarkerPath(root));
            }
            finally
            {
                foreach (var lease in leases)
                {
                    lease.Dispose();
                }
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string MarkerPath(string root) => Path.Combine(StoreLayout.RigDir(root), AnnotateResidentTransport.MarkerFileName);

    private static void AssertMarkerGateAvailable(string markerPath)
    {
        using var gate = new FileStream(
            AnnotateResidentTransport.MarkerGatePath(markerPath),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None
        );
    }

    private static void WithTempRoot(Action<string> assertion)
    {
        var root = Directory.CreateTempSubdirectory("rig-serve-marker-").FullName;
        try
        {
            assertion(root);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void TryDelete(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
