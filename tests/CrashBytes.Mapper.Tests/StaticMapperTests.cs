using System.Diagnostics;

namespace CrashBytes.Mapper.Tests;

public class StaticMapperFixture : IDisposable
{
    public StaticMapperFixture() => Mapper.Reset();
    public void Dispose() => Mapper.Reset();
}

public class StaticMapperTests : IClassFixture<StaticMapperFixture>
{
    public StaticMapperTests() => Mapper.Reset();

    [Fact]
    public void StaticMap_AutoConfiguresFromConvention()
    {
        var src = new Source { Id = 7, Name = "Eve", Email = "eve@test.com" };
        var dest = Mapper.Map<Source, Dest>(src);

        Assert.Equal(7, dest.Id);
        Assert.Equal("Eve", dest.Name);
        Assert.Equal("eve@test.com", dest.Email);
    }

    [Fact]
    public void StaticMap_OntoExistingDestination_OverwritesMatchingProperties()
    {
        var src = new Source { Id = 99, Name = "Frank", Email = "f@test.com" };
        var existing = new Dest { Id = 1, Name = "old", Email = "old@old.com" };

        var result = Mapper.Map(src, existing);

        Assert.Same(existing, result);
        Assert.Equal(99, existing.Id);
        Assert.Equal("Frank", existing.Name);
        Assert.Equal("f@test.com", existing.Email);
    }

    [Fact]
    public void StaticMap_NullSource_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Mapper.Map<Source, Dest>((Source)null!));
    }

    [Fact]
    public void StaticMap_NullDestination_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Mapper.Map(new Source(), (Dest)null!));
    }

    [Fact]
    public void StaticConfigureMap_AppliesCustomResolver()
    {
        Mapper.ConfigureMap<Source, DestWithExtra>(m =>
            m.ForMember(d => d.FullName, s => $"Dr. {s.Name}"));

        var dest = Mapper.Map<Source, DestWithExtra>(new Source { Id = 2, Name = "Grace" });

        Assert.Equal(2, dest.Id);
        Assert.Equal("Grace", dest.Name);
        Assert.Equal("Dr. Grace", dest.FullName);
    }

    [Fact]
    public void StaticConfigureMap_NullConfigure_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Mapper.ConfigureMap<Source, Dest>(null!));
    }

    [Fact]
    public void StaticConfigureMap_OverwritesPreviouslyCachedAmbientMapping()
    {
        // First trigger the convention-based ambient mapping.
        var first = Mapper.Map<Source, DestWithExtra>(new Source { Id = 1, Name = "first" });
        Assert.Equal("", first.FullName);

        // Now override with a custom resolver — subsequent calls must use the new mapping.
        Mapper.ConfigureMap<Source, DestWithExtra>(m =>
            m.ForMember(d => d.FullName, s => $"Hi {s.Name}"));

        var second = Mapper.Map<Source, DestWithExtra>(new Source { Id = 2, Name = "second" });
        Assert.Equal("Hi second", second.FullName);
    }
}

// ──────────────────────────────────────────────
//  Type-mismatch / unmatched-property behavior
// ──────────────────────────────────────────────

public class IncompatibleTypesSource
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string ShouldBeSkipped { get; set; } = "string-not-int";
}

public class IncompatibleTypesDest
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int ShouldBeSkipped { get; set; }
}

public class TypeCompatibilityTests
{
    [Fact]
    public void Map_WithTypeMismatchedProperty_SkipsItSilently()
    {
        var config = new MapperConfiguration();
        config.CreateMap<IncompatibleTypesSource, IncompatibleTypesDest>();
        var mapper = config.BuildMapper();

        var dest = mapper.Map<IncompatibleTypesSource, IncompatibleTypesDest>(
            new IncompatibleTypesSource { Id = 5, Name = "ok", ShouldBeSkipped = "ignored" });

        Assert.Equal(5, dest.Id);
        Assert.Equal("ok", dest.Name);
        Assert.Equal(0, dest.ShouldBeSkipped); // left at default — string→int is not assignable
    }
}

// ──────────────────────────────────────────────
//  Nested reference handling
// ──────────────────────────────────────────────

public class Address
{
    public string City { get; set; } = "";
}

public class PersonSource
{
    public string Name { get; set; } = "";
    public Address Address { get; set; } = new();
}

public class PersonDest
{
    public string Name { get; set; } = "";
    public Address Address { get; set; } = new();
}

public class NestedReferenceTests
{
    [Fact]
    public void Map_NestedReference_CopiesByReference()
    {
        var config = new MapperConfiguration();
        config.CreateMap<PersonSource, PersonDest>();
        var mapper = config.BuildMapper();

        var src = new PersonSource { Name = "Hugo", Address = new Address { City = "Dayton" } };
        var dest = mapper.Map<PersonSource, PersonDest>(src);

        Assert.Equal("Hugo", dest.Name);
        Assert.NotNull(dest.Address);
        Assert.Same(src.Address, dest.Address); // reference copy is the documented behavior
        Assert.Equal("Dayton", dest.Address.City);
    }

    [Fact]
    public void Map_NullNestedReference_CopiesNull()
    {
        var config = new MapperConfiguration();
        config.CreateMap<PersonSource, PersonDest>();
        var mapper = config.BuildMapper();

        var src = new PersonSource { Name = "Iris", Address = null! };
        var dest = mapper.Map<PersonSource, PersonDest>(src);

        Assert.Equal("Iris", dest.Name);
        Assert.Null(dest.Address);
    }
}

// ──────────────────────────────────────────────
//  DI integration
// ──────────────────────────────────────────────

public class DependencyInjectionTests
{
    [Fact]
    public void AddCrashBytesMapper_RegistersIMapperAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddCrashBytesMapper(c => c.CreateMap<Source, Dest>());

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IMapper>();
        var second = provider.GetRequiredService<IMapper>();

        Assert.Same(first, second);
    }

    [Fact]
    public void AddCrashBytesMapper_ResolvedMapper_AppliesConfiguration()
    {
        var services = new ServiceCollection();
        services.AddCrashBytesMapper(c =>
            c.CreateMap<Source, DestWithExtra>()
             .ForMember(d => d.FullName, s => $"Capt. {s.Name}"));

        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();

        var dest = mapper.Map<Source, DestWithExtra>(new Source { Id = 4, Name = "Jane" });

        Assert.Equal(4, dest.Id);
        Assert.Equal("Jane", dest.Name);
        Assert.Equal("Capt. Jane", dest.FullName);
    }

    [Fact]
    public void AddCrashBytesMapper_NoConfigureCallback_ResolvesMapper()
    {
        var services = new ServiceCollection();
        services.AddCrashBytesMapper();

        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();

        Assert.NotNull(mapper);
    }

    [Fact]
    public void AddCrashBytesMapper_NullServices_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ServiceCollectionExtensions.AddCrashBytesMapper(null!));
    }
}

// ──────────────────────────────────────────────
//  Performance smoke test
// ──────────────────────────────────────────────

public class PerformanceSmokeTests
{
    [Fact]
    public void Map_TenThousandIterations_CompletesInGenerousBound()
    {
        var config = new MapperConfiguration();
        config.CreateMap<Source, Dest>();
        var mapper = config.BuildMapper();

        // Warm the compiled-expression cache so we exercise the hot path.
        mapper.Map<Source, Dest>(new Source { Id = 1, Name = "warmup", Email = "w@w.com" });

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 10_000; i++)
        {
            var dest = mapper.Map<Source, Dest>(new Source { Id = i, Name = "n", Email = "e" });
            Assert.Equal(i, dest.Id);
        }
        sw.Stop();

        // Generous bound — this is a smoke test, not a benchmark. On a modern dev
        // machine the compiled-expression path completes in well under 100 ms; we
        // pick 2_000 ms to absorb noisy CI runners and the JIT cost of the first
        // 10k allocations.
        Assert.True(sw.ElapsedMilliseconds < 2_000,
            $"10k mapped allocations took {sw.ElapsedMilliseconds} ms — expected < 2000 ms");
    }
}
