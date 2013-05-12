using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace PostBuild {
    internal class Program {
        /// <summary>
        ///     Used to locate and parse the string holding the product
        ///     version in ProductInfo.cs.
        /// </summary>
        private static readonly Regex VersionMatch
            = new Regex("^(.*\\sProductVersion\\s.*\\.)(\\d+)(\".*)$");

        /// <summary>
        ///     Program Entry Point
        /// </summary>
        static int Main(string[] args) {
            Console.WriteLine("PostBuild - Version string management");
            if (args.Length != 4) {
                Console.WriteLine("Usage: PostBuild <executable> <productinfo.cs> <updates.xml> <update-uri>");
                return 1;
            }
            
            // Extract the desired information from the assembly
            Console.WriteLine("Loading Executable \"{0}\".", args[0]);
            var assembly = Assembly.LoadFrom(args[0]);
            var assemblyName = assembly.GetName();
            var assemblyVersion = assembly
                .GetCustomAttributes(
                    typeof(AssemblyFileVersionAttribute),
                    true)
                .Cast<AssemblyFileVersionAttribute>()
                .Single();
            Console.WriteLine("{0}, FileVersion={1}",
                assemblyName,
                assemblyVersion.Version);

            // Generate the updates.xml file
            if (!String.IsNullOrEmpty(args[2])) {
                var xmlSettings = new XmlWriterSettings() {
                    Indent = true
                };
                Console.WriteLine("Writing version list to \"{0}\"", args[2]);
                using (var xmlWriter = XmlWriter.Create(args[2], xmlSettings)) {
                    xmlWriter.WriteStartElement("assembly-list");
                    xmlWriter.WriteStartElement("assembly");
                    xmlWriter.WriteAttributeString("name", assemblyName.Name);
                    xmlWriter.WriteAttributeString("version", assemblyVersion.Version);
                    xmlWriter.WriteAttributeString("update-uri", args[3]);
                    xmlWriter.WriteEndElement();
                    xmlWriter.WriteEndElement();
                    xmlWriter.Flush();
                }
            }

            // Update version number in file
            if (!String.IsNullOrEmpty(args[1])) {
                Console.WriteLine("Incrementing version string in \"{0}\"", args[1]);
                var text = File.ReadAllLines(args[1]);
                for (int n = 0; n < text.Length; ++n) {
                    var match = VersionMatch.Match(text[n]);
                    if (match.Success) {
                        var newVersion = Int16.Parse(match.Groups[2].Value) + 1;
                        text[n] = String.Format(
                            CultureInfo.InvariantCulture,
                            "{0}{1}{2}",
                            match.Groups[1].Value,
                            newVersion,
                            match.Groups[3].Value);
                    }
                }
                File.WriteAllLines(args[1], text);
            }

            // Success
            Console.WriteLine("Done");
            return 0;
        }
    }
}
