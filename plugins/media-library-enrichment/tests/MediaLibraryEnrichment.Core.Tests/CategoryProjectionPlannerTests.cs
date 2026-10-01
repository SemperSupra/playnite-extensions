using Xunit;

namespace MediaLibraryEnrichment.Core.Tests
{
    public sealed class CategoryProjectionPlannerTests
    {
        [Fact]
        public void ObserveNeverMutates()
        {
            Assert.Equal(
                CategoryProjectionOperation.Noop,
                CategoryProjectionPlanner.Plan(
                    ProjectionMode.Observe,
                    false,
                    false,
                    true));
        }

        [Fact]
        public void ApplyAddsOnlyWhenMembershipMissing()
        {
            Assert.Equal(
                CategoryProjectionOperation.Add,
                CategoryProjectionPlanner.Plan(
                    ProjectionMode.Apply,
                    false,
                    false,
                    true));

            Assert.Equal(
                CategoryProjectionOperation.Noop,
                CategoryProjectionPlanner.Plan(
                    ProjectionMode.Apply,
                    true,
                    true,
                    true));
        }

        [Fact]
        public void RollbackRequiresOwnedMembershipAndStableCategoryIdentity()
        {
            Assert.Equal(
                CategoryProjectionOperation.Remove,
                CategoryProjectionPlanner.Plan(
                    ProjectionMode.Rollback,
                    true,
                    true,
                    true));

            Assert.Equal(
                CategoryProjectionOperation.Noop,
                CategoryProjectionPlanner.Plan(
                    ProjectionMode.Rollback,
                    true,
                    false,
                    true));

            Assert.Equal(
                CategoryProjectionOperation.Noop,
                CategoryProjectionPlanner.Plan(
                    ProjectionMode.Rollback,
                    true,
                    true,
                    false));
        }
    }
}
