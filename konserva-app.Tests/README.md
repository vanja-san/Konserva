# Konserva — Tests

The test project targets `net10.0-windows` and uses xUnit v3, Moq and
FluentAssertions. Tests never start the application or a Minecraft server.

## Running

```bash
# All tests
dotnet test

# Unit tests only — pure logic, no I/O
dotnet test --filter "Category=Unit"

# Integration tests only — filesystem, registry, HTTP
dotnet test --filter "Category=Integration"

# One test class
dotnet test --filter "FullyQualifiedName~AppUpdaterTests"

# With coverage
dotnet test --collect:"XPlat Code Coverage"
```

Categories are declared as an xUnit `Category` trait on each test class. There is
no `E2E` category: no end-to-end tests exist. Add the trait when the first one
appears.

## Structure

```
konserva-app.Tests/
├── CollectionDefinition.cs        # Shared xUnit collections
├── Converters/                    # Value converter tests
├── Fixtures/                      # TestConfigFixture and shared setup
├── Localization/                  # String table and parity tests
├── Models/                        # Model and API URL tests
├── Regression/                    # Regression tests for fixed defects
├── Services/                      # Service tests (installer, process, updates, ...)
├── Utilities/                     # Parser, hash and helper tests
└── ViewModels/                    # ViewModel tests
```

The suite currently contains 513 tests. `Directory.Build.props` enables
`TreatWarningsAsErrors`, so the test project must build without warnings.

## Writing tests

```csharp
[Fact]
public void MyTest_ReturnsTrue()
{
    var service = new MyService();

    var result = service.DoSomething();

    result.Should().BeTrue();
}
```

Data-driven tests use `[Theory]` with `[InlineData]`. WPF code that needs an STA
thread uses `[StaFact]`. Tests that touch the real filesystem belong in the
`Integration` category; keep `Unit` tests free of I/O.

## Coverage

```bash
dotnet test --collect:"XPlat Code Coverage"
```

The report is written to `konserva-app.Tests/TestResults/**/coverage.cobertura.xml`.
For an HTML report:

```bash
reportgenerator -reports:"konserva-app.Tests/TestResults/**/coverage.cobertura.xml" `
                -targetdir:"TestResults/CoverageReport" `
                -reporttypes:Html
```

## CI

`.github/workflows/ci.yml` runs on every push and pull request to `master`:
restore, build, `dotnet test` with OpenCover coverage, then uploads the
coverage report as an artifact.
