using MTT;
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Framework;
using MSBuildTask = Microsoft.Build.Utilities.Task;
using System.Text.RegularExpressions;

namespace MSBuildTasks
{
    public class ConvertMain : MSBuildTask
    {
        /// <summary>
        /// The current working directory for the convert process
        /// </summary>
        public string WorkingDirectory { get; set; }

        /// <summary>
        /// The directory to save the ts models
        /// </summary>
        public string ConvertDirectory { get; set; }

        private PathStyle _pathStyle = MTT.PathStyle.Default;

        /// <summary>
        /// Determines the naming style of the generated files and folders
        /// </summary>
        public string PathStyle
        {
            get => _pathStyle.ToString();
            set => _pathStyle = (PathStyle)Enum.Parse(typeof(PathStyle), value);
        }

        /// <summary>
        /// Comments at the top of each file that it was auto generated
        /// </summary>
        public bool AutoGeneratedTag { get; set; } = true; //default value if one is not provided;

        private EnumValues _enumValues = MTT.EnumValues.Default;

        /// <summary>
        /// Determines whether to generate numeric or string values in typescript enums
        /// </summary>
        public string EnumValues
        {
            get => _enumValues.ToString();
            set => _enumValues = (EnumValues)Enum.Parse(typeof(EnumValues), value);
        }

        protected MessageImportance LoggingImportance { get; } = MessageImportance.High;  //If its not high then there are no logs

        private readonly ConvertService convertService;

        public ConvertMain()
        {
            convertService = new ConvertService(Log.LogMessage);
        }

        public override bool Execute()
        {
            Log.LogMessage(LoggingImportance, "Starting MTT");

            convertService.AutoGeneratedTag = AutoGeneratedTag;
            convertService.ConvertDirectory = ConvertDirectory;
            convertService.WorkingDirectory = WorkingDirectory;
            convertService.EnumValues = _enumValues;
            convertService.PathStyle = _pathStyle;

            var result = convertService.Execute();

            Log.LogMessage(LoggingImportance, "Finished MTT");
            return result;
        }
    }
}
