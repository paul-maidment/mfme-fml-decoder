using System;
using System.Linq;
using System.Text.Json.Serialization.Metadata;
using Xunit;

namespace MfmeFmlDecoder.src.Model.Component
{
    public class ComponentJsonContextTests
    {
        [Fact]
        public void ContextCoversEveryConcreteComponentType()
        {
            Type[] componentTypes = typeof(BaseComponent).Assembly.GetTypes()
                .Where(type => !type.IsAbstract && typeof(BaseComponent).IsAssignableFrom(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();

            Assert.NotEmpty(componentTypes);

            foreach (Type type in componentTypes)
            {
                JsonTypeInfo typeInfo = ComponentJsonContext.Default.GetTypeInfo(type);
                Assert.NotNull(typeInfo);
            }
        }
    }
}
