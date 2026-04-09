using Xunit;
using Konserva.Tests.Fixtures;

namespace Konserva.Tests;

// Коллекция для тестов, которые не могут выполняться параллельно
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class SequentialCollection { }

// Коллекция для тестов, использующих TestConfigFixture
[CollectionDefinition("TestConfig", DisableParallelization = false)]
public class TestConfigCollection : ICollectionFixture<TestConfigFixture> { }
