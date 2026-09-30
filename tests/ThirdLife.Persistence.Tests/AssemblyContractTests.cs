using ThirdLife.Core.Jobs;

namespace ThirdLife.Persistence.Tests;

public sealed class AssemblyContractTests
{
    [Fact]
    public void AssemblyIdentityMatchesProjectBoundary()
    {
        var marker = typeof(global::ThirdLife.Persistence.AssemblyMarker);

        Assert.Equal("ThirdLife.Persistence", marker.Assembly.GetName().Name);
        Assert.Equal("ThirdLife.Persistence", marker.Namespace);
    }

    [Fact]
    public void RepositoryPortIsCoreOwnedAndImplementedByTheSqliteAdapter()
    {
        Assert.Equal("ThirdLife.Core", typeof(IJobStore).Assembly.GetName().Name);
        Assert.True(typeof(IJobStore).IsAssignableFrom(typeof(SqliteJobStore)));
    }
}
