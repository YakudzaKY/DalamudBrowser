using System;
class Program {
    static void Main() {
        var path = "/path/to/file.html";
        var uri = new Uri(path, UriKind.Absolute);
        Console.WriteLine($"AbsoluteUri: '{uri.AbsoluteUri}'");
    }
}
