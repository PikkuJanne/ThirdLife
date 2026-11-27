using System.Collections.ObjectModel;
using System.Text;
using ThirdLife.Core.Finalization;

namespace ThirdLife.Inventory.Windows;

/// <summary>Reads a fixed set of workshop artifact indicators without reading file content.</summary>
public sealed class WorkshopArtifactDetector
{
    public const int MaximumConfiguredTargets = 16;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly WorkshopArtifactKind[] Kinds = Enum.GetValues<WorkshopArtifactKind>();
    private readonly Func<WorkshopArtifactKind, string?, CancellationToken, WorkshopArtifactStatus> _read;
    private int _active;

    public WorkshopArtifactDetector() : this(WindowsWorkshopArtifactSource.Read) { }

    internal WorkshopArtifactDetector(Func<WorkshopArtifactKind, string?, CancellationToken, WorkshopArtifactStatus> read) =>
        _read = read ?? throw new ArgumentNullException(nameof(read));

    public Task<IReadOnlyList<WorkshopArtifactFinding>> DetectAsync(CancellationToken cancellationToken = default) =>
        DetectAsync(null, null, cancellationToken);

    public async Task<IReadOnlyList<WorkshopArtifactFinding>> DetectAsync(IEnumerable<string>? localAccountNames,
        IEnumerable<string>? smbShareNames, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var accountNames = CopyLocalNames(localAccountNames, 20, nameof(localAccountNames));
        var shareNames = CopyLocalNames(smbShareNames, 80, nameof(smbShareNames));
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
        {
            return UniformResult(WorkshopArtifactStatus.Unavailable);
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(Timeout);
        var token = budget.Token;
        var scan = Task.Run(() =>
        {
            try
            {
                return Kinds.Select(kind => new WorkshopArtifactFinding(kind, Read(kind, accountNames, shareNames, token))).ToArray();
            }
            finally
            {
                // A timed-out native call can return later. Do not add workers while it still owns the scan.
                Volatile.Write(ref _active, 0);
            }
        });

        try
        {
            return Array.AsReadOnly(await scan.WaitAsync(token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return UniformResult(WorkshopArtifactStatus.TimedOut);
        }
    }

    private static ReadOnlyCollection<WorkshopArtifactFinding> UniformResult(WorkshopArtifactStatus status) =>
        Array.AsReadOnly(Kinds.Select(kind => new WorkshopArtifactFinding(kind, status)).ToArray());

    private WorkshopArtifactStatus Read(WorkshopArtifactKind kind, string[] accountNames, string[] shareNames, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return kind switch
        {
            WorkshopArtifactKind.ConfiguredLocalAccounts => CheckConfigured(kind, accountNames, token),
            WorkshopArtifactKind.ConfiguredSmbShares => CheckConfigured(kind, shareNames, token),
            _ => _read(kind, null, token),
        };
    }

    private WorkshopArtifactStatus CheckConfigured(WorkshopArtifactKind kind, string[] names, CancellationToken token)
    {
        if (names.Length == 0)
        {
            return WorkshopArtifactStatus.TargetsNotConfigured;
        }

        var result = WorkshopArtifactStatus.AbsentInCheckedScope;
        foreach (var name in names)
        {
            token.ThrowIfCancellationRequested();
            var status = _read(kind, name, token);
            if (status == WorkshopArtifactStatus.Present)
            {
                return status;
            }

            if (result == WorkshopArtifactStatus.AbsentInCheckedScope)
            {
                result = status;
            }
        }

        return result;
    }

    private static string[] CopyLocalNames(IEnumerable<string>? names, int maximumLength, string parameter)
    {
        var copy = names?.Take(MaximumConfiguredTargets + 1).ToArray() ?? [];
        if (copy.Length > MaximumConfiguredTargets || copy.Distinct(StringComparer.OrdinalIgnoreCase).Count() != copy.Length ||
            copy.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > maximumLength ||
                name.EndsWith('.') || name != name.Trim() ||
                name.EnumerateRunes().Any(rune => rune == Rune.ReplacementChar) ||
                name.Any(character => char.IsControl(character) || "\"/\\[]:;|=,+*?<>@".Contains(character))))
        {
            throw new ArgumentException("Use a bounded list of unique exact local names.", parameter);
        }

        return copy;
    }
}
