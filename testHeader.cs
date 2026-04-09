using System;
using System.IO;
using System.Collections.Generic;

class Program {
    static void Main(string[] args) {
        using (var fs = new FileStream(args[0], FileMode.Open, FileAccess.Read)) {
            while (true) {
                string line = ReadLine(fs);
                Console.WriteLine(line);
                if (line == "end_header") break;
            }
        }
    }
    static string ReadLine(FileStream fs) {
        var chars = new List<char>();
        int b;
        while ((b = fs.ReadByte()) != -1) {
            char c = (char)b;
            if (c == '\n') break;
            if (c != '\r') chars.Add(c);
        }
        return new string(chars.ToArray());
    }
}
