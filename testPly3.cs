using System;
using System.IO;
using System.Collections.Generic;

class Program {
    static void Main(string[] args) {
        string path = args[0];
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read)) {
            while (ReadLine(fs) != "end_header") { }
            long headerEndPos = fs.Position;
            Console.WriteLine("Header ends at: " + headerEndPos);
            int dataLen = (int)(fs.Length - headerEndPos);
            Console.WriteLine("Data length: " + dataLen);
            Console.WriteLine("Data length / 1140772: " + ((double)dataLen / 1140772.0));
            fs.Position = headerEndPos;
            for(int i=0; i<30; i++) {
                Console.Write(fs.ReadByte().ToString("X2") + " ");
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
