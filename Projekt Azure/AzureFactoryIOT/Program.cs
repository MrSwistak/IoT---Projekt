using AzureFactoryIOT;
using Microsoft.Extensions.Configuration;

public class Program
{

    public static void Main(string[] args)
    {
        var config = new ConfigurationBuilder().AddJsonFile("lineConfiguration.json").Build();
        var lineSettins = new LineSettings();
        config.GetSection("LineConfiguration").Bind(lineSettins);
        new Line(lineSettins).Up().Wait();
    }

}