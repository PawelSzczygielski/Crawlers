internal static class ConsoleUi
{
    internal static int ShowMenu(string prompt, string[] options)
    {
        int selected = 0;
        ConsoleKey key;

        do
        {
            Console.Clear();
            PrintBanner();
            Console.WriteLine(prompt);
            Console.WriteLine();
            for (int i = 0; i < options.Length; i++)
            {
                if (i == selected)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"  > {options[i]}");
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine($"    {options[i]}");
                }
            }

            key = Console.ReadKey(true).Key;
            if (key == ConsoleKey.UpArrow)   selected = (selected - 1 + options.Length) % options.Length;
            if (key == ConsoleKey.DownArrow) selected = (selected + 1) % options.Length;
        }
        while (key != ConsoleKey.Enter);

        return selected;
    }

    private static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(@" __  __              ____                ");
        Console.WriteLine(@"|  \/  | __ _  __ _ / ___|_ __ __ ___      __");
        Console.WriteLine(@"| |\/| |/ _` |/ _` | |   | '__/ _` \ \ /\ / /");
        Console.WriteLine(@"| |  | | (_| | (_| | |___| | | (_| |\ V  V / ");
        Console.WriteLine(@"|_|  |_|\__,_|\__, |\____|_|  \__,_| \_/\_/  ");
        Console.WriteLine(@"               |___/                           ");
        Console.ResetColor();
        Console.WriteLine();
    }
}
