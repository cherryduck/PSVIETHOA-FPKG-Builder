using System.Globalization;
using System.Text;
using PsViethoa.FpkgBuilder.Cli;

Console.OutputEncoding = Encoding.UTF8;
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
return await CommandLine.RunAsync(args);
