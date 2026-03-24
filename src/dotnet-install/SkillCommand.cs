using System.Reflection;

static class SkillCommand
{
    public static int Run()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("skill.md", StringComparison.OrdinalIgnoreCase));

        if (name is null)
        {
            Console.Error.WriteLine("error: embedded skill not found");
            return 1;
        }

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        Console.Write(reader.ReadToEnd());
        return 0;
    }
}
