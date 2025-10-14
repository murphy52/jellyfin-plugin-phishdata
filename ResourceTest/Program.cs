using System;
using System.Reflection;
using System.IO;

// Simple test to check if embedded resources are accessible
var assembly = Assembly.LoadFrom("/tmp/jellyfin-plugin-phishnet/Jellyfin.Plugin.PhishNet/bin/Debug/net8.0/Jellyfin.Plugin.PhishNet.dll");

Console.WriteLine("Available embedded resources:");
foreach (var name in assembly.GetManifestResourceNames())
{
    Console.WriteLine($"- {name}");
}

Console.WriteLine("\nTesting image resources:");
var thumbStream = assembly.GetManifestResourceStream("Jellyfin.Plugin.PhishNet.thumb.png");
var logoStream = assembly.GetManifestResourceStream("Jellyfin.Plugin.PhishNet.logo.png");

Console.WriteLine($"thumb.png stream: {(thumbStream != null ? $"OK ({thumbStream.Length} bytes)" : "NOT FOUND")}");
Console.WriteLine($"logo.png stream: {(logoStream != null ? $"OK ({logoStream.Length} bytes)" : "NOT FOUND")}");

thumbStream?.Dispose();
logoStream?.Dispose();