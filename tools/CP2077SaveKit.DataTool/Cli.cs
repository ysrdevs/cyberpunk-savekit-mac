namespace CP2077SaveKit.DataTool;

/// <summary>Command line for the data refresh tool: `datatool &lt;command&gt; [--game|--resources|--out|--extra] [positional]`.</summary>
public sealed class Cli
{
    public required string Command { get; init; }
    public required string Game { get; init; }
    public required string Resources { get; init; }
    public string? Out { get; init; }
    public IReadOnlyList<string> Extra { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Args { get; init; } = Array.Empty<string>();

    public string ResourcePath(string file) => Path.Combine(Resources, file);

    public static Cli? Parse(string[] argv)
    {
        if (argv.Length == 0)
        {
            Usage();
            return null;
        }

        var positional = new List<string>();
        var extra = new List<string>();
        string? game = null, resources = null, output = null;
        var command = "";

        for (var i = 0; i < argv.Length; i++)
        {
            var arg = argv[i];
            switch (arg)
            {
                case "--game": game = Next(argv, ref i); break;
                case "--resources": resources = Next(argv, ref i); break;
                case "--out": output = Next(argv, ref i); break;
                case "--extra": extra.Add(Next(argv, ref i)); break;
                case "-h" or "--help": Usage(); return null;
                default:
                    if (arg.StartsWith("--")) { Console.Error.WriteLine($"unknown option: {arg}"); Usage(); return null; }
                    if (command.Length == 0) command = arg; else positional.Add(arg);
                    break;
            }
        }

        if (command.Length == 0) { Usage(); return null; }

        resources ??= DefaultResources();
        if (resources is null)
        {
            Console.Error.WriteLine("could not locate src/CP2077SaveKit.Core/Resources — pass --resources <dir>");
            return null;
        }

        game ??= DefaultGame();
        if (game is null)
        {
            Console.Error.WriteLine("could not locate the game install — pass --game \"<Cyberpunk 2077 folder>\"");
            return null;
        }

        return new Cli
        {
            Command = command,
            Game = game,
            Resources = resources,
            Out = output,
            Extra = extra,
            Args = positional,
        };
    }

    private static string? Next(string[] argv, ref int i)
    {
        if (i + 1 >= argv.Length) throw new ArgumentException($"{argv[i]} needs a value");
        return argv[++i];
    }

    private static void Usage()
    {
        Console.Error.WriteLine("""
            datatool - refresh the bundled item data from the installed game

            usage:
              datatool diag    [--game <dir>] [--resources <dir>]
              datatool dump    [--game <dir>] <Items.Name|0xhash>
              datatool names   [--game <dir>] [--resources <dir>] [--out items.bin] [--extra <file>]...
              datatool classes [--game <dir>] [--resources <dir>] [--out ItemClasses.json] [--extra <file>]...
              datatool catalog [--game <dir>] [--resources <dir>] [--out aio_catalog.json] [--extra <file>]...

            defaults:
              --game       the Steam install (~/Library/Application Support/Steam/steamapps/common/Cyberpunk 2077)
              --resources  src/CP2077SaveKit.Core/Resources in this repo
              --out        the matching file inside --resources
            """);
    }

    private static string? DefaultResources()
    {
        foreach (var root in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var dir = new DirectoryInfo(root); dir is not null; dir = dir.Parent)
            {
                if (!File.Exists(Path.Combine(dir.FullName, "CP2077SaveKit.sln"))) continue;
                var resources = Path.Combine(dir.FullName, "src", "CP2077SaveKit.Core", "Resources");
                if (Directory.Exists(resources)) return resources;
            }
        }
        return null;
    }

    private static string? DefaultGame()
    {
        var steam = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "Steam", "steamapps", "common", "Cyberpunk 2077");
        return File.Exists(Path.Combine(steam, "r6", "cache", "tweakdb.bin")) ? steam : null;
    }
}
