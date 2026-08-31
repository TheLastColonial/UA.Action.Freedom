using Xunit.Sdk;
using Xunit.v3;

// These tests share one database, so they are not independent and must not run in parallel.
//
// They were parallel-safe while each slice owned an isolated table and generated its own keys.
// Adding the foreign keys between Vehicle, Convoy and Manifest changed that: a insert into
// dbo.Manifest now takes a lock on dbo.Vehicle, a convoy delete touches dbo.Vehicle through
// ON DELETE SET NULL, and a paged SELECT over dbo.Vehicle walks an index another test is
// modifying. Different classes then acquire the same locks in different orders, and SQL Server
// picks one as the deadlock victim — which showed up as roughly one failure in four full runs
// of the solution, in whichever test happened to lose.
//
// Serialising the assembly is the honest fix rather than retrying the victim: the suite is
// deliberately stateful, it runs against a real database, and 37 tests take seconds either way.
// It is also closer to the truth about production, where convoys run about once a month and
// nothing does any of this concurrently.
[assembly: Parallelization(Mode = ParallelMode.None)]
