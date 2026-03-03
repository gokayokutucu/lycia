using Microsoft.Owin.Hosting;
using Sample.Product.NetFramework481.API;
using System;

namespace Sample.Product.NetFramework481.API;

public class Program
{
    public static void Main(string[] args)
    {
        var port = Environment.GetEnvironmentVariable("PORT") ?? "2000";
        var url = $"http://localhost:{port}/";

        Console.WriteLine($"Starting Product API on {url}...");

        using (WebApp.Start<Startup>(url))
        {
            Console.WriteLine($"✅ Product API running at {url}");
            Console.WriteLine("Press Enter to stop...");
            Console.ReadLine();
        }
    }
}
