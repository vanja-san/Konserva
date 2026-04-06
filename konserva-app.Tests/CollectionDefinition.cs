using Xunit;

namespace Konserva.Tests;

// Коллекция для тестов, которые не могут выполняться параллельно
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class SequentialCollection { }
