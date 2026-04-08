using System;
using System.Threading.Tasks;
using DataRequestAgent;

class Program
{
    static async Task Main(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════════════╗");
        Console.WriteLine("║       AI Data Request Agent — Prototype      ║");
        Console.WriteLine("║              We Code For Good                ║");
        Console.WriteLine("╚══════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();

        var db       = new DatabaseService();
        var ollama   = new OllamaService();
        var logger   = new RequestLogger();

        // Seed the database with sample data on first run
        await db.InitialiseAsync();

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("\nAsk a question (or type 'logs' to view history, 'exit' to quit): ");
            Console.ResetColor();

            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input)) continue;
            if (input.ToLower() == "exit") break;

            if (input.ToLower() == "logs")
            {
                logger.PrintLogs();
                continue;
            }

            Console.WriteLine();
            var agent = new AgentOrchestrator(db, ollama, logger);
            await agent.HandleQueryAsync(input);
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("\nGoodbye!");
        Console.ResetColor();
    }
}
