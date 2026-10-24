using Xunit;

namespace Citymaker.Tests
{
    /// <summary>
    /// Test classes with wall-clock budgets. They run after the parallel tests, one class at a time,
    /// so the budgets measure the code rather than how loaded the test machine is.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class TimingCollection
    {
        public const string Name = "Timing";
    }
}
