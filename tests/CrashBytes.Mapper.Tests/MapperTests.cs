namespace CrashBytes.Mapper.Tests;

// ──────────────────────────────────────────────
//  Test models
// ──────────────────────────────────────────────

public class Source
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}

public class Dest
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}

public class DestWithExtra
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
}

public class DestNoMatchingProps
{
    public string Foo { get; set; } = "";
    public int Bar { get; set; }
}

// ──────────────────────────────────────────────
//  Tests
// ──────────────────────────────────────────────

public class MapperConfigurationTests
{
    [Fact]
    public void CreateMap_RegistersMapping()
    {
        var config = new MapperConfiguration();
        config.CreateMap<Source, Dest>();
        var mapper = config.BuildMapper();
        Assert.NotNull(mapper);
    }
}

public class MapTests
{
    [Fact]
    public void Map_ConventionBased_CopiesMatchingProperties()
    {
        var config = new MapperConfiguration();
        config.CreateMap<Source, Dest>();
        var mapper = config.BuildMapper();

        var source = new Source { Id = 1, Name = "Alice", Email = "alice@test.com" };
        var dest = mapper.Map<Source, Dest>(source);

        Assert.Equal(1, dest.Id);
        Assert.Equal("Alice", dest.Name);
        Assert.Equal("alice@test.com", dest.Email);
    }

    [Fact]
    public void Map_UnmatchedProperties_AreLeftDefault()
    {
        var config = new MapperConfiguration();
        config.CreateMap<Source, DestNoMatchingProps>();
        var mapper = config.BuildMapper();

        var source = new Source { Id = 1, Name = "Alice" };
        var dest = mapper.Map<Source, DestNoMatchingProps>(source);

        Assert.Equal("", dest.Foo);
        Assert.Equal(0, dest.Bar);
    }

    [Fact]
    public void Map_WithCustomResolver_UsesResolver()
    {
        var config = new MapperConfiguration();
        config.CreateMap<Source, DestWithExtra>()
            .ForMember(d => d.FullName, s => $"Mr. {s.Name}");
        var mapper = config.BuildMapper();

        var source = new Source { Id = 1, Name = "Bob" };
        var dest = mapper.Map<Source, DestWithExtra>(source);

        Assert.Equal(1, dest.Id);
        Assert.Equal("Bob", dest.Name);
        Assert.Equal("Mr. Bob", dest.FullName);
    }

    [Fact]
    public void Map_OntoExistingObject_UpdatesProperties()
    {
        var config = new MapperConfiguration();
        config.CreateMap<Source, Dest>();
        var mapper = config.BuildMapper();

        var source = new Source { Id = 5, Name = "Charlie", Email = "c@test.com" };
        var dest = new Dest { Id = 0, Name = "old" };

        mapper.Map(source, dest);

        Assert.Equal(5, dest.Id);
        Assert.Equal("Charlie", dest.Name);
        Assert.Equal("c@test.com", dest.Email);
    }

    [Fact]
    public void Map_ObjectOverload_MapsCorrectly()
    {
        var config = new MapperConfiguration();
        config.CreateMap<Source, Dest>();
        var mapper = config.BuildMapper();

        object source = new Source { Id = 10, Name = "Dave" };
        var dest = mapper.Map<Dest>(source);

        Assert.Equal(10, dest.Id);
        Assert.Equal("Dave", dest.Name);
    }

    [Fact]
    public void Map_UnregisteredMapping_ThrowsInvalidOperationException()
    {
        var config = new MapperConfiguration();
        var mapper = config.BuildMapper();

        Assert.Throws<InvalidOperationException>(() =>
            mapper.Map<Source, Dest>(new Source()));
    }

    [Fact]
    public void Map_NullSource_ThrowsArgumentNullException()
    {
        var config = new MapperConfiguration();
        config.CreateMap<Source, Dest>();
        var mapper = config.BuildMapper();

        Assert.Throws<ArgumentNullException>(() => mapper.Map<Source, Dest>(null!));
    }

    [Fact]
    public void Map_ObjectOverload_NullSource_ThrowsArgumentNullException()
    {
        var config = new MapperConfiguration();
        config.CreateMap<Source, Dest>();
        var mapper = config.BuildMapper();

        Assert.Throws<ArgumentNullException>(() => mapper.Map<Dest>(null!));
    }

    [Fact]
    public void Map_OntoExisting_NullSource_ThrowsArgumentNullException()
    {
        var config = new MapperConfiguration();
        config.CreateMap<Source, Dest>();
        var mapper = config.BuildMapper();

        Assert.Throws<ArgumentNullException>(() => mapper.Map<Source, Dest>(null!, new Dest()));
    }

    [Fact]
    public void Map_OntoExisting_NullDest_ThrowsArgumentNullException()
    {
        var config = new MapperConfiguration();
        config.CreateMap<Source, Dest>();
        var mapper = config.BuildMapper();

        Assert.Throws<ArgumentNullException>(() => mapper.Map(new Source(), (Dest)null!));
    }
}

public class ForMemberTests
{
    [Fact]
    public void ForMember_NullDestMember_ThrowsArgumentNullException()
    {
        var config = new MapperConfiguration();
        var expr = config.CreateMap<Source, Dest>();
        Assert.Throws<ArgumentNullException>(() =>
            expr.ForMember(null!, s => s.Name));
    }

    [Fact]
    public void ForMember_NullResolver_ThrowsArgumentNullException()
    {
        var config = new MapperConfiguration();
        var expr = config.CreateMap<Source, Dest>();
        Assert.Throws<ArgumentNullException>(() =>
            expr.ForMember(d => d.Name, (Func<Source, string>)null!));
    }

    [Fact]
    public void ForMember_CustomResolver_OverridesConventionMapping()
    {
        var config = new MapperConfiguration();
        config.CreateMap<Source, Dest>()
            .ForMember(d => d.Name, s => s.Name.ToUpper());
        var mapper = config.BuildMapper();

        var dest = mapper.Map<Source, Dest>(new Source { Name = "alice" });
        Assert.Equal("ALICE", dest.Name);
    }
}
