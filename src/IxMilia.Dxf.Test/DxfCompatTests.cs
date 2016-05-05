﻿// Copyright (c) IxMilia.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using IxMilia.Dxf.Entities;
using IxMilia.Dxf.Objects;
using Xunit;

namespace IxMilia.Dxf.Test
{
    public class DxfCompatTests : AbstractDxfTests
    {
        private static readonly string MinimumFileText = @"
  0
SECTION
  2
ENTITIES
  0
LINE
  8
0
 10
0.0
 20
0.0
 30
0.0
 11
10.0
 21
10.0
 31
0.0
  0
ENDSEC
  0
EOF
".Trim();

        private class ManageTemporaryDirectory : IDisposable
        {
            public string DirectoryPath { get; }

            public ManageTemporaryDirectory()
            {
                DirectoryPath = Path.Combine(
                    Path.GetTempPath(),
                    Guid.NewGuid().ToString()
                    );
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, true);
                }

                Directory.CreateDirectory(DirectoryPath);
            }

            public void Dispose()
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, true);
                }
            }
        }

        [TeighaConverterExistsFact]
        public void IxMiliaReadTeighaTest()
        {
            // use Teigha to convert a minimum-working-file to each of its supported versions and try to open with IxMilia
            var exceptions = new List<Exception>();
            var versions = new[] { DxfAcadVersion.R9, DxfAcadVersion.R10, DxfAcadVersion.R12, DxfAcadVersion.R13, DxfAcadVersion.R14, DxfAcadVersion.R2000, DxfAcadVersion.R2004, DxfAcadVersion.R2007, DxfAcadVersion.R2010, DxfAcadVersion.R2013 };
            foreach (var desiredVersion in versions)
            {
                using (var input = new ManageTemporaryDirectory())
                using (var output = new ManageTemporaryDirectory())
                {
                    var inputDir = input.DirectoryPath;
                    var outputDir = output.DirectoryPath;
                    var barePath = Path.Combine(inputDir, "bare.dxf");
                    File.WriteAllText(barePath, MinimumFileText);
                    AssertTeighaConvert(inputDir, outputDir, desiredVersion);

                    var convertedFilePath = Directory.EnumerateFiles(outputDir, "*.dxf").Single();
                    using (var fs = new FileStream(convertedFilePath, FileMode.Open))
                    {
                        try
                        {
                            var file = DxfFile.Load(fs);
                            Assert.IsType<DxfLine>(file.Entities.Single());
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(ex);
                        }
                    }
                }
            }

            if (exceptions.Count > 0)
            {
                throw new AggregateException("Error reading Teigha-produced files", exceptions);
            }
        }

        [TeighaConverterExistsFact]
        public void TeighaReadIxMiliaNewFileCompatTest()
        {
            TestTeighaReadIxMiliaGeneratedFile(() =>
            {
                var file = new DxfFile();
                file.Entities.Add(new DxfLine(new DxfPoint(0, 0, 0), new DxfPoint(10, 10, 0)));
                return file;
            });
        }

        [TeighaConverterExistsFact]
        public void TeighaReadIxMiliaNormalizedFileCompatTest()
        {
            TestTeighaReadIxMiliaGeneratedFile(() =>
            {
                var file = Parse(MinimumFileText);
                return file;
            });
        }

        [TeighaConverterExistsFact]
        public void TeighaReadAllEntitiesTest()
        {
            // create a file with all entities and ensure Teigha can read it
            var file = new DxfFile();
            file.Header.Version = DxfAcadVersion.R2013;
            var assembly = typeof(DxfFile).Assembly;
            foreach (var type in assembly.GetTypes())
            {
                if (DxfReaderWriterTests.IsEntityOrDerived(type))
                {
                    var ctor = type.GetConstructor(Type.EmptyTypes);
                    if (ctor != null)
                    {
                        // add the entity with its default initialized values
                        var entity = (DxfEntity)ctor.Invoke(new object[0]);
                        file.Entities.Add(entity);
                    }
                }
            }

            using (var input = new ManageTemporaryDirectory())
            using (var output = new ManageTemporaryDirectory())
            {
                var inputFile = Path.Combine(input.DirectoryPath, "file.dxf");
                using (var fs = new FileStream(inputFile, FileMode.Create))
                {
                    file.Save(fs);
                }

                AssertTeighaConvert(input.DirectoryPath, output.DirectoryPath, DxfAcadVersion.R2013);
            }
        }

        [TeighaConverterExistsFact]
        public void TeighaReadAllObjectsTest()
        {
            // create a file with all objects and ensure Teigha can read it
            var file = new DxfFile();
            file.Header.Version = DxfAcadVersion.R2013;
            var assembly = typeof(DxfFile).Assembly;
            foreach (var type in assembly.GetTypes())
            {
                if (DxfReaderWriterTests.IsObjectOrDerived(type))
                {
                    var ctor = type.GetConstructor(Type.EmptyTypes);
                    if (ctor != null)
                    {
                        // add the object with its default initialized values
                        var obj = (DxfObject)ctor.Invoke(new object[0]);
                        file.Objects.Add(obj);
                    }
                }
            }

            using (var input = new ManageTemporaryDirectory())
            using (var output = new ManageTemporaryDirectory())
            {
                var inputFile = Path.Combine(input.DirectoryPath, "file.dxf");
                using (var fs = new FileStream(inputFile, FileMode.Create))
                {
                    file.Save(fs);
                }

                AssertTeighaConvert(input.DirectoryPath, output.DirectoryPath, DxfAcadVersion.R2013);
            }
        }

        [AutoCadExistsFact]
        public void IxMiliaReadAutoCadTest()
        {
            // use AutoCad to convert a minimum-working-file to each of its supported versions and try to open with IxMilia
            var exceptions = new List<Exception>();

            using (var directory = new ManageTemporaryDirectory())
            {
                var tempDir = directory.DirectoryPath;
                var barePath = Path.Combine(tempDir, "bare.dxf");
                File.WriteAllText(barePath, MinimumFileText);

                var scriptLines = new List<string>();
                scriptLines.Add("FILEDIA 0");
                scriptLines.Add($"DXFIN \"{barePath}\"");
                foreach (var version in new[] { "R12", "2000", "2004", "2007", "2010", "2013" })
                {
                    var fullPath = Path.Combine(tempDir, $"result-{version}.dxf");
                    scriptLines.Add($"DXFOUT \"{fullPath}\" V {version} 16");
                }

                scriptLines.Add("FILEDIA 1");
                scriptLines.Add("QUIT Y");
                scriptLines.Add("");
                var scriptPath = Path.Combine(tempDir, "script.scr");
                File.WriteAllLines(scriptPath, scriptLines);

                ExecuteAutoCadScript(scriptPath);

                foreach (var resultPath in Directory.EnumerateFiles(tempDir, "result-*.dxf"))
                {
                    using (var fs = new FileStream(resultPath, FileMode.Open))
                    {
                        try
                        {
                            var file = DxfFile.Load(fs);
                            Assert.IsType<DxfLine>(file.Entities.Single());
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(ex);
                        }
                    }
                }
            }

            if (exceptions.Count > 0)
            {
                throw new AggregateException("Error reading AutoCad-produced files", exceptions);
            }
        }

        [AutoCadExistsFact]
        public void AutoCadReadIxMiliaFileCompatTest()
        {
            // save a DXF file in all the formats that IxMilia.Dxf and AutoCAD support and try to get AutoCAD to read all of them
            using (var directory = new ManageTemporaryDirectory())
            {
                var tempDir = directory.DirectoryPath;
                var versions = new[]
                {
                    Tuple.Create(DxfAcadVersion.R12, "R12"),
                    Tuple.Create(DxfAcadVersion.R2000, "2000"),
                    Tuple.Create(DxfAcadVersion.R2004, "2004"),
                    Tuple.Create(DxfAcadVersion.R2007, "2007"),
                    Tuple.Create(DxfAcadVersion.R2010, "2010"),
                    Tuple.Create(DxfAcadVersion.R2013, "2013")
                };

                // save the minimal file with all versions
                var file = new DxfFile();
                var text = new DxfText(DxfPoint.Origin, 2.0, "");
                file.Entities.Add(text);
                foreach (var pair in versions)
                {
                    var fileName = $"file.{pair.Item1}.dxf";
                    file.Header.Version = pair.Item1;
                    text.Value = pair.Item1.ToString();
                    var outputPath = Path.Combine(tempDir, fileName);
                    using (var fs = new FileStream(outputPath, FileMode.Create))
                    {
                        file.Save(fs);
                    }
                }

                // open each file in AutoCAD and try to write it back out
                var lines = new List<string>();
                lines.Add("FILEDIA 0");
                foreach (var pair in versions)
                {
                    lines.Add("ERASE ALL ");
                    lines.Add($"DXFIN \"{Path.Combine(tempDir, $"file.{pair.Item1}.dxf")}\"");
                    lines.Add($"DXFOUT \"{Path.Combine(tempDir, $"result.{pair.Item1}.dxf")}\" V {pair.Item2} 16");
                }

                lines.Add("FILEDIA 1");
                lines.Add("QUIT Y");

                // create and execute the script
                var scriptPath = Path.Combine(tempDir, "script.scr");
                File.WriteAllLines(scriptPath, lines);

                ExecuteAutoCadScript(scriptPath);

                // check each resultant file for the correct version and text
                foreach (var pair in versions)
                {
                    DxfFile dxf;
                    using (var fs = new FileStream(Path.Combine(tempDir, $"result.{pair.Item1}.dxf"), FileMode.Open))
                    {
                        dxf = DxfFile.Load(fs);
                    }

                    Assert.Equal(pair.Item1, dxf.Header.Version);
                    Assert.Equal(pair.Item1.ToString(), ((DxfText)dxf.Entities.Single()).Value);
                }
            }
        }

        private void TestTeighaReadIxMiliaGeneratedFile(Func<DxfFile> fileGenerator)
        {
            // save a DXF file in all the formats that IxMilia.Dxf supports and try to get Teigha to read all of them
            using (var input = new ManageTemporaryDirectory())
            {
                var inputDir = input.DirectoryPath;

                // save the minimum file with all versions
                var file = fileGenerator();
                var allIxMiliaVersions = new[]
                {
                DxfAcadVersion.R9,
                DxfAcadVersion.R10,
                DxfAcadVersion.R11,
                DxfAcadVersion.R12,
                DxfAcadVersion.R13,
                DxfAcadVersion.R14,
                DxfAcadVersion.R2000,
                DxfAcadVersion.R2004,
                DxfAcadVersion.R2007,
                DxfAcadVersion.R2010,
                DxfAcadVersion.R2013
            };
                foreach (var version in allIxMiliaVersions)
                {
                    file.Header.Version = version;
                    var outputPath = Path.Combine(inputDir, $"file.{version}.dxf");
                    using (var fs = new FileStream(outputPath, FileMode.Create))
                    {
                        file.Save(fs);
                    }
                }

                // invoke the Teigha converter
                using (var output = new ManageTemporaryDirectory())
                {
                    var outputDir = output.DirectoryPath;
                    AssertTeighaConvert(inputDir, outputDir, DxfAcadVersion.R2010);
                }
            }
        }

        private void WaitForProcess(string fileName, string arguments)
        {
            var psi = new ProcessStartInfo();
            psi.FileName = fileName;
            psi.Arguments = arguments;
            var proc = Process.Start(psi);
            proc.WaitForExit();
            Assert.Equal(0, proc.ExitCode);
        }

        private void ExecuteAutoCadScript(string pathToScript)
        {
            WaitForProcess(AutoCadExistsFactAttribute.GetPathToAutoCad(), $"/b \"{pathToScript}\"");
        }

        private void AssertTeighaConvert(string inputDirectory, string outputDirectory, DxfAcadVersion desiredVersion)
        {
            WaitForProcess(TeighaConverterExistsFactAttribute.GetPathToFileConverter(), GenerateTeighaArguments(inputDirectory, outputDirectory, desiredVersion));
            var errors = Directory.EnumerateFiles(outputDirectory, "*.err").Select(path => path + ":\r\n" + File.ReadAllText(path)).ToList();
            // TODO: gather the files that couldn't be converted
            if (errors.Count > 0)
            {
                throw new Exception("Teigha error converting files: " + string.Join("", errors));
            }
        }

        private string GenerateTeighaArguments(string inputDirectory, string outputDirectory, DxfAcadVersion desiredVersion)
        {
            string teighaVersion;
            switch (desiredVersion)
            {
                case DxfAcadVersion.R9:
                    teighaVersion = "ACAD9";
                    break;
                case DxfAcadVersion.R10:
                    teighaVersion = "ACAD10";
                    break;
                case DxfAcadVersion.R12:
                    teighaVersion = "ACAD12";
                    break;
                case DxfAcadVersion.R13:
                    teighaVersion = "ACAD13";
                    break;
                case DxfAcadVersion.R14:
                    teighaVersion = "ACAD14";
                    break;
                case DxfAcadVersion.R2000:
                    teighaVersion = "ACAD2000";
                    break;
                case DxfAcadVersion.R2004:
                    teighaVersion = "ACAD2004";
                    break;
                case DxfAcadVersion.R2007:
                    teighaVersion = "ACAD2007";
                    break;
                case DxfAcadVersion.R2010:
                    teighaVersion = "ACAD2010";
                    break;
                case DxfAcadVersion.R2013:
                    teighaVersion = "ACAD2013";
                    break;
                default:
                    throw new InvalidOperationException("Unsupported Teigha version " + desiredVersion);
            }

            //                                                                              recurse audit
            return $@"""{inputDirectory}"" ""{outputDirectory}"" ""{teighaVersion}"" ""DXF"" ""0"" ""1""";
        }
    }
}
