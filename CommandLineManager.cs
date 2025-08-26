using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using Microsoft.SqlServer.Server;
using RainMeadow.Shared;

namespace RainMeadow.IndividualServer
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    class CommandLineArgumentAttribute : Attribute {
        public static void InitializeCommandLine()
        {
            string[] arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
            arguments = arguments.SelectMany(x =>
            {
                if (x == "=") return [ x ];
                var split = x.Split('=');
                split = split.Aggregate<string, IEnumerable<string>>(Enumerable.Empty<string>(), (a, b) =>
                {
                    if (string.IsNullOrWhiteSpace(b))
                    {
                        if (!a.Any()) return a;
                        else return a.Append("=");
                    }

                    if (!a.Any()) return a.Append(b);
                    else return a.Append("=").Append(b);
                }).ToArray();
                return split;
            }).ToArray();


            var fields = typeof(IndividualServer).GetFields().Where(x => x.IsDefined(typeof(CommandLineArgumentAttribute), true)).ToDictionary(
                x => x,
                x => (CommandLineArgumentAttribute)x.GetCustomAttributes(typeof(CommandLineArgumentAttribute), false).First()
            );

            for (int i = 0; i < arguments.Length; i++)
            {
                try
                {
                    var selected = fields.First(x => x.Key.Name.ToLowerInvariant() == arguments[i].ToLowerInvariant());
                    if (arguments[++i] != "=") throw new FormatException($"Expected = as #{i}: {string.Join(" ", arguments)}");
                    if (selected.Key.FieldType == typeof(string)) selected.Key.SetValue(null, arguments[++i]);
                    else if (selected.Key.FieldType.GetMethod("Parse", [typeof(string)]) is MethodInfo tryParseMethod)
                    {
                        selected.Key.SetValue(null, tryParseMethod.Invoke(null, [arguments[++i]]));
                    }
                    else if (selected.Key.FieldType.GetConstructor([typeof(string)]) is ConstructorInfo stringConstructor)
                    {
                        selected.Key.SetValue(null, stringConstructor.Invoke([arguments[++i]]));
                    }
                    else throw new FormatException($"No parsing function for type {selected.Key.FieldType.Name}");

                    RainMeadow.Debug($"set IndividualServer.{selected.Key.Name} to {arguments[i]}");
                }
                catch (Exception except)
                {
                    if (except is InvalidOperationException) throw new FormatException($"Unexpected Argument #{i}: {string.Join(" ", arguments)}", except);
                    if (except is FormatException) throw;
                    if (except is IndexOutOfRangeException) throw new FormatException($"Expected Argument #{i}: {string.Join(" ", arguments)}", except);
                    throw new FormatException($"Bad command line argument #{i}: {string.Join(" ", arguments)}", except);
                }
            }
        }
    }
}
