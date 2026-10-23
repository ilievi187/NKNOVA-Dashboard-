using Xunit;

// Rule execution tests share the process-global ServiceSingleton service registry.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
