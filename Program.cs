namespace PrReviewer;

internal static class Program
{
    private static void Main(string[] args)
    {
        // Scaffold only. Real composition root (DI container, provider
        // selection, dry_run handling) lands in a later step once
        // IDiffSource, ICodeReviewer, and their implementations exist.
        Console.WriteLine("PR Reviewer - scaffold. Nothing implemented yet.");
    }
}
