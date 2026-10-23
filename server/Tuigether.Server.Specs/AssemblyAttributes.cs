using Tuigether.Server.Specs;

[assembly: AssemblyFixture(typeof(ServerFixture))]

// All specs share one database, and each spec clears it first.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
