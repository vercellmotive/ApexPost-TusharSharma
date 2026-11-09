using ApexPost.Courier.Offline.Harness;
using ApexPost.Courier.Offline.Storage;
using ApexPost.Courier.Offline.Sync;
using Couchbase.Lite.Logging;

// Couchbase Lite writes its own diagnostics to the console. The harness prints its own listener log, so hide those.
LogSinks.Console = new ConsoleLogSink(LogLevel.None);

string? dataDirectory = null;
var explain = false;
for (var index = 0; index < args.Length; index++)
{
    if (args[index] == "--data" && index + 1 < args.Length)
    {
        dataDirectory = args[++index];
    }
    else if (args[index] == "--explain")
    {
        explain = true;
    }
    else
    {
        Console.Error.WriteLine("Usage: ApexPost.Courier.Offline.Harness [--data <folder for the database>] [--explain]");
        return args[index] is "--help" or "-h" ? 0 : 2;
    }
}

dataDirectory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexPost", "courier");
Directory.CreateDirectory(dataDirectory);
var listenerLog = Path.Combine(dataDirectory, "listener.log");

// Every change notification is written to the console and appended to listener.log.
void Log(string line)
{
    var stamped = $"{DateTimeOffset.UtcNow:HH:mm:ss.fff} {line}";
    Console.WriteLine($"  {stamped}");
    File.AppendAllText(listenerLog, stamped + Environment.NewLine);
}

using var database = new CourierDatabase(new CourierDatabaseOptions { Directory = dataDirectory, Log = Log });

// Ctrl+C closes the database properly, which also removes the change listener.
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    database.Dispose();
    Environment.Exit(130);
};

var repository = new CouchbaseScanRepository(database);

// --explain prints each SQL++ statement and the index it uses, then exits without showing the menu.
if (explain)
{
    foreach (var (plan, number) in repository.ExplainQueries().Select((plan, index) => (plan, index + 1)))
    {
        Console.WriteLine($"{number}. {plan.Name}");
        Console.WriteLine($"   SQL++ : {plan.Sql}");
        Console.WriteLine($"   Plan  : {plan.AccessPath}");
    }

    return 0;
}

var outbox = new OutboxService(repository, new FakeShipmentUploader());
var app = new HarnessApp(database, repository, outbox, Console.In, Console.Out, echoInput: Console.IsInputRedirected, settleDelay: TimeSpan.FromMilliseconds(250));
return await app.RunAsync();
