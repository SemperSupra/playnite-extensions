using System;

namespace MediaLibraryEnrichment
{
    public enum ProjectionMode
    {
        Observe,
        Apply,
        Rollback
    }

    public enum CategoryProjectionOperation
    {
        Noop,
        Add,
        Remove
    }

    public static class CategoryProjectionPlanner
    {
        public static CategoryProjectionOperation Plan(
            ProjectionMode mode,
            bool categoryPresent,
            bool pluginOwnsMembership,
            bool categoryIdentityStillMatches)
        {
            switch (mode)
            {
                case ProjectionMode.Observe:
                    return CategoryProjectionOperation.Noop;

                case ProjectionMode.Apply:
                    return categoryPresent
                        ? CategoryProjectionOperation.Noop
                        : CategoryProjectionOperation.Add;

                case ProjectionMode.Rollback:
                    return categoryPresent &&
                        pluginOwnsMembership &&
                        categoryIdentityStillMatches
                        ? CategoryProjectionOperation.Remove
                        : CategoryProjectionOperation.Noop;

                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }
    }
}
