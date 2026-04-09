using System;
using System.IO;
using System.Collections.Generic;

class Program {
    static void Main(string[] args) {
        string path = args[0];
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
        using (var reader = new BinaryReader(fs)) {
            int vertexCount = 0;
            string line;
            while ((line = ReadLine(fs)) != "end_header") {
                if (line.StartsWith("element vertex")) {
                    var parts = line.Split(' ');
                    int.TryParse(parts[2], out vertexCount);
                }
            }
            Console.WriteLine("Vertices: " + vertexCount);
            if (vertexCount > 0) {
                float x = reader.ReadSingle();
                float y = reader.ReadSingle();
                float z = reader.ReadSingle();
                byte r = reader.ReadByte();
                byte g = reader.ReadByte();
                byte b = reader.ReadByte();
                Console.WriteLine("Point 1: " + x + ", " + y + ", " + z + ", color: " + r + ", " + g + ", " + b);
                float x2 = reader.ReadSingle();
                Console.WriteLine("Point 2 X: " + x2);
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
