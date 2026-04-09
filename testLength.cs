using System;
using System.IO;

class Program {
    static void Main(string[] args) {
        using (var fs = new FileStream(args[0], FileMode.Open)) {
            Console.WriteLine("Length: " + fs.Length);
        }
    }
}
