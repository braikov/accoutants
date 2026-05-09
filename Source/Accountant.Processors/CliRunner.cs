namespace Accountant.Processors;

internal static class CliRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? 2 : 0;
        }

        return args[0] switch
        {
            "extract" => await ExtractCommand.RunAsync(args[1..]),
            "normalize" => NormalizeCommand.Run(args[1..]),
            _ => UnknownCommand(args[0]),
        };
    }

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"Unknown command '{cmd}'.");
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            Accountant.Processors — batch sandbox CLI

            USAGE:
              dotnet run --project Accountant.Processors -- <command> [options]

            COMMANDS:
              extract     Run a vendor extractor over an image folder.
              normalize   Coerce null nested objects in existing extraction JSONs (no API calls).

            Run a command with --help for command-specific options.
            """);
    }
}
