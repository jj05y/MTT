using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace MTT
{
    public class ConvertService
    {
        /// <summary>
        /// A delegate to support logging
        /// </summary>
        /// <param name="s">The string to log.</param>
        /// <param name="args">The arguments.</param>
        public delegate void LogAction(string s, params object[] args);

        /// <summary>
        /// The current working directory for the convert process
        /// </summary>
        public string WorkingDirectory { get; set; }

        /// <summary>
        /// The directory to save the ts models
        /// </summary>
        public string ConvertDirectory { get; set; }

        /// <summary>
        /// Comments at the top of each file that it was auto generated
        /// </summary>
        public bool AutoGeneratedTag { get; set; } = true; //default value if one is not provided;

        /// <summary>
        /// Determines whether to generate numeric or string values in typescript enums
        /// </summary>
        public EnumValues EnumValues { get; internal set; }

        private List<ModelFile> Models { get; set; }

        private string LocalWorkingDir { get; set; }

        private string LocalConvertDir { get; set; }

        private LogAction log { get; set; }

        public ConvertService(LogAction log)
        {
            Models = new List<ModelFile>();
            this.log = log;
        }

        public bool Execute()
        {
            log("Starting MTT ConvertService");
            GetWorkingDirectory();
            GetConvertDirectory();
            LoadModels();

            try
            {
                BreakDown();
            }
            catch (Exception e)
            {
                throw e;
            }

            Convert();
            log("Finished MTT ConvertService");
            return true;
        }

        private void GetWorkingDirectory()
        {
            var dir = Directory.GetCurrentDirectory();

            if (string.IsNullOrEmpty(WorkingDirectory))
            {
                log("Using Default Working Directory {0}", dir);
                LocalWorkingDir = dir;
                return;
            }

            var localdir = Path.Combine(dir, WorkingDirectory);

            if (!Directory.Exists(localdir))
            {
                log("Working Directory does not exist {0}, creating..", localdir);
                Directory.CreateDirectory(localdir).Create();
                LocalWorkingDir = localdir;
                return;
            }

            log("Working Directory {0}", localdir);
            LocalWorkingDir = localdir;
            return;
        }

        private void GetConvertDirectory()
        {
            var dir = Directory.GetCurrentDirectory();

            if (string.IsNullOrEmpty(ConvertDirectory))
            {
                log("Using Default Convert Directory {0} - this does not always update", dir);
                LocalConvertDir = dir;
                return;
            }

            var localdir = Path.Combine(dir, ConvertDirectory);

            if (!Directory.Exists(localdir))
            {
                log("Convert Directory does not exist {0}, creating..", localdir);
            }
            else
            {
                log("Convert Directory {0}", localdir);
                Directory.Delete(localdir, true);
            }

            Directory.CreateDirectory(localdir).Create();
            LocalConvertDir = localdir;
            return;
        }

        private void LoadModels(string dirname = "")
        {
            if (String.IsNullOrEmpty(dirname))
            {
                dirname = LocalWorkingDir;
            }

            var files = Directory.GetFiles(dirname);
            var dirs = Directory.GetDirectories(dirname);

            foreach (var dir in dirs)
            {
                string d = dir.Replace(dirname, String.Empty);

                if (!String.IsNullOrEmpty(d))
                {
                    LoadModels(dir);
                }
            }

            foreach (var file in files)
            {
                AddModel(file, dirname.Replace(LocalWorkingDir, String.Empty));
            }
        }

        private void AddModel(string file, string structure = "")
        {
            structure = structure.Replace(@"\", "/");
            string[] explodedDir = file.Replace(@"\", "/").Split('/');

            string fileName = explodedDir[explodedDir.Length - 1];

            string[] fileInfo = File.ReadAllLines(file);

            var modelFile = new ModelFile()
            {
                Name = ToPascalCase(fileName.Replace(".cs", String.Empty)),
                Info = fileInfo,
                Structure = structure
            };

            Models.Add(modelFile);
        }

        private void BreakDown()
        {
            foreach (var file in Models)
            {
                foreach (var _line in file.Info)
                {
                    var line = StripComments(_line);

                    if (line.IsPreProcessorDirective())
                    {
                        continue;
                    }

                    var modLine = new List<string>(ExplodeLine(line));

                    // Check for correct structure
                    if ((line.StrictContains("enum") && line.Contains("{")) || (line.StrictContains("class") && line.Contains("{")))
                    {
                        throw new ArgumentException(string.Format("For parsing, C# DTO's must use curly braces on the next line\nin {0}.cs\n\"{1}\"", file.Name, _line));
                    }

                    // Enum declaration
                    if (line.StrictContains("enum"))
                    {
                        if (modLine.Count > 2)
                        {
                            file.Inherits = modLine[modLine.Count - 1];
                        }

                        file.IsEnum = true;

                        int value = 0;

                        foreach (var _enumLine in file.Info)
                        {
                            var enumLine = StripComments(_enumLine);

                            if (enumLine.IsPreProcessorDirective())
                            {
                                continue;
                            }

                            modLine = new List<string>(ExplodeLine(enumLine));

                            if (IsEnumObject(enumLine))
                            {
                                String name = modLine[0];
                                bool isImplicit = false;

                                if (modLine.Count > 1 && modLine[1] == "=")
                                {
                                    try
                                    {
                                        var tmpValue = modLine[2].Replace(",", "");
                                        if (tmpValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                                        {
                                            value = System.Convert.ToInt32(tmpValue, 16);
                                        }
                                        else
                                        {
                                            value = Int32.Parse(tmpValue);
                                        }
                                    }
                                    catch (System.Exception e)
                                    {
                                        throw e;
                                    }
                                }
                                else
                                {
                                    isImplicit = true;
                                }

                                EnumObject obj = new EnumObject()
                                {
                                    Name = name.Replace(",", ""),
                                    Value = value,
                                    IsImplicit = isImplicit
                                };

                                file.EnumObjects.Add(obj);
                            }
                        }

                        break;  //since enums are different we move onto the next file
                    }


                    // Class declaration
                    if (line.StrictContains("class") && line.Contains(":"))
                    {
                        string inheritance = modLine[modLine.Count - 1];
                        file.Inherits = inheritance;
                        file.InheritenceStructure = Find(inheritance, file);


                        /** If the class only contains inheritence we need a place holder obj */
                        LineObject obj = new LineObject() { };
                        file.Objects.Add(obj);
                    }

                    // Class property
                    if (line.StrictContains("public") && !line.StrictContains("class") && !IsContructor(line))
                    {
                        string type = modLine[0];
                        /** If the property is marked virtual, skip the virtual keyword. */
                        if (type.Equals("virtual"))
                        {
                            modLine.RemoveAt(0);
                            type = modLine[0];
                        }

                        bool IsArray = CheckIsArray(type);

                        bool isOptional = CheckOptional(type);

                        type = CleanType(type);

                        var userDefinedImport = Find(type, file);
                        var isUserDefined = !String.IsNullOrEmpty(userDefinedImport);

                        string varName = modLine[1];

                        if (varName.EndsWith(";"))
                        {
                            varName = varName.Substring(0, varName.Length - 1);
                        }

                        LineObject obj = new LineObject()
                        {
                            VariableName = varName,
                            Type = isUserDefined ? type : TypeOf(type),
                            IsArray = IsArray,
                            IsOptional = isOptional,
                            UserDefined = isUserDefined,
                            UserDefinedImport = userDefinedImport
                        };

                        file.Objects.Add(obj);
                    }
                }
            }
        }

        private string TypeOf(string type)
        {
            switch (type)
            {
                case "byte":
                case "sbyte":
                case "decimal":
                case "double":
                case "float":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                case "short":
                case "ushort":
                case "Byte":
                case "Decimal":
                case "Double":
                case "Int16":
                case "Int32":
                case "Int64":
                case "SByte":
                case "UInt16":
                case "UInt32":
                case "UInt64":
                    return "number";

                case "bool":
                case "Boolean":
                    return "boolean";

                case "string":
                case "char":
                case "String":
                case "Char":
                    return "string";

                case "DateTime":
                    return "Date";

                default: return "any";
            }
        }

        private void Convert()
        {
            log("Converting..");

            foreach (var file in Models)
            {
                DirectoryInfo di = Directory.CreateDirectory(Path.Combine(LocalConvertDir, file.Structure));
                di.Create();

                string fileName = ToCamelCase(file.Name + ".ts");
                log("Creating file {0}", fileName);
                string saveDir = Path.Combine(di.FullName, fileName);

                using (var stream = new FileStream(saveDir, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, FileOptions.SequentialScan))
                using (StreamWriter f =
                    new StreamWriter(stream, System.Text.Encoding.UTF8, 1024, false))
                {
                    var importing = false;  //only used for formatting
                    var imports = new List<string>();  //used for duplication

                    if (AutoGeneratedTag)
                    {
                        f.WriteLine("/* Auto Generated */");
                        f.WriteLine();
                    }

                    if (file.IsEnum)
                    {

                        f.WriteLine(
                            "export enum "
                            + file.Name
                            // + (String.IsNullOrEmpty(file.Inherits) ? "" : (" : " + file.Inherits)) //typescript doesn't extend enums like c#
                            + " {"
                            );

                        foreach (var obj in file.EnumObjects)
                        {
                            if (!String.IsNullOrEmpty(obj.Name))
                            {  //not an empty obj
                                var tsName = ToCamelCase(obj.Name);
                                var str = tsName;
                                if (EnumValues == EnumValues.Strings)
                                {
                                    str += " = '" + tsName + "'";
                                }
                                else if (!obj.IsImplicit)
                                {
                                    str += " = " + obj.Value;
                                }
                                str += ",";

                                f.WriteLine("    " + str);
                            }
                        }

                        f.WriteLine("}");

                    }
                    else
                    {

                        foreach (var obj in file.Objects)
                        {
                            if (!String.IsNullOrEmpty(file.Inherits))
                            {
                                importing = true;
                                var import = "import { " + file.Inherits + " } from \"" + file.InheritenceStructure + "\"";

                                if (!imports.Contains(import))
                                {
                                    f.WriteLine(import);
                                    imports.Add(import);
                                }
                            }

                            if (obj.UserDefined)
                            {
                                importing = true;
                                var import = "import { " + obj.Type + " } from \"" + obj.UserDefinedImport + "\"";

                                if (!imports.Contains(import))
                                {
                                    f.WriteLine(import);
                                    imports.Add(import);
                                }
                            }
                        }

                        if (importing)
                        {
                            f.WriteLine("");
                        }

                        f.WriteLine(
                            "export interface "
                            + file.Name
                            + (String.IsNullOrEmpty(file.Inherits) ? "" : (" extends " + file.Inherits)) //if class has inheritance
                            + " {"
                            );

                        foreach (var obj in file.Objects)
                        {
                            if (!String.IsNullOrEmpty(obj.VariableName))
                            {  //not an empty obj
                                var str =
                                    ToCamelCase(obj.VariableName)
                                    + (obj.IsOptional ? "?" : String.Empty)
                                    + ": "
                                    + obj.Type
                                    + (obj.IsArray ? "[]" : String.Empty)
                                    + ";";

                                f.WriteLine("    " + str);
                            }
                        }
                        f.WriteLine("}");
                    }
                }
            }
        }

        private string ToCamelCase(string str)
        {
            if (String.IsNullOrEmpty(str) || Char.IsLower(str, 0))
                return str;

            bool isCaps = true;

            foreach (var c in str)
            {
                if (Char.IsLetter(c) && Char.IsLower(c))
                    isCaps = false;
            }

            if (isCaps) return str.ToLower();

            return Char.ToLowerInvariant(str[0]) + str.Substring(1);
        }

        private string ToPascalCase(string str)
        {
            if (String.IsNullOrEmpty(str) || Char.IsUpper(str, 0))
                return str;

            return Char.ToUpperInvariant(str[0]) + str.Substring(1);
        }

        private bool CheckIsArray(string type)
        {
            return type.Contains("[]") ||
                type.Contains("ICollection") ||
                type.Contains("IEnumerable") ||
                type.Contains("Array") ||
                type.Contains("Enumerable") ||
                type.Contains("Collection");
        }

        private bool CheckOptional(string type)
        {
            return type.Contains("?");
        }

        private string CleanType(string type)
        {
            return type.Replace("?", String.Empty)
                .Replace("[]", String.Empty)
                .Replace("ICollection", String.Empty)
                .Replace("IEnumerable", String.Empty)
                .Replace("<", String.Empty)
                .Replace(">", String.Empty);
        }

        private bool IsContructor(string line)
        {
            return line.Contains("()") || ((line.Contains("(") && line.Contains(")")));
        }

        private bool IsEnumObject(string line)
        {
            return 
                !String.IsNullOrWhiteSpace(line)
                && !line.StrictContains("enum")
                && !line.StrictContains("namespace")
                && !line.StrictContains("using")
                && !IsContructor(line) 
                && !line.Contains("{") && !line.Contains("}")
                && !line.Contains("[") && !line.Contains("]");
        }

        private string[] ExplodeLine(string line)
        {
            var regex = new Regex("\\s*,\\s*");

            var l = regex.Replace(line, ",");

            return l
                .Replace("public", String.Empty)
                .Replace("static", String.Empty)
                .Replace("const", String.Empty)
                .Replace("readonly", String.Empty)
                .Trim()
                .Split(' ');
        }

        private string StripComments(string line)
        {
            if (line.Contains("//"))
            {
                line = line.Substring(0, line.IndexOf("//"));
            }

            return line;
        }

        private string GetRelativePath(string from, string to)
        {
            Uri path1 = new Uri(from.Replace("\\", "/"));
            Uri path2 = new Uri(to.Replace("\\", "/"));

            var rel = path1.MakeRelativeUri(path2);

            return Uri.UnescapeDataString(rel.OriginalString);
        }

        private string GetRelativePathFromLocalPath(string from, string to)
        {
            var path1 = Path.Combine(LocalWorkingDir, from);
            var path2 = Path.Combine(LocalWorkingDir, to);
            path1 = path1.Replace("/", "\\");
            path2 = path2.Replace("/", "\\");

            if (!String.Equals(path1.Substring(path1.Length - 1), "\\"))
            {
                path1 = path1 + "\\";
            }

            if (!String.Equals(path2.Substring(path2.Length - 1), "\\"))
            {
                path2 = path2 + "\\";
            }

            var rel = GetRelativePath(path1, path2).Replace("\\", "/");

            if (!String.Equals(rel.Substring(0), "."))
            {
                rel = "./" + rel;
            }

            return rel;
        }

        private string Find(string query, ModelFile file)
        {
            string userDefinedImport = null;

            foreach (var f in Models)
            {
                if (f.Name.Equals(query))
                {
                    var rel = GetRelativePathFromLocalPath(file.Structure, f.Structure);

                    return rel + ToCamelCase(f.Name);
                }
            }

            return userDefinedImport;
        }
    }

    public static class StringExtension
    {
        public static bool StrictContains(this string str, string match)
        {
            string reg = "(^|\\s)" + match + "(\\s|$)";
            return Regex.IsMatch(str, reg);
        }

        public static bool IsPreProcessorDirective(this string str)
        {
            return Regex.IsMatch(str, @"^#\w+");
        }
    }
}
