using System;
using System.IO;
using System.Collections.Generic;

class Program {
    static void Main(string[] args) {
        string path = args[0];
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
        using (var reader = new BinaryReader(fs)) {
            while (ReadLine(fs) != "end_header") { }
            Console.WriteLine("First 30 bytes:");
            for(int i=0; i<30; i++) {
                Console.Write(reader.ReadByte().ToString("X2") + " ");
            }
            Console.WriteLine();
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
