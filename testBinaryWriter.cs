using System;
using System.IO;

class Program {
    static void Main(string[] args) {
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms)) {
            writer.Write((float)0f);
            writer.Write((float)0f);
            writer.Write((float)0f);
            writer.Write((byte)255);
            writer.Write((byte)0);
            writer.Write((byte)0);
            Console.WriteLine("Bytes written: " + ms.Length);
        }
    }
}
