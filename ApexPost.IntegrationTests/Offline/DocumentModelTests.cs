using System.Globalization;
using System.Reflection;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Storage;
using ApexPost.Testing.Offline;

namespace ApexPost.IntegrationTests.Offline;

public class DocumentModelTests
{
    [Fact]
    public void ASavedScan_IsAJsonDocumentWithExactlyTheSevenFields()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var scan = db.Repository.Create(Scans.Make("APX-9", ScanType.InTransit, "C-5", notes: "left with neighbour"));

        // Act
        using var document = db.Database.DefaultCollection.GetDocument(scan.Id);

        // Assert
        Assert.NotNull(document);
        Assert.Equal(
            ["courierId", "location", "notes", "scanType", "scannedAtUtc", "syncStatus", "trackingNumber"],
            document.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("APX-9", document.GetString("trackingNumber"));
        Assert.Equal("InTransit", document.GetString("scanType"));
        Assert.Equal("C-5", document.GetString("courierId"));
        Assert.Equal("Sydney NSW", document.GetString("location"));
        Assert.Equal("left with neighbour", document.GetString("notes"));
        Assert.Equal("Pending", document.GetString("syncStatus"));
    }

    [Fact]
    public void ADeliveredScanWithASignature_AddsOnlyTheOptionalSignatureField()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var created = db.Repository.Create(ShipmentScan.CreatePending("APX-9", ScanType.Delivered, "C-5", "Front door", Scans.Noon, recipientSignatureName: "Mia Fictional"));

        // Act
        using var document = db.Database.DefaultCollection.GetDocument(created.Id);

        // Assert
        Assert.Equal(8, document!.Keys.Count);
        Assert.Equal("Mia Fictional", document.GetString("recipientSignatureName"));
        Assert.Equal("Mia Fictional", db.Repository.Get(created.Id)!.RecipientSignatureName);
        Assert.Equal("Mia Fictional", Assert.Single(db.Repository.FindPending()).RecipientSignatureName);
    }

    [Fact]
    public void TheTimeIsStoredAsUtcText_SoOrderingByTextIsOrderingByTime()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var scan = db.Repository.Create(Scans.Make(at: new DateTimeOffset(2026, 11, 2, 23, 15, 7, 123, TimeSpan.FromHours(10))));

        // Act
        using var document = db.Database.DefaultCollection.GetDocument(scan.Id);

        // Assert
        var stored = Assert.IsType<string>(document!.GetValue("scannedAtUtc"));
        Assert.True(DateTimeOffset.TryParse(stored, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed));
        Assert.Equal(TimeSpan.Zero, parsed.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 13, 15, 7, 123, TimeSpan.Zero), parsed);
        Assert.StartsWith("2026-11-02T13:15:07", stored, StringComparison.Ordinal);
    }

    [Fact]
    public void ADocumentId_IsScanColonColonAGuid()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var scan = db.Repository.Create(Scans.Make());

        // Act
        using var document = db.Database.DefaultCollection.GetDocument(scan.Id);

        // Assert
        Assert.Equal(scan.Id, document!.Id);
        Assert.Matches("^scan::[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", document.Id);
    }

    [Fact]
    public void MissingNotes_AreStoredAsNull_AndComeBackAsNull()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var scan = db.Repository.Create(Scans.Make(notes: null));

        // Act
        using var document = db.Database.DefaultCollection.GetDocument(scan.Id);

        // Assert
        Assert.Null(document!.GetString("notes"));
        Assert.Null(db.Repository.Get(scan.Id)!.Notes);
    }

    [Fact]
    public void ADocumentThatIsNotAShipmentScan_IsReportedNotGuessedAt()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var broken = new Couchbase.Lite.MutableDocument("scan::3f2504e0-4f89-11d3-9a0c-0305e82c3301");
        broken.SetString("trackingNumber", "APX-1");
        broken.SetString("scanType", "Teleported");
        db.Database.DefaultCollection.Save(broken);

        // Act and assert
        Assert.Throws<InvalidDataException>(() => db.Repository.Get("scan::3f2504e0-4f89-11d3-9a0c-0305e82c3301"));
    }

    [Fact]
    public void DomainTypes_ReferenceNoCouchbaseTypes()
    {
        // Act
        var domainTypes = typeof(ShipmentScan).Assembly.GetTypes()
            .Where(type => type.Namespace == "ApexPost.Courier.Offline.Domain")
            .ToList();

        // Assert
        Assert.NotEmpty(domainTypes);

        foreach (var type in domainTypes)
        {
            foreach (var used in TypesUsedBy(type))
            {
                var assembly = used.Assembly.GetName().Name ?? string.Empty;
                Assert.False(
                    assembly.StartsWith("Couchbase", StringComparison.Ordinal),
                    $"{type.Name} uses {used.FullName} from {assembly}.");
            }
        }
    }

    [Fact]
    public void DomainSourceFiles_NeverMentionCouchbase()
    {
        // Arrange
        var folder = Path.Combine(FindRepositoryRoot(), "ApexPost.Courier.Offline", "Domain");

        // Act
        var files = Directory.GetFiles(folder, "*.cs");

        // Assert
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            Assert.DoesNotContain("Couchbase", File.ReadAllText(file), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheLibrary_UsesNoNetworkApis_SoItWorksWithoutAConnection()
    {
        // Act
        var referenced = typeof(CourierDatabase).Assembly.GetReferencedAssemblies().Select(name => name.Name ?? string.Empty).ToList();

        // Assert
        Assert.DoesNotContain(referenced, name => name.StartsWith("System.Net", StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ApexPost.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Run these tests from a checkout of the repository (ApexPost.slnx was not found above the test output).");
    }

    // Every type that appears in a signature of the given type: base type, interfaces, fields, properties, methods and events.
    private static IEnumerable<Type> TypesUsedBy(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var found = new HashSet<Type>();

        void Add(Type? candidate)
        {
            if (candidate is null || !found.Add(candidate))
            {
                return;
            }

            Add(candidate.GetElementType());
            foreach (var argument in candidate.IsGenericType ? candidate.GetGenericArguments() : [])
            {
                Add(argument);
            }
        }

        Add(type.BaseType);
        foreach (var contract in type.GetInterfaces())
        {
            Add(contract);
        }

        foreach (var field in type.GetFields(all))
        {
            Add(field.FieldType);
        }

        foreach (var property in type.GetProperties(all))
        {
            Add(property.PropertyType);
        }

        foreach (var evt in type.GetEvents(all))
        {
            Add(evt.EventHandlerType);
        }

        foreach (var method in type.GetMethods(all))
        {
            Add(method.ReturnType);
            foreach (var parameter in method.GetParameters())
            {
                Add(parameter.ParameterType);
            }
        }

        foreach (var constructor in type.GetConstructors(all))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                Add(parameter.ParameterType);
            }
        }

        foreach (var attribute in type.GetCustomAttributesData())
        {
            Add(attribute.AttributeType);
        }

        return found;
    }
}
