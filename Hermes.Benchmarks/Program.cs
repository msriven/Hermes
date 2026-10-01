using BenchmarkDotNet.Running;

// Run in Release:  dotnet run -c Release --project Hermes.Benchmarks -- --filter "*"
// Short run:       dotnet run -c Release --project Hermes.Benchmarks -- --filter "*" --job short
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
