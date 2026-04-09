using System;
using System.IO;

class Program {
    static void Main(string[] args) {
        string path = args[0];
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read)) {
            while (ReadLine(fs) != "end_header") { }
            long pos = fs.Position;
            Console.WriteLine("First 100 bytes:");
            for(int i=0; i<100; i++) {
                Console.Write(fs.ReadByte().ToString("X2") + " ");
            }
            Console.WriteLine();
        }
    }
    static string ReadLine(FileStream fs) {
        var c = new System.Collections.Generic.List<char>();
        int b;
        while ((b = fs.ReadByte()) != -1) {
            char ch = (char)b;
            if (ch == '\n') break;
            if (ch != '\r') c.Add(ch);
        }
        return new string(c.ToArray());
    }
}
